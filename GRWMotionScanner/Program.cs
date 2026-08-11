using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace GRWMotionScanner;

internal static class Program
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000;
    private const uint MemPrivate = 0x20000;
    private const uint PageGuard = 0x100;
    private const uint PageNoAccess = 0x01;
    private const int VkF4 = 0x73;
    private const int VkF5 = 0x74;
    private const int VkF6 = 0x75;
    private const int VkF7 = 0x76;
    private const int VkF8 = 0x77;
    private const int VkF10 = 0x79;
    private const int VkF12 = 0x7B;
    private const int ChunkSize = 1024 * 1024;
    private const int CandidateLimit = 100_000_000;

    private static List<Sample>? _samples;

    public static int Main(string[] args)
    {
        Console.WriteLine("GRW Motion Scanner - READ-ONLY build");
        Console.WriteLine("No process-memory write API is imported.");
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
                _samples = Load(args[1]);
                Console.WriteLine($"Loaded {_samples.Count:N0} candidates from {Path.GetFullPath(args[1])}.");
            }
            else if (args.Length != 0)
            {
                Console.Error.WriteLine("Usage: GRWMotionScanner [--load <motion-result-file>]");
                return 3;
            }

            Console.WriteLine($"Attached read-only to GRW PID {process.Id}.");
            Console.WriteLine("F4 snapshot +/-32 bytes around each remaining candidate");
            Console.WriteLine("F5 WALK freely: initial plausible moving-float capture");
            Console.WriteLine("F6 IDLE: retain values near zero");
            Console.WriteLine("F7 WALK freely again: retain/rebaseline recurring motion values");
            Console.WriteLine("F8 JOG freely: retain values 1.5x-5x the walking magnitude");
            Console.WriteLine("F10 save results    F12 exit");

            Dictionary<int, bool> previous = new() { [VkF4] = false, [VkF5] = false, [VkF6] = false, [VkF7] = false, [VkF8] = false, [VkF10] = false, [VkF12] = false };
            while (true)
            {
                bool f4 = IsKeyDown(VkF4);
                bool f5 = IsKeyDown(VkF5);
                bool f6 = IsKeyDown(VkF6);
                bool f7 = IsKeyDown(VkF7);
                bool f8 = IsKeyDown(VkF8);
                bool f10 = IsKeyDown(VkF10);
                bool f12 = IsKeyDown(VkF12);

                if (f4 && !previous[VkF4]) PrintNeighborhoods(handle);
                if (f5 && !previous[VkF5]) CaptureInitialWalk(handle);
                if (f6 && !previous[VkF6]) FilterIdle(handle);
                if (f7 && !previous[VkF7]) RebaselineWalk(handle);
                if (f8 && !previous[VkF8]) FilterJogRatio(handle);
                if (f10 && !previous[VkF10]) Save(process.Id);
                if (f12 && !previous[VkF12]) break;

                previous[VkF4] = f4;
                previous[VkF5] = f5;
                previous[VkF6] = f6;
                previous[VkF7] = f7;
                previous[VkF8] = f8;
                previous[VkF10] = f10;
                previous[VkF12] = f12;
                Thread.Sleep(25);
            }
        }
        finally
        {
            CloseHandle(handle);
        }

        return 0;
    }

    private static void PrintNeighborhoods(nint handle)
    {
        if (!RequireSamples()) return;
        Console.WriteLine($"NEIGHBORHOOD SNAPSHOT {DateTime.Now:HH:mm:ss.fff}");
        foreach (Sample sample in _samples!)
        {
            ulong start = sample.Address - 32;
            byte[] bytes = new byte[68];
            if (!ReadProcessMemory(handle, (nint)start, bytes, (nuint)bytes.Length, out nuint read) || read != (nuint)bytes.Length)
            {
                Console.WriteLine($"  0x{sample.Address:X16}: <unreadable>");
                continue;
            }

            Console.WriteLine($"  candidate 0x{sample.Address:X16}");
            for (int offset = 0; offset < bytes.Length; offset += 4)
            {
                int relative = offset - 32;
                float value = BitConverter.ToSingle(bytes, offset);
                Console.WriteLine($"    {relative,4}: {value:R}");
            }
        }
    }

    private static void CaptureInitialWalk(nint handle)
    {
        if (_samples is not null)
        {
            Console.WriteLine("Initial capture already exists; restart to reset.");
            return;
        }

        _samples = Scan(handle, null, (_, value) => IsPlausibleMovingValue(value), retainCurrent: true);
        Console.WriteLine($"WALK initial: {_samples.Count:N0} candidates.");
    }

    private static void FilterIdle(nint handle)
    {
        if (!RequireSamples()) return;
        _samples = Scan(handle, _samples, (_, value) => Math.Abs(value) < 0.001f, retainCurrent: false);
        Console.WriteLine($"IDLE near-zero: {_samples.Count:N0} candidates remain.");
    }

    private static void RebaselineWalk(nint handle)
    {
        if (!RequireSamples()) return;
        List<Sample> old = _samples!;
        _samples = Scan(handle, old, (previous, value) =>
        {
            float baseline = previous!.Value.Baseline;
            float ratio = Math.Abs(value / baseline);
            return IsPlausibleMovingValue(value) && SameSign(value, baseline) && ratio is >= 0.2f and <= 2f;
        }, retainCurrent: true);
        Console.WriteLine($"WALK recurrence: {_samples.Count:N0} candidates remain and were rebaselined.");
    }

    private static void FilterJogRatio(nint handle)
    {
        if (!RequireSamples()) return;
        List<Sample> walk = _samples!;
        List<Sample> jog = Scan(handle, walk, (previous, value) =>
        {
            float baseline = previous!.Value.Baseline;
            float ratio = Math.Abs(value / baseline);
            return float.IsFinite(value) && SameSign(value, baseline) && ratio is >= 1.5f and <= 5f;
        }, retainCurrent: true);

        _samples = jog;
        Console.WriteLine($"JOG/WALK ratio: {_samples.Count:N0} candidates remain.");
        Dictionary<ulong, float> walkValues = walk.ToDictionary(sample => sample.Address, sample => sample.Baseline);
        foreach (Sample sample in _samples.Take(200))
        {
            float walkValue = walkValues[sample.Address];
            Console.WriteLine($"  0x{sample.Address:X16}: walk={walkValue:R}, jog={sample.Baseline:R}, ratio={Math.Abs(sample.Baseline / walkValue):F4}");
        }
    }

    private static List<Sample> Scan(
        nint handle,
        List<Sample>? existing,
        Func<Sample?, float, bool> predicate,
        bool retainCurrent)
    {
        Stopwatch timer = Stopwatch.StartNew();
        List<Sample> next = existing is null ? [] : new List<Sample>(Math.Min(existing.Count, 1_000_000));
        byte[] buffer = new byte[ChunkSize];
        long bytesRead = 0;
        int existingIndex = 0;

        foreach (MemoryRegion region in EnumerateWritablePrivateRegions(handle))
        {
            ulong end = checked(region.BaseAddress + region.Size);
            for (ulong chunkBase = region.BaseAddress; chunkBase < end;)
            {
                int requested = (int)Math.Min((ulong)buffer.Length, end - chunkBase);
                if (!ReadProcessMemory(handle, (nint)chunkBase, buffer, (nuint)requested, out nuint actual) || actual < 4)
                {
                    chunkBase += (ulong)Math.Max(requested, 4096);
                    continue;
                }

                int length = checked((int)actual);
                bytesRead += length;
                int start = (int)((4 - (chunkBase & 3)) & 3);
                for (int offset = start; offset <= length - 4; offset += 4)
                {
                    ulong address = chunkBase + (ulong)offset;
                    Sample? previous = null;
                    if (existing is not null)
                    {
                        while (existingIndex < existing.Count && existing[existingIndex].Address < address)
                        {
                            existingIndex++;
                        }

                        if (existingIndex >= existing.Count || existing[existingIndex].Address != address) continue;
                        previous = existing[existingIndex];
                    }

                    float value = BitConverter.ToSingle(buffer, offset);
                    if (!predicate(previous, value)) continue;
                    next.Add(new Sample(address, retainCurrent ? value : previous!.Value.Baseline));
                    if (next.Count >= CandidateLimit)
                    {
                        throw new InvalidOperationException($"Candidate safety limit of {CandidateLimit:N0} reached; narrow the initial range.");
                    }
                }

                chunkBase += (ulong)length;
            }
        }

        timer.Stop();
        Console.WriteLine($"  scan read {bytesRead / (1024d * 1024d):N1} MiB in {timer.Elapsed.TotalSeconds:N1}s");
        return next;
    }

    private static bool IsPlausibleMovingValue(float value)
    {
        if (!float.IsFinite(value)) return false;
        float magnitude = Math.Abs(value);
        if (magnitude < 0.30f || magnitude > 1.5f) return false;
        return Math.Abs(value - MathF.Round(value)) > 0.0001f;
    }

    private static bool SameSign(float left, float right) => MathF.CopySign(1f, left) == MathF.CopySign(1f, right);

    private static bool RequireSamples()
    {
        if (_samples is not null) return true;
        Console.WriteLine("No initial WALK capture exists. Start with F5.");
        return false;
    }

    private static List<Sample> Load(string path)
    {
        List<Sample> samples = [];
        foreach (string line in File.ReadLines(path))
        {
            string[] fields = line.Split('\t');
            if (fields.Length != 2) throw new FormatException($"Invalid result line: {line}");
            string addressText = fields[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? fields[0][2..] : fields[0];
            if (!ulong.TryParse(addressText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong address) ||
                !float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                throw new FormatException($"Invalid result line: {line}");
            }

            samples.Add(new Sample(address, value));
        }

        samples.Sort((left, right) => left.Address.CompareTo(right.Address));
        return samples;
    }

    private static void Save(int processId)
    {
        if (!RequireSamples()) return;
        List<Sample> samples = _samples!;
        string directory = Path.Combine(AppContext.BaseDirectory, "scanner-results");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"motion-{processId}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllLines(path, samples.Select(sample =>
            $"0x{sample.Address:X16}\t{sample.Baseline.ToString("R", CultureInfo.InvariantCulture)}"));
        Console.WriteLine($"Saved {samples.Count:N0} candidates to {path}");
    }

    private static IEnumerable<MemoryRegion> EnumerateWritablePrivateRegions(nint handle)
    {
        ulong address = 0;
        const ulong maximum = 0x00007FFFFFFEFFFFUL;
        nuint size = (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
        while (address < maximum)
        {
            if (VirtualQueryEx(handle, (nint)address, out MEMORY_BASIC_INFORMATION info, size) == 0) yield break;
            ulong baseAddress = (ulong)info.BaseAddress;
            ulong regionSize = (ulong)info.RegionSize;
            if (regionSize == 0) yield break;
            uint protection = info.Protect & 0xFF;
            bool writable = protection is 0x04 or 0x08;
            if (info.State == MemCommit && info.Type == MemPrivate && writable &&
                (info.Protect & (PageGuard | PageNoAccess)) == 0)
            {
                yield return new MemoryRegion(baseAddress, regionSize);
            }

            ulong next = baseAddress + regionSize;
            if (next <= address) yield break;
            address = next;
        }
    }

    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private readonly record struct Sample(ulong Address, float Baseline);
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
    private static extern nint OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);

    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQueryEx(nint process, nint address, out MEMORY_BASIC_INFORMATION buffer, nuint length);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
