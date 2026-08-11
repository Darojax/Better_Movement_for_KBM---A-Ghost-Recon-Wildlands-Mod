using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GRWAdsTargetTest;

internal static class Program
{
    private const uint ProcessVmOperation = 0x0008, ProcessVmRead = 0x0010, ProcessVmWrite = 0x0020, ProcessQueryInformation = 0x0400;
    private const int VkRButton = 0x02, VkF7 = 0x76, VkF8 = 0x77, VkF12 = 0x7B;
    private const ulong TargetAddress = 0x0000019629A07A70;
    private const float HipValue = 1.0f, AdsVanillaValue = 0.30f, TestValue = 1.0f;

    public static int Main()
    {
        Process[] games = Process.GetProcessesByName("GRW");
        if (games.Length != 1) { Console.Error.WriteLine($"Expected one GRW process; found {games.Length}."); return 2; }
        using Process game = games[0];
        nint process = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryInformation, false, game.Id);
        if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error());

        bool overrideEnabled = false;
        try
        {
            float initial = ReadFloat(process, TargetAddress);
            Console.WriteLine("GRW ADS single-address test - session-specific and reversible");
            Console.WriteLine($"PID {game.Id}; target 0x{TargetAddress:X16}; current value {initial:R}");
            if (Math.Abs(initial - HipValue) > 0.001f && Math.Abs(initial - AdsVanillaValue) > 0.001f)
            {
                Console.Error.WriteLine("Target no longer has a recognized HIP/ADS value; refusing to write.");
                return 3;
            }
            Console.WriteLine("F7 enable 1.00 ADS override   F8 restore vanilla   F12 restore + exit");
            bool previousF7 = false, previousF8 = false, previousF12 = false;
            while (true)
            {
                bool f7 = IsDown(VkF7), f8 = IsDown(VkF8), f12 = IsDown(VkF12);
                if (f7 && !previousF7) { overrideEnabled = true; Console.WriteLine("ADS override ENABLED (1.00 while RMB is held)."); }
                if (f8 && !previousF8)
                {
                    overrideEnabled = false;
                    RestoreForCurrentState(process);
                    Console.WriteLine("Vanilla value restored; override disabled.");
                }
                if (f12 && !previousF12) break;
                if (overrideEnabled && IsDown(VkRButton)) WriteFloat(process, TargetAddress, TestValue);
                previousF7 = f7; previousF8 = f8; previousF12 = f12;
                Thread.Sleep(10);
            }
            return 0;
        }
        finally
        {
            try { RestoreForCurrentState(process); } catch { }
            CloseHandle(process);
            Console.WriteLine("Restored current vanilla HIP/ADS value and detached.");
        }
    }

    private static void RestoreForCurrentState(nint process) =>
        WriteFloat(process, TargetAddress, IsDown(VkRButton) ? AdsVanillaValue : HipValue);

    private static float ReadFloat(nint process, ulong address)
    {
        byte[] buffer = new byte[4];
        if (!ReadProcessMemory(process, (nint)address, buffer, 4, out nuint read) || read != 4)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "ReadProcessMemory failed.");
        return BitConverter.ToSingle(buffer);
    }

    private static void WriteFloat(nint process, ulong address, float value)
    {
        byte[] buffer = BitConverter.GetBytes(value);
        if (!WriteProcessMemory(process, (nint)address, buffer, 4, out nuint written) || written != 4)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory failed.");
    }

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(nint process, nint address, byte[] buffer, nuint size, out nuint written);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
