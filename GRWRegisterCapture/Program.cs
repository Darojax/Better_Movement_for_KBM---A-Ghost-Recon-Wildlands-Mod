using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

if (args.Length != 4 || args[0] != "--pid" || args[2] != "--execute" || !int.TryParse(args[1], out int pid))
{
    Console.Error.WriteLine("Usage: GRWRegisterCapture --pid 1234 --execute 0xABCDEF");
    return 2;
}
string text = args[3].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[3][2..] : args[3];
if (!ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong execute)) return 2;

const uint ExceptionDebugEvent = 1, CreateThreadDebugEvent = 2, CreateProcessDebugEvent = 3, ExitProcessDebugEvent = 5;
const uint ExceptionSingleStep = 0x80000004, DbgContinue = 0x00010002, DbgExceptionNotHandled = 0x80010001;
const uint ThreadGetContext = 0x0008, ThreadSetContext = 0x0010, ThreadQueryInformation = 0x0040;
const int ContextSize = 1232, ContextFlagsOffset = 48, Dr0Offset = 72, Dr6Offset = 104, Dr7Offset = 112;
const int RipOffset = 248, RbxOffset = 144, RsiOffset = 168, RdiOffset = 176;
const uint ContextDebugRegisters = 0x00100010, ContextDebugControlInteger = 0x00100013;

using Process target = Process.GetProcessById(pid);
nint debugEvent = Marshal.AllocHGlobal(176);
bool attached = false;
try
{
    if (!DebugActiveProcess(pid)) throw new Win32Exception(Marshal.GetLastWin32Error(), "DebugActiveProcess failed");
    attached = true;
    if (!DebugSetProcessKillOnExit(false)) throw new Win32Exception(Marshal.GetLastWin32Error());
    Console.WriteLine($"Attached to PID {pid}; execute watch at 0x{execute:X16}.");
    Stopwatch timeout = Stopwatch.StartNew();
    while (timeout.Elapsed < TimeSpan.FromSeconds(120))
    {
        if (!WaitForDebugEvent(debugEvent, 500))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 121) continue;
            throw new Win32Exception(error);
        }
        uint code = unchecked((uint)Marshal.ReadInt32(debugEvent, 0));
        int eventPid = Marshal.ReadInt32(debugEvent, 4), tid = Marshal.ReadInt32(debugEvent, 8);
        uint status = DbgContinue;
        bool hit = false;
        ulong rip = 0, rbx = 0, rsi = 0, rdi = 0;
        if (code is CreateProcessDebugEvent or CreateThreadDebugEvent)
        {
            SetExecute(tid, execute, true);
            if (code == CreateProcessDebugEvent) SetAll(target, execute, true);
        }
        else if (code == ExceptionDebugEvent)
        {
            uint exception = unchecked((uint)Marshal.ReadInt32(debugEvent, 16));
            if (exception == ExceptionSingleStep && TryGetState(tid, out ulong dr6, out rip, out rbx, out rsi, out rdi) && (dr6 & 1) != 0)
            {
                hit = true;
                SetAll(target, execute, false);
            }
            else if (exception != 0x80000003) status = DbgExceptionNotHandled;
        }
        else if (code == ExitProcessDebugEvent) return 4;
        if (!ContinueDebugEvent(eventPid, tid, status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (hit)
        {
            Console.WriteLine($"EXEC HIT: RIP 0x{rip:X16}; RBX 0x{rbx:X16}; RSI 0x{rsi:X16}; RDI 0x{rdi:X16}; thread {tid}.");
            if (DebugActiveProcessStop(pid)) attached = false;
            Console.WriteLine("Breakpoint cleared and debugger detached.");
            return 0;
        }
    }
    SetAll(target, execute, false);
    if (DebugActiveProcessStop(pid)) attached = false;
    Console.WriteLine("Timed out; breakpoint cleared and debugger detached.");
    return 5;
}
finally
{
    if (attached) { try { SetAll(target, execute, false); } catch { } DebugActiveProcessStop(pid); }
    Marshal.FreeHGlobal(debugEvent);
}

void SetAll(Process process, ulong address, bool enabled)
{
    process.Refresh();
    foreach (ProcessThread thread in process.Threads) try { SetExecute(thread.Id, address, enabled); } catch (Win32Exception) { }
}
void SetExecute(int tid, ulong address, bool enabled)
{
    nint thread = OpenThread(ThreadGetContext | ThreadSetContext | ThreadQueryInformation, false, tid);
    if (thread == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    try
    {
        WithContext(ctx =>
        {
            Marshal.WriteInt32(ctx, ContextFlagsOffset, unchecked((int)ContextDebugControlInteger));
            if (!GetThreadContext(thread, ctx)) throw new Win32Exception(Marshal.GetLastWin32Error());
            ulong dr7 = unchecked((ulong)Marshal.ReadInt64(ctx, Dr7Offset));
            dr7 &= ~((ulong)3 | ((ulong)0xF << 16));
            Marshal.WriteInt64(ctx, Dr0Offset, enabled ? unchecked((long)address) : 0);
            Marshal.WriteInt64(ctx, Dr6Offset, 0);
            if (enabled) dr7 |= 1;
            Marshal.WriteInt64(ctx, Dr7Offset, unchecked((long)dr7));
            Marshal.WriteInt32(ctx, ContextFlagsOffset, unchecked((int)ContextDebugRegisters));
            if (!SetThreadContext(thread, ctx)) throw new Win32Exception(Marshal.GetLastWin32Error());
        });
    }
    finally { CloseHandle(thread); }
}
bool TryGetState(int tid, out ulong dr6, out ulong rip, out ulong rbx, out ulong rsi, out ulong rdi)
{
    ulong a = 0, b = 0, c = 0, d = 0, e = 0; bool ok = false;
    nint thread = OpenThread(ThreadGetContext | ThreadQueryInformation, false, tid);
    if (thread != 0)
    {
        try { WithContext(ctx => { Marshal.WriteInt32(ctx, ContextFlagsOffset, unchecked((int)ContextDebugControlInteger)); ok = GetThreadContext(thread, ctx); if (ok) { a = (ulong)Marshal.ReadInt64(ctx, Dr6Offset); b = (ulong)Marshal.ReadInt64(ctx, RipOffset); c = (ulong)Marshal.ReadInt64(ctx, RbxOffset); d = (ulong)Marshal.ReadInt64(ctx, RsiOffset); e = (ulong)Marshal.ReadInt64(ctx, RdiOffset); } }); }
        finally { CloseHandle(thread); }
    }
    dr6 = a; rip = b; rbx = c; rsi = d; rdi = e; return ok;
}
void WithContext(Action<nint> action)
{
    nint raw = Marshal.AllocHGlobal(ContextSize + 16);
    try { nint aligned = (nint)((raw.ToInt64() + 15) & ~15L); for (int i = 0; i < ContextSize; i += 8) Marshal.WriteInt64(aligned, i, 0); action(aligned); }
    finally { Marshal.FreeHGlobal(raw); }
}

[DllImport("kernel32.dll", SetLastError = true)] static extern bool DebugSetProcessKillOnExit(bool value);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool DebugActiveProcess(int pid);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool DebugActiveProcessStop(int pid);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool WaitForDebugEvent(nint debugEvent, uint ms);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool ContinueDebugEvent(int pid, int tid, uint status);
[DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenThread(uint access, bool inherit, int tid);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool GetThreadContext(nint thread, nint context);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool SetThreadContext(nint thread, nint context);
[DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
