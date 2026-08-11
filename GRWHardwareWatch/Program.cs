using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace GRWHardwareWatch;

internal static class Program
{
    private const uint ExceptionDebugEvent = 1;
    private const uint CreateThreadDebugEvent = 2;
    private const uint CreateProcessDebugEvent = 3;
    private const uint ExitProcessDebugEvent = 5;
    private const uint ExceptionSingleStep = 0x80000004;
    private const uint DbgContinue = 0x00010002;
    private const uint DbgExceptionNotHandled = 0x80010001;
    private const uint ThreadGetContext = 0x0008;
    private const uint ThreadSetContext = 0x0010;
    private const uint ThreadQueryInformation = 0x0040;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const int ContextSize = 1232;
    private const int ContextFlagsOffset = 48;
    private const int Dr0Offset = 72;
    private const int Dr6Offset = 104;
    private const int Dr7Offset = 112;
    private const int RipOffset = 248;
    private const uint ContextDebugRegisters = 0x00100010;
    private const uint ContextDebugAndControl = 0x00100011;

    public static int Main(string[] args)
    {
        if (!TryParse(args, out int processId, out ulong watchedAddress, out int timeoutSeconds, out float? desiredValue, out bool accessMode, out HashSet<ulong> ignoredNextRips))
        {
            Console.Error.WriteLine("Usage: GRWHardwareWatch --pid 1234 --address 0xABCDEF00 [--timeout 120] [--value 0.3] [--access 1] [--ignore-next-rip 0xABCDEF]");
            return 2;
        }

        if ((watchedAddress & 3) != 0)
        {
            Console.Error.WriteLine("The watched four-byte address must be four-byte aligned.");
            return 3;
        }

        using Process target = Process.GetProcessById(processId);
        nint readHandle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, processId);
        if (readHandle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed");

        bool attached = false;
        nint debugEvent = Marshal.AllocHGlobal(176);
        try
        {
            if (!DebugActiveProcess(processId))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DebugActiveProcess failed");
            attached = true;
            if (!DebugSetProcessKillOnExit(false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DebugSetProcessKillOnExit(false) failed");

            Console.WriteLine($"Attached to PID {processId}; kill-on-watcher-exit is disabled.");
            Console.WriteLine($"Watching 0x{watchedAddress:X16} for a four-byte {(accessMode ? "access" : "write")}.");
            if (desiredValue.HasValue) Console.WriteLine($"Ignoring writes until the resulting float is {desiredValue.Value:R}.");
            if (ignoredNextRips.Count != 0) Console.WriteLine("Ignoring next RIPs: " + string.Join(", ", ignoredNextRips.Select(rip => $"0x{rip:X16}")));
            Stopwatch timeout = Stopwatch.StartNew();
            int ignoredHits = 0;

            while (timeout.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
            {
                if (!WaitForDebugEvent(debugEvent, 500))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 121) continue; // ERROR_SEM_TIMEOUT
                    throw new Win32Exception(error, "WaitForDebugEvent failed");
                }

                uint eventCode = unchecked((uint)Marshal.ReadInt32(debugEvent, 0));
                int eventProcessId = Marshal.ReadInt32(debugEvent, 4);
                int threadId = Marshal.ReadInt32(debugEvent, 8);
                uint continueStatus = DbgContinue;
                bool hit = false;
                ulong instructionAfterWrite = 0;

                if (eventCode is CreateProcessDebugEvent or CreateThreadDebugEvent)
                {
                    SetHardwareWriteBreakpoint(threadId, watchedAddress, enabled: true, accessMode);
                    if (eventCode == CreateProcessDebugEvent)
                    {
                        SetBreakpointOnAllThreads(target, watchedAddress, enabled: true, accessMode);
                    }
                }
                else if (eventCode == ExceptionDebugEvent)
                {
                    uint exceptionCode = unchecked((uint)Marshal.ReadInt32(debugEvent, 16));
                    if (exceptionCode == ExceptionSingleStep && TryGetDebugState(threadId, out ulong dr6, out ulong rip) && (dr6 & 1) != 0)
                    {
                        float resultingValue = ReadFloat(readHandle, watchedAddress);
                        bool matches = !ignoredNextRips.Contains(rip) && (!desiredValue.HasValue ||
                            Math.Abs(resultingValue - desiredValue.Value) <= Math.Max(0.0001f, Math.Abs(desiredValue.Value) * 0.001f));
                        if (matches)
                        {
                            hit = true;
                            instructionAfterWrite = rip;
                            SetBreakpointOnAllThreads(target, watchedAddress, enabled: false, accessMode);
                        }
                        else
                        {
                            ignoredHits++;
                            if (ignoredHits <= 5) Console.WriteLine($"Ignored hit #{ignoredHits}: next RIP 0x{rip:X16}, resulting float {resultingValue:R}.");
                            SetBreakpointOnAllThreads(target, watchedAddress, enabled: true, accessMode);
                        }
                    }
                    else if (exceptionCode != 0x80000003) // initial attach breakpoint
                    {
                        continueStatus = DbgExceptionNotHandled;
                    }
                }
                else if (eventCode == ExitProcessDebugEvent)
                {
                    Console.Error.WriteLine("Target exited while being watched.");
                }

                if (!ContinueDebugEvent(eventProcessId, threadId, continueStatus))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "ContinueDebugEvent failed");

                if (hit)
                {
                    byte[] bytes = ReadAround(readHandle, instructionAfterWrite);
                    Console.WriteLine($"WRITE HIT: next RIP 0x{instructionAfterWrite:X16}, thread {threadId}.");
                    Console.WriteLine("Bytes around next RIP (-16..+31): " + Convert.ToHexString(bytes));
                    if (DebugActiveProcessStop(processId)) attached = false;
                    Console.WriteLine("Hardware breakpoint cleared and debugger detached.");
                    return 0;
                }

                if (eventCode == ExitProcessDebugEvent) return 4;
            }

            Console.WriteLine("Timed out without a matching write; clearing breakpoint and detaching.");
            SetBreakpointOnAllThreads(target, watchedAddress, enabled: false, accessMode);
            if (DebugActiveProcessStop(processId)) attached = false;
            return 5;
        }
        finally
        {
            if (attached)
            {
                try { SetBreakpointOnAllThreads(target, watchedAddress, enabled: false, accessMode); } catch { }
                DebugActiveProcessStop(processId);
            }
            Marshal.FreeHGlobal(debugEvent);
            CloseHandle(readHandle);
        }
    }

    private static void SetBreakpointOnAllThreads(Process process, ulong address, bool enabled, bool accessMode)
    {
        process.Refresh();
        foreach (ProcessThread thread in process.Threads)
        {
            try { SetHardwareWriteBreakpoint(thread.Id, address, enabled, accessMode); } catch (Win32Exception) { }
        }
    }

    private static void SetHardwareWriteBreakpoint(int threadId, ulong address, bool enabled, bool accessMode)
    {
        nint thread = OpenThread(ThreadGetContext | ThreadSetContext | ThreadQueryInformation, false, threadId);
        if (thread == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), $"OpenThread({threadId}) failed");
        try
        {
            WithAlignedContext(context =>
            {
                Marshal.WriteInt32(context, ContextFlagsOffset, unchecked((int)ContextDebugAndControl));
                if (!GetThreadContext(thread, context))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"GetThreadContext({threadId}) failed");

                ulong dr7 = unchecked((ulong)Marshal.ReadInt64(context, Dr7Offset));
                dr7 &= ~((ulong)3 | ((ulong)0xF << 16));
                Marshal.WriteInt64(context, Dr0Offset, enabled ? unchecked((long)address) : 0);
                Marshal.WriteInt64(context, Dr6Offset, 0);
                if (enabled) dr7 |= 1UL | ((accessMode ? 3UL : 1UL) << 16) | (3UL << 18); // local, read/write or write, 4 bytes
                Marshal.WriteInt64(context, Dr7Offset, unchecked((long)dr7));
                Marshal.WriteInt32(context, ContextFlagsOffset, unchecked((int)ContextDebugRegisters));
                if (!SetThreadContext(thread, context))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"SetThreadContext({threadId}) failed");
            });
        }
        finally { CloseHandle(thread); }
    }

    private static bool TryGetDebugState(int threadId, out ulong dr6, out ulong rip)
    {
        dr6 = rip = 0;
        ulong localDr6 = 0;
        ulong localRip = 0;
        nint thread = OpenThread(ThreadGetContext | ThreadQueryInformation, false, threadId);
        if (thread == 0) return false;
        try
        {
            bool success = false;
            WithAlignedContext(context =>
            {
                Marshal.WriteInt32(context, ContextFlagsOffset, unchecked((int)ContextDebugAndControl));
                success = GetThreadContext(thread, context);
                if (success)
                {
                    localDr6 = unchecked((ulong)Marshal.ReadInt64(context, Dr6Offset));
                    localRip = unchecked((ulong)Marshal.ReadInt64(context, RipOffset));
                }
            });
            dr6 = localDr6;
            rip = localRip;
            return success;
        }
        finally { CloseHandle(thread); }
    }

    private static void WithAlignedContext(Action<nint> action)
    {
        nint raw = Marshal.AllocHGlobal(ContextSize + 16);
        try
        {
            long aligned = (raw.ToInt64() + 15) & ~15L;
            for (int offset = 0; offset < ContextSize; offset += 8)
                Marshal.WriteInt64((nint)aligned, offset, 0);
            action((nint)aligned);
        }
        finally { Marshal.FreeHGlobal(raw); }
    }

    private static byte[] ReadAround(nint process, ulong nextRip)
    {
        byte[] bytes = new byte[48];
        ReadProcessMemory(process, (nint)(nextRip - 16), bytes, (nuint)bytes.Length, out _);
        return bytes;
    }

    private static float ReadFloat(nint process, ulong address)
    {
        byte[] bytes = new byte[4];
        if (!ReadProcessMemory(process, (nint)address, bytes, 4, out nuint read) || read != 4)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "ReadProcessMemory failed while checking watched value.");
        return BitConverter.ToSingle(bytes);
    }

    private static bool TryParse(string[] args, out int processId, out ulong address, out int timeout, out float? desiredValue, out bool accessMode, out HashSet<ulong> ignoredNextRips)
    {
        processId = 0; address = 0; timeout = 120; desiredValue = null; accessMode = false; ignoredNextRips = [];
        if (args.Length < 4 || (args.Length & 1) != 0) return false;
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--pid": if (!int.TryParse(args[i + 1], out processId)) return false; break;
                case "--address":
                    string text = args[i + 1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[i + 1][2..] : args[i + 1];
                    if (!ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address)) return false;
                    break;
                case "--timeout": if (!int.TryParse(args[i + 1], out timeout)) return false; break;
                case "--value":
                    if (!float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) return false;
                    desiredValue = parsed;
                    break;
                case "--access":
                    if (args[i + 1] is not ("0" or "1")) return false;
                    accessMode = args[i + 1] == "1";
                    break;
                case "--ignore-next-rip":
                    foreach (string item in args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string ripText = item.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? item[2..] : item;
                        if (!ulong.TryParse(ripText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong parsedRip)) return false;
                        ignoredNextRips.Add(parsedRip);
                    }
                    break;
                default: return false;
            }
        }
        return processId > 0 && address > 0 && timeout is >= 5 and <= 600;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DebugSetProcessKillOnExit(bool killOnExit);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DebugActiveProcess(int processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DebugActiveProcessStop(int processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WaitForDebugEvent(nint debugEvent, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ContinueDebugEvent(int processId, int threadId, uint continueStatus);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenThread(uint access, bool inherit, int threadId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadContext(nint thread, nint context);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetThreadContext(nint thread, nint context);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
