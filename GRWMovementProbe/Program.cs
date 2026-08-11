using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace GRWMovementProbe;

internal static class Program
{
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000;
    private const uint MemPrivate = 0x20000;
    private const uint PageGuard = 0x100;
    private const uint PageNoAccess = 0x01;
    private const int VkF4 = 0x73;
    private const int VkF12 = 0x7B;
    private const int VkW = 0x57;
    private const int VkS = 0x53;
    private static readonly TimeSpan MaximumActivation = TimeSpan.FromSeconds(10);

    public static int Main(string[] args)
    {
        if (!TryParseArguments(args, out ulong[] addresses, out float scale))
        {
            Console.Error.WriteLine("Usage: GRWMovementProbe --addresses 0xAAA,0xBBB --value 0.35");
            return 2;
        }

        Process[] processes = Process.GetProcessesByName("GRW");
        if (processes.Length != 1)
        {
            Console.Error.WriteLine($"Expected exactly one GRW process; found {processes.Length}.");
            return 3;
        }

        using Process process = processes[0];
        uint access = ProcessQueryInformation | ProcessVmOperation | ProcessVmRead | ProcessVmWrite;
        nint handle = OpenProcess(access, false, process.Id);
        if (handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed");
        }

        try
        {
            timeBeginPeriod(1);
            foreach (ulong address in addresses)
            {
                ValidateTarget(handle, address);
            }

            Console.WriteLine("GRW Movement Probe - CONTROLLED 4-BYTE WRITE TEST");
            Console.WriteLine($"PID {process.Id}; {addresses.Length} explicit target(s); magnitude {scale:F3}.");
            foreach (ulong address in addresses)
            {
                Console.WriteLine($"  0x{address:X16}");
            }
            Console.WriteLine("Hold W or S first, then hold F4 to apply. Release F4 to stop. F12 exits.");
            Console.WriteLine("Each activation is capped at 10 seconds.");

            bool f4WasDown = false;
            bool f12WasDown = false;
            while (true)
            {
                bool f4 = IsKeyDown(VkF4);
                bool f12 = IsKeyDown(VkF12);
                if (f12 && !f12WasDown)
                {
                    Console.WriteLine("Exit requested.");
                    break;
                }

                if (f4 && !f4WasDown)
                {
                    ApplyWhileHeld(handle, addresses, scale);
                    f4 = IsKeyDown(VkF4);
                }

                f4WasDown = f4;
                f12WasDown = f12;
                Thread.Sleep(10);
            }
        }
        finally
        {
            timeEndPeriod(1);
            CloseHandle(handle);
        }

        return 0;
    }

    private static void ApplyWhileHeld(nint handle, ulong[] addresses, float scale)
    {
        bool w = IsKeyDown(VkW);
        bool s = IsKeyDown(VkS);
        if (w == s)
        {
            Console.WriteLine("Activation ignored: hold exactly one of W or S before pressing F4.");
            return;
        }

        List<(ulong Address, byte[] Bytes)> targets = [];
        foreach (ulong address in addresses)
        {
            if (!TryReadFloat(handle, address, out float observed) || Math.Abs(observed) != 1f)
            {
                Console.WriteLine($"Activation ignored: 0x{address:X16} was {observed:R}, not +/-1.0.");
                return;
            }

            targets.Add((address, BitConverter.GetBytes(MathF.CopySign(scale, observed))));
        }

        Stopwatch timer = Stopwatch.StartNew();
        int writes = 0;
        while (IsKeyDown(VkF4) && timer.Elapsed < MaximumActivation)
        {
            bool movementHeld = IsKeyDown(VkW) ^ IsKeyDown(VkS);
            if (!movementHeld)
            {
                break;
            }

            foreach ((ulong targetAddress, byte[] bytes) in targets)
            {
                if (!WriteProcessMemory(handle, (nint)targetAddress, bytes, 4, out nuint written) || written != 4)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "A 4-byte probe write failed");
                }

                writes++;
            }

            Thread.Sleep(1);
        }

        Thread.Sleep(25);
        string observedAfter = string.Join(", ", addresses.Select(address =>
        {
            TryReadFloat(handle, address, out float value);
            return value.ToString("R", CultureInfo.InvariantCulture);
        }));
        Console.WriteLine(
            $"Activation ended after {timer.Elapsed.TotalSeconds:F2}s and {writes:N0} four-byte writes; " +
            $"post-release observed values [{observedAfter}].");
    }

    private static void ValidateTarget(nint handle, ulong address)
    {
        nuint size = (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
        if (VirtualQueryEx(handle, (nint)address, out MEMORY_BASIC_INFORMATION info, size) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualQueryEx failed for target");
        }

        bool acceptable = info.State == MemCommit && info.Type == MemPrivate &&
                          (info.Protect & (PageGuard | PageNoAccess)) == 0 &&
                          IsWritableProtection(info.Protect);
        if (!acceptable)
        {
            throw new InvalidOperationException("Target is not committed, writable, private memory.");
        }

        if (!TryReadFloat(handle, address, out float value) || !float.IsFinite(value))
        {
            throw new InvalidOperationException("Target does not currently contain a finite float.");
        }
    }

    private static bool TryParseArguments(string[] args, out ulong[] addresses, out float scale)
    {
        addresses = [];
        scale = 0;
        if (args.Length != 4 ||
            !(args[0].Equals("--address", StringComparison.OrdinalIgnoreCase) ||
              args[0].Equals("--addresses", StringComparison.OrdinalIgnoreCase)) ||
            !args[2].Equals("--value", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        List<ulong> parsed = [];
        foreach (string rawAddress in args[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string addressText = rawAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? rawAddress[2..]
                : rawAddress;
            if (!ulong.TryParse(addressText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong address))
            {
                return false;
            }

            parsed.Add(address);
        }

        addresses = parsed.Distinct().ToArray();
        return addresses.Length is >= 1 and <= 8 &&
               float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out scale) &&
               scale is >= 0.05f and <= 0.95f;
    }

    private static bool TryReadFloat(nint handle, ulong address, out float value)
    {
        byte[] bytes = new byte[4];
        if (ReadProcessMemory(handle, (nint)address, bytes, 4, out nuint read) && read == 4)
        {
            value = BitConverter.ToSingle(bytes);
            return true;
        }

        value = float.NaN;
        return false;
    }

    private static bool IsWritableProtection(uint protection)
    {
        uint basic = protection & 0xFF;
        return basic is 0x04 or 0x08 or 0x40 or 0x80;
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint processAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(nint process, nint address, byte[] buffer, nuint size, out nuint written);

    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQueryEx(nint process, nint address, out MEMORY_BASIC_INFORMATION buffer, nuint length);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint periodMilliseconds);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint periodMilliseconds);
}
