using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

const ulong HookRva = 0x29D7175;
const ulong SessionObject = 0x0000019629A07A50;
const uint ProcessVmOperation = 0x0008, ProcessVmRead = 0x0010, ProcessVmWrite = 0x0020, ProcessQueryInformation = 0x0400;
const uint MemCommit = 0x1000, MemReserve = 0x2000, MemRelease = 0x8000;
const uint PageReadWrite = 0x04, PageExecuteRead = 0x20, PageExecuteReadWrite = 0x40;
const int VkF7 = 0x76, VkF8 = 0x77, VkF12 = 0x7B;
byte[] original = [0xF3, 0x0F, 0x11, 0x51, 0x20]; // movss [rcx+20h],xmm2

using Process game = Process.GetProcessesByName("GRW").SingleOrDefault()
    ?? throw new InvalidOperationException("Exactly one GRW process must be running.");
ulong imageBase = unchecked((ulong)(game.MainModule?.BaseAddress.ToInt64() ?? 0));
ulong hook = imageBase + HookRva;
nint process = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryInformation, false, game.Id);
if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed.");

ulong cave = 0;
bool installed = false;
try
{
    byte[] current = ReadExact(process, hook, original.Length);
    if (!current.SequenceEqual(original))
        throw new InvalidOperationException($"Hook-site bytes are not original: {Convert.ToHexString(current)}");

    float objectOutput = BitConverter.ToSingle(ReadExact(process, SessionObject + 0x20, 4));
    if (!float.IsFinite(objectOutput) || objectOutput is < 0.25f or > 1.05f)
        throw new InvalidOperationException($"Session object validation failed: output={objectOutput:R}");

    cave = AllocateNear(process, hook);
    byte[] code = new byte[44];
    int p = 0;
    code[p++] = 0x50;                                      // push rax
    code[p++] = 0x48; code[p++] = 0xB8;                   // mov rax,imm64
    BitConverter.GetBytes(SessionObject).CopyTo(code, p); p += 8;
    code[p++] = 0x48; code[p++] = 0x39; code[p++] = 0xC1; // cmp rcx,rax
    code[p++] = 0x58;                                      // pop rax
    code[p++] = 0x75; code[p++] = 0x08;                   // jne original store
    int loadOffset = p;
    code[p++] = 0xF3; code[p++] = 0x0F; code[p++] = 0x10; code[p++] = 0x15; // movss xmm2,[rip+disp32]
    int loadDispOffset = p; p += 4;
    original.CopyTo(code, p); p += original.Length;
    int returnJumpOffset = p;
    code[p++] = 0xE9; p += 4;
    int constantOffset = 40;
    BitConverter.GetBytes(constantOffset - (loadOffset + 8)).CopyTo(code, loadDispOffset);
    BitConverter.GetBytes(CheckedRelative(cave + (ulong)returnJumpOffset, 5, hook + 5)).CopyTo(code, returnJumpOffset + 1);
    BitConverter.GetBytes(1.0f).CopyTo(code, constantOffset);
    WriteExact(process, cave, code);
    if (!VirtualProtectEx(process, (nint)cave, (nuint)code.Length, PageExecuteRead, out _))
        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not make trampoline executable.");

    byte[] redirect = new byte[5];
    redirect[0] = 0xE9;
    BitConverter.GetBytes(CheckedRelative(hook, 5, cave)).CopyTo(redirect, 1);

    void Restore()
    {
        if (!installed) return;
        try { PatchCode(process, hook, original); } catch { }
        installed = false;
    }

    Console.CancelKeyPress += (_, e) => { e.Cancel = true; Restore(); };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
    Console.WriteLine("GRW ADS writer-hook test - current session/original weapon only");
    Console.WriteLine($"PID {game.Id}; hook 0x{hook:X16}; object 0x{SessionObject:X16}");
    Console.WriteLine("F7 enable forced 1.00 output   F8 restore original writer   F12 restore + exit");
    bool oldF7 = false, oldF8 = false, oldF12 = false;
    while (true)
    {
        bool f7 = Down(VkF7), f8 = Down(VkF8), f12 = Down(VkF12);
        if (f7 && !oldF7 && !installed)
        {
            PatchCode(process, hook, redirect);
            installed = true;
            Console.WriteLine("Conditional writer hook ENABLED.");
        }
        if (f8 && !oldF8 && installed)
        {
            Restore();
            Console.WriteLine("Original writer RESTORED.");
        }
        if (f12 && !oldF12) break;
        oldF7 = f7; oldF8 = f8; oldF12 = f12;
        Thread.Sleep(20);
    }
    Restore();
    return 0;
}
finally
{
    if (installed) { try { PatchCode(process, hook, original); } catch { } }
    if (cave != 0) VirtualFreeEx(process, (nint)cave, 0, MemRelease);
    CloseHandle(process);
}

static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
static int CheckedRelative(ulong instruction, int length, ulong target)
{
    long relative = unchecked((long)target - ((long)instruction + length));
    if (relative is < int.MinValue or > int.MaxValue) throw new InvalidOperationException("Trampoline is outside rel32 range.");
    return (int)relative;
}
static ulong AllocateNear(nint process, ulong site)
{
    const ulong granularity = 0x10000, range = 0x70000000;
    ulong low = (site > range ? site - range : granularity) & ~(granularity - 1);
    ulong high = (site + range) & ~(granularity - 1);
    for (ulong distance = granularity; distance < range; distance += granularity)
    {
        foreach (ulong hint in new[] { (site + distance) & ~(granularity - 1), (site - distance) & ~(granularity - 1) })
        {
            if (hint < low || hint > high) continue;
            nint allocation = VirtualAllocEx(process, (nint)hint, 4096, MemCommit | MemReserve, PageReadWrite);
            if (allocation != 0) return unchecked((ulong)allocation.ToInt64());
        }
    }
    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not allocate a nearby trampoline page.");
}
static byte[] ReadExact(nint process, ulong address, int count)
{
    byte[] bytes = new byte[count];
    if (!ReadProcessMemory(process, (nint)address, bytes, (nuint)count, out nuint read) || read != (nuint)count)
        throw new Win32Exception(Marshal.GetLastWin32Error(), $"Read failed at 0x{address:X16}.");
    return bytes;
}
static void WriteExact(nint process, ulong address, byte[] bytes)
{
    if (!WriteProcessMemory(process, (nint)address, bytes, (nuint)bytes.Length, out nuint written) || written != (nuint)bytes.Length)
        throw new Win32Exception(Marshal.GetLastWin32Error(), $"Write failed at 0x{address:X16}.");
}
static void PatchCode(nint process, ulong address, byte[] bytes)
{
    if (!VirtualProtectEx(process, (nint)address, (nuint)bytes.Length, PageExecuteReadWrite, out uint old))
        throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualProtectEx failed.");
    try { WriteExact(process, address, bytes); FlushInstructionCache(process, (nint)address, (nuint)bytes.Length); }
    finally { VirtualProtectEx(process, (nint)address, (nuint)bytes.Length, old, out _); }
}

[DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, int pid);
[DllImport("kernel32.dll", SetLastError = true)] static extern nint VirtualAllocEx(nint process, nint address, nuint size, uint allocationType, uint protect);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualFreeEx(nint process, nint address, nuint size, uint freeType);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualProtectEx(nint process, nint address, nuint size, uint newProtect, out uint oldProtect);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(nint process, nint address, byte[] buffer, nuint size, out nuint written);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool FlushInstructionCache(nint process, nint address, nuint size);
[DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
[DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
