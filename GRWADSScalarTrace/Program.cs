using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

const ulong SiteRva = 0x133CFDFF, TargetRva = 0xA0CF70;
const uint Access = 0x438, Allocation = 0x3000, Release = 0x8000, RWX = 0x40;
const int F9 = 0x78, F12 = 0x7B, Slot = 128;

using Process game = Process.GetProcessesByName("GRW").SingleOrDefault()
    ?? throw new InvalidOperationException("Exactly one GRW process must be running.");
ulong imageBase = (ulong)game.MainModule!.BaseAddress.ToInt64();
ulong site = imageBase + SiteRva, target = imageBase + TargetRva, cave = 0;
byte[] original = [0xE8, .. BitConverter.GetBytes(Rel(site, 5, target))];
nint process = OpenProcess(Access, false, game.Id);
if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
bool installed = false;
string logPath = Path.Combine(AppContext.BaseDirectory, "ads-scalar-trace.csv");

try
{
    byte[] current = Read(process, site, 5);
    if (!current.SequenceEqual(original))
        throw new InvalidOperationException($"Unexpected site bytes: {Convert.ToHexString(current)}");
    cave = AllocateNear(process, site);
    byte[] code = new byte[132];
    int p = 0;
    code[p++] = 0xF3; code[p++] = 0x0F; code[p++] = 0x11; code[p++] = 0x3D;
    int displacement = checked((int)((long)(cave + Slot) - (long)(cave + (ulong)p + 4)));
    BitConverter.GetBytes(displacement).CopyTo(code, p); p += 4;
    code[p++] = 0xE9;
    BitConverter.GetBytes(Rel(cave + (ulong)p - 1, 5, target)).CopyTo(code, p);
    Write(process, cave, code);
    Patch(process, site, [0xE8, .. BitConverter.GetBytes(Rel(site, 5, cave))]);
    installed = true;
    void Restore() { if (installed) { try { Patch(process, site, original); } catch { } installed = false; } }
    AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; Restore(); };

    File.WriteAllText(logPath, "elapsed_ms,value\n");
    Console.WriteLine("ADS scalar tracer active. F9 record toggle; starts OFF. F12 restores and exits.");
    bool oldF9 = false, oldF12 = false, recording = false;
    Stopwatch timer = new();
    using StreamWriter log = new(logPath, append: true) { AutoFlush = true };
    while (true)
    {
        bool f9 = (GetAsyncKeyState(F9) & 0x8000) != 0;
        bool f12 = (GetAsyncKeyState(F12) & 0x8000) != 0;
        if (f9 && !oldF9)
        {
            recording = !recording;
            if (recording) { timer.Restart(); Console.WriteLine("Recording ON"); }
            else { timer.Stop(); Console.WriteLine("Recording OFF"); }
        }
        if (f12 && !oldF12) break;
        if (recording)
        {
            float value = BitConverter.ToSingle(Read(process, cave + Slot, 4));
            log.WriteLine($"{timer.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)},{value.ToString("R", CultureInfo.InvariantCulture)}");
        }
        oldF9 = f9; oldF12 = f12;
        Thread.Sleep(2);
    }
    Restore();
}
finally
{
    if (installed) try { Patch(process, site, original); } catch { }
    if (cave != 0) VirtualFreeEx(process, (nint)cave, 0, Release);
    CloseHandle(process);
}

static int Rel(ulong instruction, int length, ulong target)
{
    long value = (long)target - ((long)instruction + length);
    if (value is < int.MinValue or > int.MaxValue) throw new InvalidOperationException("rel32 out of range");
    return (int)value;
}
static ulong AllocateNear(nint process, ulong site)
{
    for (ulong distance = 0x10000; distance < 0x70000000; distance += 0x10000)
        foreach (ulong address in new[] { (site + distance) & ~0xFFFFUL, (site - distance) & ~0xFFFFUL })
        {
            nint result = VirtualAllocEx(process, (nint)address, 4096, Allocation, RWX);
            if (result != 0) return (ulong)result.ToInt64();
        }
    throw new Win32Exception(Marshal.GetLastWin32Error());
}
static byte[] Read(nint process, ulong address, int length)
{
    byte[] bytes = new byte[length];
    if (!ReadProcessMemory(process, (nint)address, bytes, (nuint)length, out nuint read) || read != (nuint)length)
        throw new Win32Exception(Marshal.GetLastWin32Error());
    return bytes;
}
static void Write(nint process, ulong address, byte[] bytes)
{
    if (!WriteProcessMemory(process, (nint)address, bytes, (nuint)bytes.Length, out nuint written) || written != (nuint)bytes.Length)
        throw new Win32Exception(Marshal.GetLastWin32Error());
}
static void Patch(nint process, ulong address, byte[] bytes)
{
    if (!VirtualProtectEx(process, (nint)address, (nuint)bytes.Length, RWX, out uint old))
        throw new Win32Exception(Marshal.GetLastWin32Error());
    try { Write(process, address, bytes); FlushInstructionCache(process, (nint)address, (nuint)bytes.Length); }
    finally { VirtualProtectEx(process, (nint)address, (nuint)bytes.Length, old, out _); }
}

[DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, int id);
[DllImport("kernel32.dll", SetLastError = true)] static extern nint VirtualAllocEx(nint process, nint address, nuint size, uint type, uint protection);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualFreeEx(nint process, nint address, nuint size, uint type);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualProtectEx(nint process, nint address, nuint size, uint protection, out uint old);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(nint process, nint address, byte[] buffer, nuint size, out nuint written);
[DllImport("kernel32.dll")] static extern bool FlushInstructionCache(nint process, nint address, nuint size);
[DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
[DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
