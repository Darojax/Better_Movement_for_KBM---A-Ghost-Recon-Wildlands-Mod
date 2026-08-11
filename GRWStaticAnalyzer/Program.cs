using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Iced.Intel;

if (args.Length != 4 || args[0] != "--pid" || args[2] != "--writer" || !int.TryParse(args[1], out int pid))
{
    Console.Error.WriteLine("Usage: GRWStaticAnalyzer --pid 1234 --writer 0xABCDEF");
    return 2;
}

string text = args[3].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[3][2..] : args[3];
if (!ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong writer)) return 2;

const int LookBehind = 1024 * 1024;
const int LookAhead = 128;
const uint ProcessVmRead = 0x0010, ProcessQueryInformation = 0x0400;
ulong regionStart = writer - LookBehind;
byte[] bytes = new byte[LookBehind + LookAhead];
nint process = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, pid);
if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error());

try
{
    if (!ReadProcessMemory(process, (nint)regionStart, bytes, (nuint)bytes.Length, out nuint read) || read != (nuint)bytes.Length)
        throw new Win32Exception(Marshal.GetLastWin32Error(), "ReadProcessMemory failed");

    InstructionInfoFactory infoFactory = new();
    MasmFormatter formatter = new();
    formatter.Options.HexPrefix = "0x";
    formatter.Options.HexSuffix = null;
    TextOutput output = new();
    List<(Instruction Instruction, OpAccess Access)> candidates = [];

    for (int offset = 0; offset < LookBehind; offset++)
    {
        Decoder decoder = Decoder.Create(64, new ByteArrayCodeReader(bytes[offset..Math.Min(bytes.Length, offset + 15)]));
        decoder.IP = regionStart + (ulong)offset;
        decoder.Decode(out Instruction instruction);
        if (instruction.IsInvalid) continue;
        InstructionInfo info = infoFactory.GetInfo(instruction);
        foreach (UsedMemory memory in info.GetUsedMemory())
        {
            if (memory.Base != Register.RSP || memory.Displacement != 0x30) continue;
            if (memory.Access is not (OpAccess.Write or OpAccess.CondWrite or OpAccess.ReadWrite or OpAccess.ReadCondWrite)) continue;
            if (LinearPathReachesWriter(bytes, regionStart, instruction.IP, writer))
                candidates.Add((instruction, memory.Access));
        }
    }

    Console.WriteLine($"Scanned {LookBehind:N0} code bytes preceding 0x{writer:X16}.");
    if (candidates.Count == 0)
    {
        Console.WriteLine("No aligned instruction stream writes exactly [rsp+0x30] before the captured writer.");
        return 0;
    }

    foreach ((Instruction instruction, OpAccess access) in candidates.DistinctBy(c => c.Instruction.IP))
    {
        output.Reset(); formatter.Format(instruction, output);
        Console.WriteLine($"0x{instruction.IP:X16}  {output}  ({access}, {writer - instruction.IP:N0} bytes before writer)");
    }
}
finally { CloseHandle(process); }

return 0;

static bool LinearPathReachesWriter(byte[] bytes, ulong regionStart, ulong candidate, ulong writer)
{
    int offset = checked((int)(candidate - regionStart));
    Decoder decoder = Decoder.Create(64, new ByteArrayCodeReader(bytes[offset..]));
    decoder.IP = candidate;
    int count = 0;
    while (decoder.IP <= writer && count++ < 200_000)
    {
        decoder.Decode(out Instruction instruction);
        if (instruction.IsInvalid) return false;
        if (instruction.IP == writer) return instruction.Mnemonic == Mnemonic.Movss;
        if (instruction.IP > writer) return false;
    }
    return false;
}

[DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, int pid);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
[DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);

sealed class TextOutput : FormatterOutput
{
    private readonly System.Text.StringBuilder builder = new();
    public override void Write(string text, FormatterTextKind kind) => builder.Append(text);
    public void Reset() => builder.Clear();
    public override string ToString() => builder.ToString();
}
