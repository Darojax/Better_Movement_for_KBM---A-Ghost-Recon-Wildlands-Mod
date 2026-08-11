using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace GRWAdsScanner;

internal static class Program
{
    private const uint ProcessVmRead = 0x0010, ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000, MemPrivate = 0x20000, PageGuard = 0x100, PageNoAccess = 0x01;
    private const int VkF5 = 0x74, VkF6 = 0x75, VkF7 = 0x76, VkF8 = 0x77, VkF9 = 0x78, VkF11 = 0x7A, VkF12 = 0x7B;
    private const int BlockSize = 64 * 1024, FloatCandidateLimit = 5_000_000;

    private static Dictionary<ulong, BlockSignature>? _hipHashes;
    private static List<BlockCandidate>? _changedBlocks;
    private static Dictionary<ulong, byte[]>? _hipBlocks;
    private static List<FloatCandidate>? _floats;

    public static int Main(string[] args)
    {
        Console.WriteLine("GRW ADS Differential Scanner - READ ONLY");
        Console.WriteLine("No process-memory write API is imported.");
        Process[] games = Process.GetProcessesByName("GRW");
        if (games.Length != 1) { Console.Error.WriteLine($"Expected one GRW process; found {games.Length}."); return 2; }
        using Process game = games[0];
        nint process = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, game.Id);
        if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            Console.WriteLine($"Attached read-only to PID {game.Id}.");
            if (args.Length == 2 && args[0].Equals("--refine", StringComparison.OrdinalIgnoreCase))
                return RunRefinement(process, game.Id, args[1]);
            if (args.Length == 1 && args[0].Equals("--motion", StringComparison.OrdinalIgnoreCase))
                return RunMotionScan(process, game.Id);
            Console.WriteLine("Remain stationary on stable terrain and allow each state to settle.");
            Console.WriteLine("F5 HIP snapshot hashes   F6 ADS changed blocks");
            Console.WriteLine("F7 HIP baselines         F8 ADS lower-float filter");
            Console.WriteLine("F9 HIP return filter     F11 ADS recurrence + save");
            Console.WriteLine("F12 exit");
            int[] keys = [VkF5, VkF6, VkF7, VkF8, VkF9, VkF11, VkF12];
            Dictionary<int, bool> previous = keys.ToDictionary(key => key, _ => false);
            while (true)
            {
                foreach (int key in keys)
                {
                    bool down = IsKeyDown(key);
                    if (down && !previous[key])
                    {
                        switch (key)
                        {
                            case VkF5: CaptureHipHashes(process); break;
                            case VkF6: CaptureAdsChangedBlocks(process); break;
                            case VkF7: CaptureHipBaselines(process); break;
                            case VkF8: CaptureAdsFloats(process); break;
                            case VkF9: FilterHipReturn(process); break;
                            case VkF11: FilterAdsRecurrenceAndSave(process, game.Id); break;
                            case VkF12: return 0;
                        }
                    }
                    previous[key] = down;
                }
                Thread.Sleep(25);
            }
        }
        finally { CloseHandle(process); }
    }

    private static int RunMotionScan(nint process, int pid)
    {
        string statusPath = Path.Combine(AppContext.BaseDirectory, "motion-live.status");
        File.WriteAllText(statusPath, "");
        void Report(string message) { Console.WriteLine(message); File.AppendAllText(statusPath, message + Environment.NewLine); }
        Report("MOTION MODE - READ ONLY");
        Report("Use one open, level route and keep the same camera heading for every moving sample.");
        Report("F5 IDLE hashes                 F6 HIP jog changed blocks");
        Report("F7 IDLE baselines              F8 HIP jog moving floats");
        Report("F9 ADS jog lower-speed filter  F11 HIP jog recurrence + save");
        Report("F12 exit");
        int[] keys = [VkF5, VkF6, VkF7, VkF8, VkF9, VkF11, VkF12];
        Dictionary<int, bool> previous = keys.ToDictionary(key => key, _ => false);
        while (true)
        {
            foreach (int key in keys)
            {
                bool down = IsKeyDown(key);
                if (down && !previous[key])
                {
                    if (key == VkF12) return 0;
                    if (key == VkF5) MotionIdleHashes(process, Report);
                    else if (key == VkF6) MotionHipChangedBlocks(process, Report);
                    else if (key == VkF7) MotionIdleBaselines(process, Report);
                    else if (key == VkF8) MotionHipFloats(process, Report);
                    else if (key == VkF9) MotionAdsFilter(process, Report);
                    else if (key == VkF11) MotionHipRecurrence(process, pid, Report);
                }
                previous[key] = down;
            }
            Thread.Sleep(25);
        }
    }

    private static void MotionIdleHashes(nint process, Action<string> report)
    {
        Stopwatch timer = Stopwatch.StartNew();
        List<BlockCandidate> blocks = EnumerateBlocks(process).ToList();
        ConcurrentDictionary<ulong, BlockSignature> parallelHashes = [];
        long bytes = 0;
        Parallel.ForEach(blocks, MotionParallelOptions(), block =>
        {
            byte[] data = ArrayPool<byte>.Shared.Rent(block.Length);
            try
            {
                if (!ReadProcessMemory(process, (nint)block.Address, data, (nuint)block.Length, out nuint read) || read != (nuint)block.Length) return;
                parallelHashes[block.Address] = new(block.Length, Hash(data, block.Length));
                Interlocked.Add(ref bytes, block.Length);
            }
            finally { ArrayPool<byte>.Shared.Return(data); }
        });
        Dictionary<ulong, BlockSignature> hashes = new(parallelHashes);
        _hipHashes = hashes; _changedBlocks = null; _hipBlocks = null; _floats = null;
        report($"F5 IDLE hashes complete: {hashes.Count:N0} blocks, {bytes / 1048576d:N1} MiB in {timer.Elapsed.TotalSeconds:N1}s.");
    }

    private static void MotionHipChangedBlocks(nint process, Action<string> report)
    {
        if (_hipHashes is null) { report("F6 refused: capture F5 while IDLE first."); return; }
        Stopwatch timer = Stopwatch.StartNew();
        ConcurrentBag<BlockCandidate> parallelChanged = [];
        Parallel.ForEach(_hipHashes, MotionParallelOptions(), entry =>
        {
            ulong address = entry.Key;
            BlockSignature signature = entry.Value;
            BlockCandidate block = new(address, signature.Length);
            byte[] data = ArrayPool<byte>.Shared.Rent(block.Length);
            try
            {
                if (ReadProcessMemory(process, (nint)block.Address, data, (nuint)block.Length, out nuint read) &&
                    read == (nuint)block.Length && Hash(data, block.Length) != signature.Hash)
                    parallelChanged.Add(block);
            }
            finally { ArrayPool<byte>.Shared.Return(data); }
        });
        List<BlockCandidate> changed = parallelChanged.OrderBy(block => block.Address).ToList();
        _changedBlocks = changed; _hipBlocks = null; _floats = null;
        report($"F6 moving HIP difference complete: {changed.Count:N0} changed blocks in {timer.Elapsed.TotalSeconds:N1}s.");
    }

    private static void MotionIdleBaselines(nint process, Action<string> report)
    {
        if (_changedBlocks is null) { report("F7 refused: capture F6 while moving in HIP first."); return; }
        Stopwatch timer = Stopwatch.StartNew();
        Dictionary<ulong, byte[]> blocks = [];
        long bytes = 0;
        foreach (BlockCandidate block in _changedBlocks)
        {
            byte[]? data = ReadBlock(process, block);
            if (data is null) continue;
            blocks[block.Address] = data; bytes += data.Length;
        }
        _hipBlocks = blocks; _floats = null;
        report($"F7 IDLE baselines complete: {blocks.Count:N0} blocks, {bytes / 1048576d:N1} MiB in {timer.Elapsed.TotalSeconds:N1}s.");
    }

    private static void MotionHipFloats(nint process, Action<string> report)
    {
        if (_hipBlocks is null) { report("F8 refused: capture F7 while IDLE first."); return; }
        Stopwatch timer = Stopwatch.StartNew();
        List<FloatCandidate> floats = [];
        foreach ((ulong blockAddress, byte[] idle) in _hipBlocks)
        {
            byte[]? moving = ReadBlock(process, new(blockAddress, idle.Length));
            if (moving is null) continue;
            for (int offset = 0; offset <= idle.Length - 4; offset += 4)
            {
                float stopped = BitConverter.ToSingle(idle, offset), hip = BitConverter.ToSingle(moving, offset);
                if (!float.IsFinite(stopped) || !float.IsFinite(hip) || Math.Abs(stopped) > 0.002f) continue;
                float magnitude = Math.Abs(hip);
                if (magnitude < 0.03f || magnitude > 20f) continue;
                floats.Add(new(blockAddress, offset, blockAddress + (ulong)offset, hip, 0));
                if (floats.Count >= FloatCandidateLimit) throw new InvalidOperationException("Motion candidate limit reached.");
            }
        }
        _floats = floats;
        report($"F8 moving HIP floats complete: {floats.Count:N0} candidates in {timer.Elapsed.TotalSeconds:N1}s.");
    }

    private static void MotionAdsFilter(nint process, Action<string> report)
    {
        if (_floats is null) { report("F9 refused: capture F8 while moving in HIP first."); return; }
        Stopwatch timer = Stopwatch.StartNew();
        List<FloatCandidate> next = [];
        foreach (IGrouping<ulong, FloatCandidate> group in _floats.GroupBy(c => c.BlockAddress))
        {
            int length = _hipBlocks![group.Key].Length;
            byte[]? current = ReadBlock(process, new(group.Key, length));
            if (current is null) continue;
            foreach (FloatCandidate candidate in group)
            {
                float ads = BitConverter.ToSingle(current, candidate.Offset);
                if (!float.IsFinite(ads) || MathF.CopySign(1, ads) != MathF.CopySign(1, candidate.Hip)) continue;
                float ratio = Math.Abs(ads / candidate.Hip);
                if (ratio is >= 0.03f and <= 0.90f) next.Add(candidate with { Ads = ads });
            }
        }
        _floats = next;
        report($"F9 moving ADS lower-speed filter complete: {next.Count:N0} candidates remain in {timer.Elapsed.TotalSeconds:N1}s.");
    }

    private static void MotionHipRecurrence(nint process, int pid, Action<string> report)
    {
        if (_floats is null) { report("F11 refused: capture F9 while moving in ADS first."); return; }
        Stopwatch timer = Stopwatch.StartNew();
        _floats = FilterByBlocks(process, _floats, (candidate, value) =>
            MathF.CopySign(1, value) == MathF.CopySign(1, candidate.Hip) && Near(value, candidate.Hip, 0.35f));
        string saved = SaveCandidates(_floats, pid, "motion-hip-ads");
        report($"F11 moving HIP recurrence complete: {_floats.Count:N0} candidates remain in {timer.Elapsed.TotalSeconds:N1}s.");
        report($"Saved results to {saved}");
    }

    private static int RunRefinement(nint process, int pid, string path)
    {
        if (!File.Exists(path)) { Console.Error.WriteLine($"Candidate file not found: {path}"); return 3; }
        List<FloatCandidate> candidates = [];
        foreach (string line in File.ReadLines(path))
        {
            string[] fields = line.Split('\t');
            if (fields.Length < 3 || !fields[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase)) continue;
            if (!ulong.TryParse(fields[0].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong address) ||
                !float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float hip) ||
                !float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float ads)) continue;
            ulong page = address & ~0xFFFUL;
            candidates.Add(new(page, checked((int)(address - page)), address, hip, ads));
        }
        if (candidates.Count == 0) { Console.Error.WriteLine("No candidates could be loaded."); return 4; }

        Console.WriteLine($"REFINEMENT MODE: loaded {candidates.Count:N0} candidates from {Path.GetFileName(path)}.");
        Console.WriteLine("Remain stationary on stable terrain and let each state settle.");
        Console.WriteLine("F5 validate HIP    F6 validate ADS + save    F12 exit");
        Console.WriteLine("Repeat F5/F6 for as many recurrence cycles as needed.");
        int[] keys = [VkF5, VkF6, VkF12];
        Dictionary<int, bool> previous = keys.ToDictionary(key => key, _ => false);
        bool hipValidated = false;
        int cycle = 0;
        while (true)
        {
            foreach (int key in keys)
            {
                bool down = IsKeyDown(key);
                if (down && !previous[key])
                {
                    if (key == VkF12) return 0;
                    if (key == VkF5)
                    {
                        candidates = FilterLoaded(process, candidates, (candidate, value) => Near(value, candidate.Hip, 0.05f));
                        hipValidated = true;
                        Console.WriteLine($"HIP validation: {candidates.Count:N0} candidates remain.");
                    }
                    else if (key == VkF6)
                    {
                        if (!hipValidated) { Console.WriteLine("Press F5 in HIP first."); }
                        else
                        {
                            candidates = FilterLoaded(process, candidates, (candidate, value) =>
                                Math.Abs(value) < Math.Abs(candidate.Hip) * 0.95f && Near(value, candidate.Ads, 0.10f));
                            hipValidated = false;
                            cycle++;
                            string saved = SaveCandidates(candidates, pid, $"refined-{cycle}");
                            Console.WriteLine($"ADS validation cycle {cycle}: {candidates.Count:N0} candidates remain.");
                            PrintTop(candidates, 30);
                            Console.WriteLine($"Saved results to {saved}");
                        }
                    }
                }
                previous[key] = down;
            }
            Thread.Sleep(25);
        }
    }

    private static List<FloatCandidate> FilterLoaded(nint process, List<FloatCandidate> candidates, Func<FloatCandidate, float, bool> predicate)
    {
        List<FloatCandidate> next = [];
        foreach (IGrouping<ulong, FloatCandidate> group in candidates.GroupBy(c => c.BlockAddress))
        {
            byte[]? current = ReadBlock(process, new(group.Key, 4096));
            if (current is null) continue;
            foreach (FloatCandidate candidate in group)
            {
                float value = BitConverter.ToSingle(current, candidate.Offset);
                if (float.IsFinite(value) && predicate(candidate, value)) next.Add(candidate);
            }
        }
        return next;
    }

    private static string SaveCandidates(List<FloatCandidate> candidates, int pid, string label)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "scanner-results");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"ads-{pid}-{label}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllLines(path, candidates.Select(c =>
            $"0x{c.Address:X16}\t{c.Hip.ToString("R", CultureInfo.InvariantCulture)}\t{c.Ads.ToString("R", CultureInfo.InvariantCulture)}\t{Math.Abs(c.Ads / c.Hip).ToString("F6", CultureInfo.InvariantCulture)}"));
        return path;
    }

    private static void CaptureHipHashes(nint process)
    {
        Stopwatch timer = Stopwatch.StartNew();
        Dictionary<ulong, BlockSignature> hashes = [];
        long bytes = 0;
        foreach (BlockCandidate block in EnumerateBlocks(process))
        {
            byte[]? data = ReadBlock(process, block);
            if (data is null) continue;
            hashes[block.Address] = new(block.Length, Hash(data));
            bytes += data.Length;
        }
        _hipHashes = hashes; _changedBlocks = null; _hipBlocks = null; _floats = null;
        Console.WriteLine($"HIP hashes: {hashes.Count:N0} blocks, {bytes / 1048576d:N1} MiB in {timer.Elapsed.TotalSeconds:N1}s.");
    }

    private static void CaptureAdsChangedBlocks(nint process)
    {
        if (_hipHashes is null) { Console.WriteLine("Press F5 in HIP first."); return; }
        Stopwatch timer = Stopwatch.StartNew();
        List<BlockCandidate> changed = [];
        foreach ((ulong address, BlockSignature signature) in _hipHashes)
        {
            BlockCandidate block = new(address, signature.Length);
            byte[]? data = ReadBlock(process, block);
            if (data is not null && Hash(data) != signature.Hash) changed.Add(block);
        }
        _changedBlocks = changed; _hipBlocks = null; _floats = null;
        Console.WriteLine($"ADS difference: {changed.Count:N0} changed blocks in {timer.Elapsed.TotalSeconds:N1}s.");
    }

    private static void CaptureHipBaselines(nint process)
    {
        if (_changedBlocks is null) { Console.WriteLine("Press F6 in ADS first."); return; }
        Stopwatch timer = Stopwatch.StartNew();
        Dictionary<ulong, byte[]> blocks = [];
        long bytes = 0;
        foreach (BlockCandidate block in _changedBlocks)
        {
            byte[]? data = ReadBlock(process, block);
            if (data is null) continue;
            blocks[block.Address] = data; bytes += data.Length;
        }
        _hipBlocks = blocks; _floats = null;
        Console.WriteLine($"HIP baselines: {blocks.Count:N0} blocks, {bytes / 1048576d:N1} MiB in {timer.Elapsed.TotalSeconds:N1}s.");
    }

    private static void CaptureAdsFloats(nint process)
    {
        if (_hipBlocks is null) { Console.WriteLine("Press F7 in HIP first."); return; }
        Stopwatch timer = Stopwatch.StartNew();
        List<FloatCandidate> floats = [];
        foreach ((ulong blockAddress, byte[] hip) in _hipBlocks)
        {
            byte[]? ads = ReadBlock(process, new(blockAddress, hip.Length));
            if (ads is null) continue;
            for (int offset = 0; offset <= hip.Length - 4; offset += 4)
            {
                float h = BitConverter.ToSingle(hip, offset), a = BitConverter.ToSingle(ads, offset);
                if (!float.IsFinite(h) || !float.IsFinite(a)) continue;
                float hm = Math.Abs(h), am = Math.Abs(a);
                if (hm < 0.05f || hm > 10f || am >= hm * 0.95f || am < hm * 0.02f) continue;
                if (a != 0 && MathF.CopySign(1, h) != MathF.CopySign(1, a)) continue;
                floats.Add(new(blockAddress, offset, blockAddress + (ulong)offset, h, a));
                if (floats.Count >= FloatCandidateLimit) throw new InvalidOperationException("Float candidate limit reached.");
            }
        }
        _floats = floats;
        Console.WriteLine($"ADS lower-float filter: {floats.Count:N0} candidates in {timer.Elapsed.TotalSeconds:N1}s.");
        PrintTop(floats, 30);
    }

    private static void FilterHipReturn(nint process)
    {
        if (_floats is null) { Console.WriteLine("Press F8 in ADS first."); return; }
        _floats = FilterByBlocks(process, _floats, (candidate, value) => Near(value, candidate.Hip, 0.05f));
        Console.WriteLine($"HIP return: {_floats.Count:N0} candidates remain.");
        PrintTop(_floats, 30);
    }

    private static void FilterAdsRecurrenceAndSave(nint process, int pid)
    {
        if (_floats is null) { Console.WriteLine("Press F9 in HIP first."); return; }
        _floats = FilterByBlocks(process, _floats, (candidate, value) =>
            Math.Abs(value) < Math.Abs(candidate.Hip) * 0.95f && Near(value, candidate.Ads, 0.15f));
        Console.WriteLine($"ADS recurrence: {_floats.Count:N0} candidates remain.");
        PrintTop(_floats, 100);
        string directory = Path.Combine(AppContext.BaseDirectory, "scanner-results");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"ads-{pid}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllLines(path, _floats.Select(c =>
            $"0x{c.Address:X16}\t{c.Hip.ToString("R", CultureInfo.InvariantCulture)}\t{c.Ads.ToString("R", CultureInfo.InvariantCulture)}\t{Math.Abs(c.Ads / c.Hip).ToString("F6", CultureInfo.InvariantCulture)}"));
        Console.WriteLine($"Saved results to {path}");
    }

    private static List<FloatCandidate> FilterByBlocks(nint process, List<FloatCandidate> candidates, Func<FloatCandidate, float, bool> predicate)
    {
        List<FloatCandidate> next = [];
        foreach (IGrouping<ulong, FloatCandidate> group in candidates.GroupBy(c => c.BlockAddress))
        {
            int length = _hipBlocks![group.Key].Length;
            byte[]? current = ReadBlock(process, new(group.Key, length));
            if (current is null) continue;
            foreach (FloatCandidate candidate in group)
            {
                float value = BitConverter.ToSingle(current, candidate.Offset);
                if (float.IsFinite(value) && predicate(candidate, value)) next.Add(candidate);
            }
        }
        return next;
    }

    private static bool Near(float value, float baseline, float fraction) =>
        Math.Abs(value - baseline) <= Math.Max(0.01f, Math.Abs(baseline) * fraction);

    private static void PrintTop(IEnumerable<FloatCandidate> source, int count)
    {
        foreach (FloatCandidate c in source.OrderBy(c => Math.Abs(c.Ads / c.Hip)).Take(count))
            Console.WriteLine($"  0x{c.Address:X16}: hip={c.Hip:R} ads={c.Ads:R} ratio={Math.Abs(c.Ads / c.Hip):F4}");
    }

    private static IEnumerable<BlockCandidate> EnumerateBlocks(nint process)
    {
        ulong address = 0, maximum = 0x00007FFFFFFEFFFFUL;
        nuint infoSize = (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
        while (address < maximum)
        {
            if (VirtualQueryEx(process, (nint)address, out MEMORY_BASIC_INFORMATION info, infoSize) == 0) yield break;
            ulong baseAddress = (ulong)info.BaseAddress, regionSize = (ulong)info.RegionSize;
            if (regionSize == 0) yield break;
            uint protection = info.Protect & 0xFF;
            bool writable = protection is 0x04 or 0x08 or 0x40 or 0x80;
            if (info.State == MemCommit && info.Type == MemPrivate && writable && (info.Protect & (PageGuard | PageNoAccess)) == 0)
            {
                ulong end = baseAddress + regionSize;
                for (ulong block = baseAddress; block < end; block += BlockSize)
                    yield return new(block, checked((int)Math.Min((ulong)BlockSize, end - block)));
            }
            ulong next = baseAddress + regionSize;
            if (next <= address) yield break;
            address = next;
        }
    }

    private static byte[]? ReadBlock(nint process, BlockCandidate block)
    {
        byte[] data = new byte[block.Length];
        return ReadProcessMemory(process, (nint)block.Address, data, (nuint)data.Length, out nuint read) && read == (nuint)data.Length ? data : null;
    }

    private static ulong Hash(byte[] data)
        => Hash(data, data.Length);

    private static ulong Hash(byte[] data, int length)
    {
        ulong hash = 14695981039346656037UL;
        for (int index = 0; index < length; index++) { hash ^= data[index]; hash *= 1099511628211UL; }
        return hash;
    }

    private static ParallelOptions MotionParallelOptions() => new()
    {
        MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8)
    };

    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private readonly record struct BlockSignature(int Length, ulong Hash);
    private readonly record struct BlockCandidate(ulong Address, int Length);
    private readonly record struct FloatCandidate(ulong BlockAddress, int Offset, ulong Address, float Hip, float Ads);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public nint BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State, Protect, Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll")] private static extern nuint VirtualQueryEx(nint process, nint address, out MEMORY_BASIC_INFORMATION info, nuint length);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
