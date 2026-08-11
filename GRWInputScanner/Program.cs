using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace GRWInputScanner;

internal static class Program
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000;
    private const uint MemPrivate = 0x20000;
    private const uint PageGuard = 0x100;
    private const uint PageNoAccess = 0x01;
    private const int VkF2 = 0x71;
    private const int VkF3 = 0x72;
    private const int VkF4 = 0x73;
    private const int VkF5 = 0x74;
    private const int VkF6 = 0x75;
    private const int VkF7 = 0x76;
    private const int VkF8 = 0x77;
    private const int VkF9 = 0x78;
    private const int VkF10 = 0x79;
    private const int VkF12 = 0x7B;
    private const int ChunkSize = 1024 * 1024;

    private static HashSet<ulong>? _candidates;
    private static Dictionary<ulong, int>? _forwardSigns;
    private static int _captureNumber;

    public static int Main(string[] args)
    {
        Console.WriteLine("GRW Input Scanner — READ-ONLY build");
        Console.WriteLine("Process access: QUERY_INFORMATION | VM_READ. No write API is imported.");

        Process[] processes = Process.GetProcessesByName("GRW");
        if (processes.Length != 1)
        {
            Console.Error.WriteLine($"Expected exactly one GRW process; found {processes.Length}.");
            return 2;
        }

        using Process process = processes[0];
        nint handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, process.Id);
        if (handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed");
        }

        try
        {
            if (args.Length == 2 && args[0].Equals("--load", StringComparison.OrdinalIgnoreCase))
            {
                _candidates = LoadCandidates(args[1]);
                Console.WriteLine($"Loaded {_candidates.Count:N0} candidates from {Path.GetFullPath(args[1])}.");
            }
            else if (args.Length != 0)
            {
                Console.Error.WriteLine("Usage: GRWInputScanner [--load <candidate-file>]");
                return 3;
            }

            Console.WriteLine($"Attached read-only to GRW PID {process.Id}.");
            Console.WriteLine("Face a solid wall on level ground.");
            Console.WriteLine("F3: initial WALK capture at approximately 0.35");
            Console.WriteLine("F2: retain JOG candidates at approximately 1.0");
            Console.WriteLine("F4: print current float values for loaded/remaining candidates");
            Console.WriteLine("F5: retain loaded candidates with fractional magnitude (0 < abs(value) < 1)");
            Console.WriteLine("F6: record FORWARD values while holding W (loaded candidates only)");
            Console.WriteLine("F7: retain opposite signs while holding S (after F6)");
            Console.WriteLine("F8: capture MOVING while holding W (accepts float +1.0 or -1.0)");
            Console.WriteLine("F9: capture IDLE after releasing W (accepts float +0.0 or -0.0)");
            Console.WriteLine("F10: save remaining addresses    F12: exit");
            Console.WriteLine("Start with: hold W, then tap F8.");

            bool f2WasDown = false;
            bool f3WasDown = false;
            bool f4WasDown = false;
            bool f5WasDown = false;
            bool f6WasDown = false;
            bool f7WasDown = false;
            bool f8WasDown = false;
            bool f9WasDown = false;
            bool f10WasDown = false;
            bool f12WasDown = false;

            while (true)
            {
                bool f2 = IsKeyDown(VkF2);
                bool f3 = IsKeyDown(VkF3);
                bool f4 = IsKeyDown(VkF4);
                bool f5 = IsKeyDown(VkF5);
                bool f6 = IsKeyDown(VkF6);
                bool f7 = IsKeyDown(VkF7);
                bool f8 = IsKeyDown(VkF8);
                bool f9 = IsKeyDown(VkF9);
                bool f10 = IsKeyDown(VkF10);
                bool f12 = IsKeyDown(VkF12);

                if (f2 && !f2WasDown)
                {
                    Capture(handle, MatchJogOne, "JOG (~1.0)");
                }

                if (f3 && !f3WasDown)
                {
                    Capture(handle, MatchWalkPoint35, "WALK (~0.35)");
                }

                if (f4 && !f4WasDown)
                {
                    PrintCandidateValues(handle);
                }

                if (f5 && !f5WasDown)
                {
                    FilterForFractionalMagnitude(handle);
                }

                if (f6 && !f6WasDown)
                {
                    RecordForwardSigns(handle);
                }

                if (f7 && !f7WasDown)
                {
                    FilterForOppositeBackwardSigns(handle);
                }

                if (f8 && !f8WasDown)
                {
                    Capture(handle, MatchUnit, "MOVING (+/-1.0)");
                }

                if (f9 && !f9WasDown)
                {
                    Capture(handle, MatchZero, "IDLE (+/-0.0)");
                }

                if (f10 && !f10WasDown)
                {
                    SaveCandidates(process);
                }

                if (f12 && !f12WasDown)
                {
                    Console.WriteLine("Exit requested.");
                    break;
                }

                f2WasDown = f2;
                f3WasDown = f3;
                f4WasDown = f4;
                f5WasDown = f5;
                f6WasDown = f6;
                f7WasDown = f7;
                f8WasDown = f8;
                f9WasDown = f9;
                f10WasDown = f10;
                f12WasDown = f12;
                Thread.Sleep(25);
            }
        }
        finally
        {
            CloseHandle(handle);
        }

        return 0;
    }

    private static void PrintCandidateValues(nint processHandle)
    {
        if (_candidates is null)
        {
            Console.WriteLine("No candidate set is loaded.");
            return;
        }

        Console.WriteLine($"VALUE SNAPSHOT {DateTime.Now:HH:mm:ss.fff} ({_candidates.Count:N0} candidates)");
        foreach (ulong address in _candidates.Order())
        {
            if (TryReadFloatBits(processHandle, address, out int bits))
            {
                float value = BitConverter.Int32BitsToSingle(bits);
                Console.WriteLine($"  0x{address:X16} = {value:R} (0x{bits:X8})");
            }
            else
            {
                Console.WriteLine($"  0x{address:X16} = <unreadable>");
            }
        }
    }

    private static void FilterForFractionalMagnitude(nint processHandle)
    {
        if (_candidates is null)
        {
            Console.WriteLine("No candidate set is loaded. Start with --load.");
            return;
        }

        HashSet<ulong> fractional = [];
        foreach (ulong address in _candidates)
        {
            if (TryReadFloatBits(processHandle, address, out int bits))
            {
                float value = BitConverter.Int32BitsToSingle(bits);
                float magnitude = Math.Abs(value);
                if (float.IsFinite(value) && magnitude > 0.001f && magnitude < 0.999f)
                {
                    fractional.Add(address);
                }
            }
        }

        _candidates = fractional;
        _forwardSigns = null;
        Console.WriteLine($"FRACTIONAL magnitude filter: {_candidates.Count:N0} candidates remain.");
    }

    private static HashSet<ulong> LoadCandidates(string path)
    {
        HashSet<ulong> loaded = [];
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                line = line[2..];
            }

            if (!ulong.TryParse(line, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong address))
            {
                throw new FormatException($"Invalid candidate address: {rawLine}");
            }

            loaded.Add(address);
        }

        return loaded;
    }

    private static void RecordForwardSigns(nint processHandle)
    {
        if (_candidates is null)
        {
            Console.WriteLine("No candidate set is loaded. Use --load or perform F8/F9 captures first.");
            return;
        }

        Dictionary<ulong, int> signs = [];
        foreach (ulong address in _candidates)
        {
            if (TryReadFloatBits(processHandle, address, out int bits))
            {
                float value = BitConverter.Int32BitsToSingle(bits);
                if (float.IsFinite(value) && Math.Abs(value) > 0.001f && Math.Abs(value) <= 1.001f)
                {
                    signs[address] = bits;
                }
            }
        }

        _forwardSigns = signs;
        _candidates.IntersectWith(signs.Keys);
        Console.WriteLine($"FORWARD value capture: {_candidates.Count:N0} nonzero candidates recorded.");
    }

    private static void FilterForOppositeBackwardSigns(nint processHandle)
    {
        if (_candidates is null || _forwardSigns is null)
        {
            Console.WriteLine("Record a FORWARD sign capture with F6 first.");
            return;
        }

        HashSet<ulong> opposite = [];
        foreach (ulong address in _candidates)
        {
            if (!TryReadFloatBits(processHandle, address, out int bits))
            {
                continue;
            }

            float forward = BitConverter.Int32BitsToSingle(_forwardSigns[address]);
            float backward = BitConverter.Int32BitsToSingle(bits);
            bool finiteNonzero = float.IsFinite(backward) && Math.Abs(backward) > 0.001f && Math.Abs(backward) <= 1.001f;
            bool oppositeSign = MathF.CopySign(1f, forward) != MathF.CopySign(1f, backward);
            if (finiteNonzero && oppositeSign)
            {
                opposite.Add(address);
            }
        }

        _candidates = opposite;
        _forwardSigns = null;
        Console.WriteLine($"BACKWARD opposite-sign filter: {_candidates.Count:N0} candidates remain.");
        if (_candidates.Count <= 100)
        {
            Console.WriteLine("Candidate set is small; tap F10 to save it.");
        }
    }

    private static bool TryReadFloatBits(nint processHandle, ulong address, out int bits)
    {
        byte[] value = new byte[4];
        if (ReadProcessMemory(processHandle, (nint)address, value, 4, out nuint actual) && actual == 4)
        {
            bits = BitConverter.ToInt32(value);
            return true;
        }

        bits = 0;
        return false;
    }

    private static void Capture(nint processHandle, Func<int, bool> matcher, string label)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        HashSet<ulong> next = [];
        long bytesRead = 0;
        int regionsRead = 0;
        bool initial = _candidates is null;

        foreach (MemoryRegion region in EnumerateReadablePrivateRegions(processHandle))
        {
            regionsRead++;
            ScanRegion(processHandle, region, matcher, initial ? null : _candidates, next, ref bytesRead);
        }

        _candidates = next;
        _captureNumber++;
        stopwatch.Stop();
        Console.WriteLine(
            $"Capture {_captureNumber}: {label}; {_candidates.Count:N0} candidates; " +
            $"{bytesRead / (1024d * 1024d):N1} MiB read across {regionsRead:N0} regions in " +
            $"{stopwatch.Elapsed.TotalSeconds:N1}s.");

        if (_candidates.Count == 0)
        {
            Console.WriteLine("No candidates remain. Exit with F12 and restart the scanner to reset.");
        }
        else if (_candidates.Count <= 100)
        {
            Console.WriteLine("Candidate set is small; tap F10 to save it before further filtering.");
        }
    }

    private static void ScanRegion(
        nint processHandle,
        MemoryRegion region,
        Func<int, bool> matcher,
        HashSet<ulong>? existing,
        HashSet<ulong> next,
        ref long bytesRead)
    {
        byte[] buffer = new byte[ChunkSize + 4];
        ulong regionEnd = checked(region.BaseAddress + region.Size);

        for (ulong chunkBase = region.BaseAddress; chunkBase < regionEnd;)
        {
            int requested = (int)Math.Min((ulong)ChunkSize, regionEnd - chunkBase);
            if (!ReadProcessMemory(processHandle, (nint)chunkBase, buffer, (nuint)requested, out nuint actual) || actual < 4)
            {
                chunkBase += (ulong)Math.Max(requested, 4096);
                continue;
            }

            int length = checked((int)actual);
            bytesRead += length;
            int start = (int)((4 - (chunkBase & 3)) & 3);

            for (int offset = start; offset <= length - 4; offset += 4)
            {
                int bits = BitConverter.ToInt32(buffer, offset);
                if (!matcher(bits))
                {
                    continue;
                }

                ulong address = chunkBase + (ulong)offset;
                if (existing is null || existing.Contains(address))
                {
                    next.Add(address);
                }
            }

            chunkBase += (ulong)length;
        }
    }

    private static IEnumerable<MemoryRegion> EnumerateReadablePrivateRegions(nint processHandle)
    {
        ulong address = 0;
        ulong maximumAddress = Environment.Is64BitProcess ? 0x00007FFFFFFEFFFFUL : uint.MaxValue;
        nuint infoSize = (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();

        while (address < maximumAddress)
        {
            nuint result = VirtualQueryEx(processHandle, (nint)address, out MEMORY_BASIC_INFORMATION info, infoSize);
            if (result == 0)
            {
                yield break;
            }

            ulong baseAddress = (ulong)info.BaseAddress;
            ulong size = (ulong)info.RegionSize;
            if (size == 0)
            {
                yield break;
            }

            bool committed = info.State == MemCommit;
            bool privateMemory = info.Type == MemPrivate;
            bool readable = (info.Protect & (PageGuard | PageNoAccess)) == 0 && IsReadableProtection(info.Protect);
            if (committed && privateMemory && readable)
            {
                yield return new MemoryRegion(baseAddress, size);
            }

            ulong next = baseAddress + size;
            if (next <= address)
            {
                yield break;
            }

            address = next;
        }
    }

    private static bool IsReadableProtection(uint protection)
    {
        uint baseProtection = protection & 0xFF;
        return baseProtection is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80;
    }

    private static bool MatchUnit(int bits) => bits is 0x3F800000 or unchecked((int)0xBF800000);
    private static bool MatchWalkPoint35(int bits) => bits is 0x3EB33332 or 0x3EB33333;
    private static bool MatchJogOne(int bits) => bits is 0x3F7FFFFF or 0x3F800000;
    private static bool MatchZero(int bits) => bits is 0 or unchecked((int)0x80000000);
    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static void SaveCandidates(Process process)
    {
        if (_candidates is null)
        {
            Console.WriteLine("Nothing to save yet.");
            return;
        }

        string directory = Path.Combine(AppContext.BaseDirectory, "scanner-results");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"candidates-{process.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllLines(path, _candidates.Order().Select(address => "0x" + address.ToString("X16", CultureInfo.InvariantCulture)));
        Console.WriteLine($"Saved {_candidates.Count:N0} candidates to {path}");
    }

    private readonly record struct MemoryRegion(ulong BaseAddress, ulong Size);

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
    private static extern bool ReadProcessMemory(
        nint process,
        nint baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint bytesRead);

    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQueryEx(
        nint process,
        nint address,
        out MEMORY_BASIC_INFORMATION buffer,
        nuint length);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
