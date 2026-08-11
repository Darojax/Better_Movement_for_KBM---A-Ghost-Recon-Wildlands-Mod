using System.Diagnostics;
using System.Runtime.InteropServices;

nint memory = Marshal.AllocHGlobal(4);
Console.WriteLine($"{Process.GetCurrentProcess().Id} 0x{memory:X16}");
Console.Out.Flush();
try
{
    int value = 0;
    while (true)
    {
        Marshal.WriteInt32(memory, value++);
        Thread.Sleep(100);
    }
}
finally { Marshal.FreeHGlobal(memory); }
