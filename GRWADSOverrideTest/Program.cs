using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

const ulong SiteRva = 0x133CFDFF, TargetRva = 0xA0CF70;
const uint ProcessAccess = 0x438, CommitReserve = 0x3000, Release = 0x8000, RWX = 0x40;
const int F6 = 0x75, F7 = 0x76, F8 = 0x77, F12 = 0x7B;

using Process game = Process.GetProcessesByName("GRW").SingleOrDefault()
    ?? throw new InvalidOperationException("Exactly one GRW process must be running.");
ulong imageBase = (ulong)game.MainModule!.BaseAddress.ToInt64();
ulong site = imageBase + SiteRva, target = imageBase + TargetRva, cave = 0;
byte[] original = [0xE8, .. BitConverter.GetBytes(Rel(site, 5, target))];
nint process = OpenProcess(ProcessAccess, false, game.Id);
if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
bool installed = false;

try
{
    byte[] current = Read(process, site, 5);
    if (!current.SequenceEqual(original))
        throw new InvalidOperationException($"Unexpected site bytes: {Convert.ToHexString(current)}");

    cave = AllocateNear(process, site);
    const int Low = 128, High = 132, Replacement = 136;
    byte[] code = new byte[140];
    int p = 0;
    EmitRip(code, ref p, [0x0F, 0x2F, 0x3D], cave, Low); // comiss xmm7,[low]
    code[p++] = 0x72; code[p++] = 17;                       // jb original target
    EmitRip(code, ref p, [0x0F, 0x2F, 0x3D], cave, High); // comiss xmm7,[high]
    code[p++] = 0x77; code[p++] = 8;                        // ja original target
    EmitRip(code, ref p, [0xF3, 0x0F, 0x10, 0x3D], cave, Replacement); // movss xmm7,[replacement]
    code[p++] = 0xE9;
    BitConverter.GetBytes(Rel(cave + (ulong)p - 1, 5, target)).CopyTo(code, p);
    BitConverter.GetBytes(1.25f).CopyTo(code, Low);
    BitConverter.GetBytes(1.45f).CopyTo(code, High);
    BitConverter.GetBytes(1.3500115f).CopyTo(code, Replacement);
    Write(process, cave, code);

    byte[] redirect = [0xE8, .. BitConverter.GetBytes(Rel(site, 5, cave))];
    Patch(process, site, redirect);
    installed = true;
    void Restore() { if (installed) { try { Patch(process, site, original); } catch { } installed = false; } }
    AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; Restore(); };
    Console.WriteLine("Walk-ADS calibration active. F6 -0.1, F7 +0.1, F8 A/B, F12 exit. Starts OFF; selected 1.60.");
    bool previousF6 = false, previousF7 = false, previousF8 = false, previousF12 = false, enabled = false;
    float selected = 1.60f;
    while (true)
    {
        bool f6 = (GetAsyncKeyState(F6) & 0x8000) != 0;
        bool f7 = (GetAsyncKeyState(F7) & 0x8000) != 0;
        bool f8 = (GetAsyncKeyState(F8) & 0x8000) != 0;
        bool f12 = (GetAsyncKeyState(F12) & 0x8000) != 0;
        if (f6 && !previousF6)
        {
            selected = MathF.Max(1.40f, MathF.Round(selected - 0.10f, 2));
            if (enabled) Write(process, cave + Replacement, BitConverter.GetBytes(selected));
            Console.WriteLine($"Selected {selected:F2}" + (enabled ? " (ON)" : " (OFF)"));
        }
        if (f7 && !previousF7)
        {
            selected = MathF.Min(3.00f, MathF.Round(selected + 0.10f, 2));
            if (enabled) Write(process, cave + Replacement, BitConverter.GetBytes(selected));
            Console.WriteLine($"Selected {selected:F2}" + (enabled ? " (ON)" : " (OFF)"));
        }
        if (f8 && !previousF8)
        {
            enabled = !enabled;
            Write(process, cave + Replacement, BitConverter.GetBytes(enabled ? selected : 1.3500115f));
            Console.WriteLine(enabled ? $"Override ON ({selected:F2})" : "Override OFF (vanilla)");
        }
        if (f12 && !previousF12) break;
        previousF6 = f6;
        previousF7 = f7;
        previousF8 = f8;
        previousF12 = f12;
        Thread.Sleep(20);
    }
    Restore();
}
finally
{
    if (installed) try { Patch(process, site, original); } catch { }
    if (cave != 0) VirtualFreeEx(process, (nint)cave, 0, Release);
    CloseHandle(process);
}

static void EmitRip(byte[] code, ref int p, byte[] opcode, ulong cave, int dataOffset)
{
    foreach (byte b in opcode) code[p++] = b;
    int displacement = checked((int)((long)(cave + (ulong)dataOffset) - (long)(cave + (ulong)p + 4)));
    BitConverter.GetBytes(displacement).CopyTo(code, p); p += 4;
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
            nint result = VirtualAllocEx(process, (nint)address, 4096, CommitReserve, RWX);
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
