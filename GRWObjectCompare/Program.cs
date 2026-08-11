using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

if (args.Length != 2 || !TryAddress(args[0], out ulong left) || !TryAddress(args[1], out ulong right))
{
    Console.Error.WriteLine("Usage: GRWObjectCompare 0xLEFT 0xRIGHT");
    return 2;
}

using Process game = Process.GetProcessesByName("GRW").Single();
nint process = OpenProcess(0x0010 | 0x0400, false, game.Id);
if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
try
{
    byte[] a = Read(process, left, 0x300), b = Read(process, right, 0x300);
    Console.WriteLine($"offset\tleft_float\tright_float\tdelta");
    for (int offset = 0; offset <= a.Length - 4; offset += 4)
    {
        float x = BitConverter.ToSingle(a, offset), y = BitConverter.ToSingle(b, offset);
        if (!Plausible(x) && !Plausible(y)) continue;
        Console.WriteLine($"0x{offset:X3}\t{F(x)}\t{F(y)}\t{F(y - x)}");
    }
    Console.WriteLine("offset\tleft_qword\tright_qword");
    for (int offset = 0; offset <= a.Length - 8; offset += 8)
    {
        ulong x = BitConverter.ToUInt64(a, offset), y = BitConverter.ToUInt64(b, offset);
        if (x != y) Console.WriteLine($"0x{offset:X3}\t0x{x:X16}\t0x{y:X16}");
    }
    PrintStrings("left", a);
    PrintStrings("right", b);
}
finally { CloseHandle(process); }

return 0;

static bool TryAddress(string text, out ulong value) =>
    ulong.TryParse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text,
        NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
static bool Plausible(float value) => float.IsFinite(value) && MathF.Abs(value) is >= 0.00001f and <= 10.0f;
static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
static void PrintStrings(string label, byte[] bytes)
{
    List<string> strings = [];
    for (int start = 0; start < bytes.Length;)
    {
        int end = start;
        while (end < bytes.Length && bytes[end] is >= 0x20 and <= 0x7E) end++;
        if (end - start >= 4) strings.Add($"0x{start:X3}:{System.Text.Encoding.ASCII.GetString(bytes, start, end - start)}");
        start = Math.Max(start + 1, end + 1);
    }
    Console.WriteLine($"{label}_strings\t{string.Join(" | ", strings)}");
}
static byte[] Read(nint process, ulong address, int length)
{
    byte[] bytes = new byte[length];
    if (!ReadProcessMemory(process, (nint)address, bytes, (nuint)length, out nuint read) || read != (nuint)length)
        throw new Win32Exception(Marshal.GetLastWin32Error(), $"Read failed at 0x{address:X16}");
    return bytes;
}

[DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, int processId);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
[DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
