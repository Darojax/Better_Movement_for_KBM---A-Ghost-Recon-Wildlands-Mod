using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

if (args.Length != 4 || args[0] != "--pid" || args[2] != "--instruction" ||
    !int.TryParse(args[1], out int pid))
{
    Console.Error.WriteLine("Usage: GRWStackTrace --pid 1234 --instruction 0xABCDEF");
    return 2;
}

string addressText = args[3].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[3][2..] : args[3];
if (!ulong.TryParse(addressText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong instruction)) return 2;

const uint ExceptionDebugEvent = 1, CreateThreadDebugEvent = 2, CreateProcessDebugEvent = 3, ExitProcessDebugEvent = 5;
const uint ExceptionSingleStep = 0x80000004, DbgContinue = 0x00010002, DbgExceptionNotHandled = 0x80010001;
const uint ThreadGetContext = 0x0008, ThreadSetContext = 0x0010, ThreadQueryInformation = 0x0040;
const uint ProcessVmRead = 0x0010, ProcessQueryInformation = 0x0400;
const uint ContextDebugRegisters = 0x00100010, ContextDebugAndControl = 0x00100011;
const int ContextSize = 1232, ContextFlagsOffset = 48, Dr0Offset = 72, Dr1Offset = 80, Dr6Offset = 104, Dr7Offset = 112;
const int RspOffset = 152, RipOffset = 248;

using Process target = Process.GetProcessById(pid);
nint readHandle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid);
if (readHandle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
nint debugEvent = Marshal.AllocHGlobal(176);
bool attached = false;
ulong activeAddress = instruction;
bool watchingStack = false;
HashSet<ulong> stackWriters = [];

try
{
    if (!DebugActiveProcess(pid)) throw new Win32Exception(Marshal.GetLastWin32Error(), "DebugActiveProcess failed");
    attached = true;
    if (!DebugSetProcessKillOnExit(false)) throw new Win32Exception(Marshal.GetLastWin32Error());
    Console.WriteLine($"Attached to PID {pid}; waiting for two executions of 0x{instruction:X16}.");
    Stopwatch timeout = Stopwatch.StartNew();

    while (timeout.Elapsed < TimeSpan.FromSeconds(120))
    {
        if (!WaitForDebugEvent(debugEvent, 500))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 121) continue;
            throw new Win32Exception(error);
        }

        uint eventCode = unchecked((uint)Marshal.ReadInt32(debugEvent, 0));
        int eventPid = Marshal.ReadInt32(debugEvent, 4), threadId = Marshal.ReadInt32(debugEvent, 8);
        uint status = DbgContinue;
        bool finished = false;
        ulong nextRip = 0;

        if (eventCode is CreateProcessDebugEvent or CreateThreadDebugEvent)
        {
            if (watchingStack) SetAccessBreakpoint(threadId, activeAddress);
            else SetBreakpoint(threadId, instruction, writeFourBytes: false);
            if (eventCode == CreateProcessDebugEvent)
            {
                if (watchingStack) SetAllAccess(target, activeAddress);
                else SetAll(target, instruction, writeFourBytes: false);
            }
        }
        else if (eventCode == ExceptionDebugEvent)
        {
            uint exceptionCode = unchecked((uint)Marshal.ReadInt32(debugEvent, 16));
            if (exceptionCode == ExceptionSingleStep && TryGetState(threadId, out ulong dr6, out ulong rip, out ulong rsp))
            {
                if ((dr6 & 1) != 0)
                {
                    if (!watchingStack)
                    {
                        activeAddress = rsp + 0x30;
                        watchingStack = true;
                        SetAllAccess(target, activeAddress);
                        Console.WriteLine($"Writer execution on thread {threadId}; tracing accesses to 0x{activeAddress:X16} until the next known scalar load.");
                    }
                    else if (rip == instruction - 8)
                    {
                        nextRip = rip;
                        ClearAllDebugBreakpoints(target);
                        finished = true;
                    }
                    else if (stackWriters.Add(rip))
                    {
                        byte[] sample = new byte[48];
                        ReadProcessMemory(readHandle, (nint)(rip - 16), sample, (nuint)sample.Length, out _);
                        Console.WriteLine($"Stack access #{stackWriters.Count}: next RIP 0x{rip:X16}; bytes(-16..+31) {Convert.ToHexString(sample)}");
                    }
                    SetAllAccess(target, activeAddress);
                }
            }
            else if (exceptionCode != 0x80000003) status = DbgExceptionNotHandled;
        }
        else if (eventCode == ExitProcessDebugEvent) return 4;

        if (!ContinueDebugEvent(eventPid, threadId, status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (finished)
        {
            byte[] bytes = new byte[64];
            ReadProcessMemory(readHandle, (nint)(nextRip - 24), bytes, (nuint)bytes.Length, out _);
            Console.WriteLine($"Next scalar load reached on thread {threadId}; collected {stackWriters.Count} unique intervening stack accesses.");
            if (DebugActiveProcessStop(pid)) attached = false;
            Console.WriteLine("Breakpoint cleared and debugger detached.");
            return 0;
        }
    }
    Console.WriteLine("Timed out; clearing breakpoint and detaching.");
    ClearAllDebugBreakpoints(target);
    if (DebugActiveProcessStop(pid)) attached = false;
    return 5;
}
finally
{
    if (attached)
    {
        try { ClearAllDebugBreakpoints(target); } catch { }
        DebugActiveProcessStop(pid);
    }
    Marshal.FreeHGlobal(debugEvent);
    CloseHandle(readHandle);
}

void SetAll(Process process, ulong address, bool writeFourBytes, bool disable = false)
{
    process.Refresh();
    foreach (ProcessThread thread in process.Threads)
        try { SetBreakpoint(thread.Id, address, writeFourBytes, disable); } catch (Win32Exception) { }
}

void SetBreakpoint(int threadId, ulong address, bool writeFourBytes, bool disable = false)
{
    nint thread = OpenThread(ThreadGetContext | ThreadSetContext | ThreadQueryInformation, false, threadId);
    if (thread == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    try
    {
        WithContext(context =>
        {
            Marshal.WriteInt32(context, ContextFlagsOffset, unchecked((int)ContextDebugAndControl));
            if (!GetThreadContext(thread, context)) throw new Win32Exception(Marshal.GetLastWin32Error());
            ulong dr7 = unchecked((ulong)Marshal.ReadInt64(context, Dr7Offset));
            dr7 &= ~((ulong)3 | ((ulong)0xF << 16));
            Marshal.WriteInt64(context, Dr0Offset, disable ? 0 : unchecked((long)address));
            Marshal.WriteInt64(context, Dr6Offset, 0);
            if (!disable)
            {
                dr7 |= 1UL;
                if (writeFourBytes) dr7 |= (1UL << 16) | (3UL << 18);
            }
            Marshal.WriteInt64(context, Dr7Offset, unchecked((long)dr7));
            Marshal.WriteInt32(context, ContextFlagsOffset, unchecked((int)ContextDebugRegisters));
            if (!SetThreadContext(thread, context)) throw new Win32Exception(Marshal.GetLastWin32Error());
        });
    }
    finally { CloseHandle(thread); }
}

void SetAllDual(Process process, ulong executeAddress, ulong writeAddress)
{
    process.Refresh();
    foreach (ProcessThread thread in process.Threads)
        try { SetDualBreakpoint(thread.Id, executeAddress, writeAddress); } catch (Win32Exception) { }
}

void SetDualBreakpoint(int threadId, ulong executeAddress, ulong writeAddress)
{
    nint thread = OpenThread(ThreadGetContext | ThreadSetContext | ThreadQueryInformation, false, threadId);
    if (thread == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    try
    {
        WithContext(context =>
        {
            Marshal.WriteInt32(context, ContextFlagsOffset, unchecked((int)ContextDebugAndControl));
            if (!GetThreadContext(thread, context)) throw new Win32Exception(Marshal.GetLastWin32Error());
            ulong dr7 = unchecked((ulong)Marshal.ReadInt64(context, Dr7Offset));
            dr7 &= ~((ulong)0xF | ((ulong)0xFF << 16));
            Marshal.WriteInt64(context, Dr0Offset, unchecked((long)executeAddress));
            Marshal.WriteInt64(context, Dr1Offset, unchecked((long)writeAddress));
            Marshal.WriteInt64(context, Dr6Offset, 0);
            dr7 |= 1UL | (1UL << 2); // enable DR0 and DR1 locally
            dr7 |= (1UL << 20) | (3UL << 22); // DR1: write, four bytes
            Marshal.WriteInt64(context, Dr7Offset, unchecked((long)dr7));
            Marshal.WriteInt32(context, ContextFlagsOffset, unchecked((int)ContextDebugRegisters));
            if (!SetThreadContext(thread, context)) throw new Win32Exception(Marshal.GetLastWin32Error());
        });
    }
    finally { CloseHandle(thread); }
}

void SetAllAccess(Process process, ulong address)
{
    process.Refresh();
    foreach (ProcessThread thread in process.Threads)
        try { SetAccessBreakpoint(thread.Id, address); } catch (Win32Exception) { }
}

void SetAccessBreakpoint(int threadId, ulong address)
{
    nint thread = OpenThread(ThreadGetContext | ThreadSetContext | ThreadQueryInformation, false, threadId);
    if (thread == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    try
    {
        WithContext(context =>
        {
            Marshal.WriteInt32(context, ContextFlagsOffset, unchecked((int)ContextDebugAndControl));
            if (!GetThreadContext(thread, context)) throw new Win32Exception(Marshal.GetLastWin32Error());
            ulong dr7 = unchecked((ulong)Marshal.ReadInt64(context, Dr7Offset));
            dr7 &= ~((ulong)0xF | ((ulong)0xFF << 16));
            Marshal.WriteInt64(context, Dr0Offset, unchecked((long)address));
            Marshal.WriteInt64(context, Dr1Offset, 0);
            Marshal.WriteInt64(context, Dr6Offset, 0);
            dr7 |= 1UL | (3UL << 16) | (3UL << 18); // DR0: read/write, four bytes
            Marshal.WriteInt64(context, Dr7Offset, unchecked((long)dr7));
            Marshal.WriteInt32(context, ContextFlagsOffset, unchecked((int)ContextDebugRegisters));
            if (!SetThreadContext(thread, context)) throw new Win32Exception(Marshal.GetLastWin32Error());
        });
    }
    finally { CloseHandle(thread); }
}

void ClearAllDebugBreakpoints(Process process)
{
    process.Refresh();
    foreach (ProcessThread thread in process.Threads)
    {
        try
        {
            nint handle = OpenThread(ThreadGetContext | ThreadSetContext | ThreadQueryInformation, false, thread.Id);
            if (handle == 0) continue;
            try
            {
                WithContext(context =>
                {
                    Marshal.WriteInt32(context, ContextFlagsOffset, unchecked((int)ContextDebugAndControl));
                    if (!GetThreadContext(handle, context)) return;
                    Marshal.WriteInt64(context, Dr0Offset, 0);
                    Marshal.WriteInt64(context, Dr1Offset, 0);
                    Marshal.WriteInt64(context, Dr6Offset, 0);
                    ulong dr7 = unchecked((ulong)Marshal.ReadInt64(context, Dr7Offset));
                    Marshal.WriteInt64(context, Dr7Offset, unchecked((long)(dr7 & ~((ulong)0xF | ((ulong)0xFF << 16)))));
                    Marshal.WriteInt32(context, ContextFlagsOffset, unchecked((int)ContextDebugRegisters));
                    SetThreadContext(handle, context);
                });
            }
            finally { CloseHandle(handle); }
        }
        catch (Win32Exception) { }
    }
}

bool TryGetState(int threadId, out ulong dr6, out ulong rip, out ulong rsp)
{
    ulong a = 0, b = 0, c = 0; bool success = false;
    nint thread = OpenThread(ThreadGetContext | ThreadQueryInformation, false, threadId);
    if (thread != 0)
    {
        try { WithContext(ctx => { Marshal.WriteInt32(ctx, ContextFlagsOffset, unchecked((int)ContextDebugAndControl)); success = GetThreadContext(thread, ctx); if (success) { a = (ulong)Marshal.ReadInt64(ctx, Dr6Offset); b = (ulong)Marshal.ReadInt64(ctx, RipOffset); c = (ulong)Marshal.ReadInt64(ctx, RspOffset); } }); }
        finally { CloseHandle(thread); }
    }
    dr6 = a; rip = b; rsp = c; return success;
}

void WithContext(Action<nint> action)
{
    nint raw = Marshal.AllocHGlobal(ContextSize + 16);
    try { nint aligned = (nint)((raw.ToInt64() + 15) & ~15L); for (int i = 0; i < ContextSize; i += 8) Marshal.WriteInt64(aligned, i, 0); action(aligned); }
    finally { Marshal.FreeHGlobal(raw); }
}

(ulong Start, byte PrologSize) FindContainingFunction(nint process, Process processInfo, ulong address)
{
    ulong imageBase = unchecked((ulong)(processInfo.MainModule?.BaseAddress.ToInt64() ?? 0));
    if (imageBase == 0 || address < imageBase) throw new InvalidOperationException("Could not resolve the image base.");
    byte[] dos = ReadBytes(process, imageBase, 0x40);
    int peOffset = BitConverter.ToInt32(dos, 0x3C);
    byte[] headers = ReadBytes(process, imageBase + (ulong)peOffset, 0xB0);
    if (BitConverter.ToUInt32(headers, 0) != 0x00004550 || BitConverter.ToUInt16(headers, 24) != 0x20B)
        throw new InvalidOperationException("Unexpected PE headers.");
    uint exceptionRva = BitConverter.ToUInt32(headers, 24 + 112 + 3 * 8);
    uint exceptionSize = BitConverter.ToUInt32(headers, 24 + 112 + 3 * 8 + 4);
    byte[] entries = ReadBytes(process, imageBase + exceptionRva, checked((int)exceptionSize));
    uint targetRva = checked((uint)(address - imageBase));
    for (int offset = 0; offset + 12 <= entries.Length; offset += 12)
    {
        uint begin = BitConverter.ToUInt32(entries, offset);
        uint end = BitConverter.ToUInt32(entries, offset + 4);
        if (targetRva < begin) break;
        if (targetRva >= begin && targetRva < end)
        {
            uint unwindRva = BitConverter.ToUInt32(entries, offset + 8) & ~1U;
            byte[] unwind = ReadBytes(process, imageBase + unwindRva, 2);
            return (imageBase + begin, unwind[1]);
        }
    }
    throw new InvalidOperationException("No x64 runtime-function entry contains the writer.");
}

byte[] ReadBytes(nint process, ulong address, int count)
{
    byte[] bytes = new byte[count];
    if (!ReadProcessMemory(process, (nint)address, bytes, (nuint)count, out nuint read) || read != (nuint)count)
        throw new Win32Exception(Marshal.GetLastWin32Error(), $"ReadProcessMemory failed at 0x{address:X16}");
    return bytes;
}

[DllImport("kernel32.dll", SetLastError = true)] static extern bool DebugSetProcessKillOnExit(bool value);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool DebugActiveProcess(int pid);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool DebugActiveProcessStop(int pid);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool WaitForDebugEvent(nint debugEvent, uint ms);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool ContinueDebugEvent(int pid, int tid, uint status);
[DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenThread(uint access, bool inherit, int tid);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool GetThreadContext(nint thread, nint context);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool SetThreadContext(nint thread, nint context);
[DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, int pid);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
[DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
