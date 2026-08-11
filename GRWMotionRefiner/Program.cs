using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace GRWMotionRefiner;

internal static class Program
{
    private const uint ProcessVmRead = 0x0010, ProcessQueryInformation = 0x0400;
    private const int VkF5 = 0x74, VkF6 = 0x75, VkF7 = 0x76, VkF8 = 0x77, VkF11 = 0x7A, VkF12 = 0x7B;

    public static int Main(string[] args)
    {
        if (args.Length != 2 || !args[0].Equals("--load", StringComparison.OrdinalIgnoreCase) || !File.Exists(args[1]))
        {
            Console.Error.WriteLine("Usage: GRWMotionRefiner --load <motion-result-file>");
            return 2;
        }
        List<Candidate> candidates = Load(args[1]);
        Process[] games = Process.GetProcessesByName("GRW");
        if (games.Length != 1) { Console.Error.WriteLine($"Expected one GRW process; found {games.Length}."); return 3; }
        using Process game = games[0];
        nint process = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, game.Id);
        if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        string statusPath = Path.Combine(AppContext.BaseDirectory, "motion-refine.status");
        File.WriteAllText(statusPath, "");
        void Report(string message) { Console.WriteLine(message); File.AppendAllText(statusPath, message + Environment.NewLine); }
        try
        {
            Report($"Loaded {candidates.Count:N0} motion candidates for PID {game.Id}.");
            Report("F5 IDLE zero   F6 HIP JOG   F7 HIP WALK   F8 ADS JOG");
            Report("F11 IDLE return + save   F12 exit");
            int[] keys = [VkF5, VkF6, VkF7, VkF8, VkF11, VkF12];
            Dictionary<int, bool> previous = keys.ToDictionary(key => key, _ => false);
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
                            candidates = candidates.Where(c => TryRead(process, c.Address, out float v) && Math.Abs(v) < 0.005f).ToList();
                            Report($"F5 IDLE zero: {candidates.Count:N0} remain.");
                        }
                        else if (key == VkF6)
                        {
                            List<Candidate> next = [];
                            foreach (Candidate c in candidates)
                            {
                                if (!TryRead(process, c.Address, out float v) || Math.Abs(v) < 0.02f || !SameSign(v, c.OriginalHip)) continue;
                                float ratio = Math.Abs(v / c.OriginalHip);
                                if (ratio is >= 0.45f and <= 1.75f) { c.Jog = v; next.Add(c); }
                            }
                            candidates = next; Report($"F6 HIP JOG recurrence: {candidates.Count:N0} remain.");
                        }
                        else if (key == VkF7)
                        {
                            List<Candidate> next = [];
                            foreach (Candidate c in candidates)
                            {
                                if (!TryRead(process, c.Address, out float v) || c.Jog == 0 || !SameSign(v, c.Jog)) continue;
                                float ratio = Math.Abs(v / c.Jog);
                                if (ratio is >= 0.05f and <= 0.90f) { c.Walk = v; next.Add(c); }
                            }
                            candidates = next; Report($"F7 HIP WALK lower than jog: {candidates.Count:N0} remain.");
                        }
                        else if (key == VkF8)
                        {
                            List<Candidate> next = [];
                            foreach (Candidate c in candidates)
                            {
                                if (!TryRead(process, c.Address, out float v) || c.Jog == 0 || c.Walk == 0 || !SameSign(v, c.Jog)) continue;
                                float jogRatio = Math.Abs(v / c.Jog);
                                float walkRatio = Math.Abs(v / c.Walk);
                                if (jogRatio is >= 0.02f and <= 0.90f && walkRatio <= 0.95f) { c.Ads = v; next.Add(c); }
                            }
                            candidates = next; Report($"F8 ADS JOG below HIP walk: {candidates.Count:N0} remain.");
                        }
                        else if (key == VkF11)
                        {
                            candidates = candidates.Where(c => TryRead(process, c.Address, out float v) && Math.Abs(v) < 0.005f).ToList();
                            string directory = Path.Combine(AppContext.BaseDirectory, "scanner-results");
                            Directory.CreateDirectory(directory);
                            string path = Path.Combine(directory, $"motion-refined-{game.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                            File.WriteAllLines(path, candidates.Select(c => string.Join('\t',
                                $"0x{c.Address:X16}", F(c.Jog), F(c.Walk), F(c.Ads), F(Math.Abs(c.Walk / c.Jog)), F(Math.Abs(c.Ads / c.Jog)))));
                            Report($"F11 IDLE return: {candidates.Count:N0} remain; saved to {path}");
                        }
                    }
                    previous[key] = down;
                }
                Thread.Sleep(25);
            }
        }
        finally { CloseHandle(process); }
    }

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static bool SameSign(float a, float b) => MathF.CopySign(1, a) == MathF.CopySign(1, b);
    private static bool TryRead(nint process, ulong address, out float value)
    {
        byte[] bytes = new byte[4];
        bool ok = ReadProcessMemory(process, (nint)address, bytes, 4, out nuint read) && read == 4;
        value = ok ? BitConverter.ToSingle(bytes) : 0;
        return ok && float.IsFinite(value);
    }
    private static List<Candidate> Load(string path)
    {
        List<Candidate> result = [];
        foreach (string line in File.ReadLines(path))
        {
            string[] f = line.Split('\t');
            if (f.Length < 3 || !ulong.TryParse(f[0].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong address) ||
                !float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float hip) ||
                !float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float ads)) continue;
            result.Add(new(address, hip, ads));
        }
        return result;
    }
    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private sealed class Candidate(ulong address, float hip, float ads)
    {
        public ulong Address { get; } = address;
        public float OriginalHip { get; } = hip;
        public float OriginalAds { get; } = ads;
        public float Jog { get; set; }
        public float Walk { get; set; }
        public float Ads { get; set; }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
