using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Iced.Intel;

if (args.Length != 4 || args[0] != "--pid" || args[2] != "--writer" ||
    !int.TryParse(args[1], out int pid))
{
    Console.Error.WriteLine("Usage: GRWCodeDump --pid 1234 --writer 0xABCDEF");
    return 2;
}

string text = args[3].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[3][2..] : args[3];
if (!ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong writer)) return 2;

const uint ProcessVmRead = 0x0010;
const uint ProcessQueryInformation = 0x0400;
nint process = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, pid);
if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error());

try
{
    ulong baseAddress = writer - 256;
    byte[] bytes = new byte[2304];
    if (!ReadProcessMemory(process, (nint)baseAddress, bytes, (nuint)bytes.Length, out nuint read) || read != (nuint)bytes.Length)
        throw new Win32Exception(Marshal.GetLastWin32Error(), "ReadProcessMemory failed");

    List<Instruction>? selected = null;
    for (int startOffset = 0; startOffset <= 256; startOffset++)
    {
        Decoder decoder = Decoder.Create(64, new ByteArrayCodeReader(bytes[startOffset..]));
        decoder.IP = baseAddress + (ulong)startOffset;
        List<Instruction> instructions = [];
        bool found = false;
        while (decoder.IP < writer + 2000)
        {
            decoder.Decode(out Instruction instruction);
            if (instruction.IsInvalid)
            {
                if (found) selected = instructions;
                break;
            }
            instructions.Add(instruction);
            if (instruction.IP == writer)
            {
                found = true;
            }
            if (found && decoder.IP >= writer + 1900)
            {
                selected = instructions;
                break;
            }
        }
        if (selected is not null) break;
    }

    if (selected is null)
    {
        Console.Error.WriteLine("Could not establish a decoding path through the requested instruction.");
        return 3;
    }

    MasmFormatter formatter = new();
    formatter.Options.HexPrefix = "0x";
    formatter.Options.HexSuffix = null;
    StringOutput output = new();
    foreach (Instruction instruction in selected.Where(i => i.IP >= writer - 128 && i.IP <= writer + 2000))
    {
        output.Reset();
        formatter.Format(instruction, output);
        string marker = instruction.IP == writer ? "  <-- captured writer" : "";
        Console.WriteLine($"0x{instruction.IP:X16}  {output}{marker}");
    }
}
finally { CloseHandle(process); }

return 0;

[DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, int pid);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
[DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);

sealed class StringOutput : FormatterOutput
{
    private readonly System.Text.StringBuilder _builder = new();
    public override void Write(string text, FormatterTextKind kind) => _builder.Append(text);
    public void Reset() => _builder.Clear();
    public override string ToString() => _builder.ToString();
}
