using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

const ulong SiteRva=0x133CFD31;
const uint ProcessVmOperation=0x0008,ProcessVmRead=0x0010,ProcessVmWrite=0x0020,ProcessQueryInformation=0x0400;
const uint MemCommit=0x1000,MemReserve=0x2000,MemRelease=0x8000,PageReadWrite=0x04,PageExecuteRead=0x20,PageExecuteReadWrite=0x40;
const int VkF5=0x74,VkF6=0x75,VkF12=0x7B;
byte[] original=[0xF3,0x0F,0x11,0x44,0x24,0x30];
using Process game=Process.GetProcessesByName("GRW").SingleOrDefault()??throw new InvalidOperationException("Exactly one GRW process must run.");
ulong imageBase=(ulong)(game.MainModule?.BaseAddress.ToInt64()??0),site=imageBase+SiteRva,cave=0;
nint process=OpenProcess(ProcessVmOperation|ProcessVmRead|ProcessVmWrite|ProcessQueryInformation,false,game.Id);
if(process==0)throw new Win32Exception(Marshal.GetLastWin32Error());
bool installed=false;
string logPath=Path.Combine(AppContext.BaseDirectory,"multiplier-captures.log");
File.WriteAllText(logPath,"");
try
{
    byte[] current=Read(process,site,6);if(!current.SequenceEqual(original))throw new InvalidOperationException($"Unexpected site bytes {Convert.ToHexString(current)}");
    cave=AllocateNear(process,site);
    const int Data=128,Result=Data,M60=Data+4,M64=Data+8,M68=Data+12,R13=Data+16,R15=Data+20,Ecx=Data+24,Edi=Data+28;
    byte[] code=new byte[Data+32];int p=0;
    original.CopyTo(code,p);p+=6; // preserve exact store
    code[p++]=0xF3;code[p++]=0x0F;code[p++]=0x11;code[p++]=0x05;PutRel32(code,ref p,cave+(ulong)p+4,cave+Result); // result xmm0
    code[p++]=0x50; // push rax
    void CopyField(byte displacement,int slot){code[p++]=0x8B;code[p++]=0x42;code[p++]=displacement;code[p++]=0x89;code[p++]=0x05;PutRel32(code,ref p,cave+(ulong)p+4,cave+(ulong)slot);}
    CopyField(0x60,M60);CopyField(0x64,M64);CopyField(0x68,M68);
    code[p++]=0x44;code[p++]=0x89;code[p++]=0xE8;code[p++]=0x89;code[p++]=0x05;PutRel32(code,ref p,cave+(ulong)p+4,cave+R13);
    code[p++]=0x41;code[p++]=0x0F;code[p++]=0xB6;code[p++]=0xC7;code[p++]=0x89;code[p++]=0x05;PutRel32(code,ref p,cave+(ulong)p+4,cave+R15);
    code[p++]=0x89;code[p++]=0xC8;code[p++]=0x89;code[p++]=0x05;PutRel32(code,ref p,cave+(ulong)p+4,cave+Ecx);
    code[p++]=0x89;code[p++]=0xF8;code[p++]=0x89;code[p++]=0x05;PutRel32(code,ref p,cave+(ulong)p+4,cave+Edi);
    code[p++]=0x58;code[p++]=0xE9;int back=p;p+=4;BitConverter.GetBytes(Rel(cave+(ulong)back-1,5,site+6)).CopyTo(code,back);
    Write(process,cave,code);if(!VirtualProtectEx(process,(nint)cave,(nuint)code.Length,PageExecuteRead,out _))throw new Win32Exception(Marshal.GetLastWin32Error());
    byte[] redirect=new byte[6];redirect[0]=0xE9;BitConverter.GetBytes(Rel(site,5,cave)).CopyTo(redirect,1);redirect[5]=0x90;
    Patch(process,site,redirect);installed=true;
    void Restore(){if(!installed)return;try{Patch(process,site,original);}catch{}installed=false;}
    AppDomain.CurrentDomain.ProcessExit+=(_,_)=>Restore();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;Restore();};
    void Capture(string label)
    {
        byte[] d=Read(process,cave+Data,32);
        float result=BitConverter.ToSingle(d,0),m60=BitConverter.ToSingle(d,4),m64=BitConverter.ToSingle(d,8),m68=BitConverter.ToSingle(d,12);
        int r13=BitConverter.ToInt32(d,16),r15=BitConverter.ToInt32(d,20),ecx=BitConverter.ToInt32(d,24),edi=BitConverter.ToInt32(d,28);
        string line=$"{DateTime.Now:HH:mm:ss.fff}\t{label}\tresult={result.ToString("R",CultureInfo.InvariantCulture)}\tm60={m60.ToString("R",CultureInfo.InvariantCulture)}\tm64={m64.ToString("R",CultureInfo.InvariantCulture)}\tm68={m68.ToString("R",CultureInfo.InvariantCulture)}\tr13={r13}\tr15={r15}\tecx={ecx}\tedi={edi}";
        Console.WriteLine(line);File.AppendAllText(logPath,line+Environment.NewLine);
    }
    Console.WriteLine($"Logger installed at 0x{site:X16}; F5 HIP capture, F6 ADS capture, F12 restore+exit.");
    bool a=false,b=false,c=false;while(true){bool x=Down(VkF5),y=Down(VkF6),z=Down(VkF12);if(x&&!a)Capture("HIP");if(y&&!b)Capture("ADS");if(z&&!c)break;a=x;b=y;c=z;Thread.Sleep(20);}Restore();return 0;
}
finally{if(installed)try{Patch(process,site,original);}catch{}if(cave!=0)VirtualFreeEx(process,(nint)cave,0,MemRelease);CloseHandle(process);}

static void PutRel32(byte[] code,ref int p,ulong next,ulong target){BitConverter.GetBytes(checked((int)((long)target-(long)next))).CopyTo(code,p);p+=4;}
static int Rel(ulong instruction,int length,ulong target){long r=(long)target-((long)instruction+length);if(r<int.MinValue||r>int.MaxValue)throw new InvalidOperationException("rel32 range");return(int)r;}
static ulong AllocateNear(nint process,ulong site){const ulong g=0x10000,r=0x70000000;for(ulong d=g;d<r;d+=g)foreach(ulong h in new[]{(site+d)&~(g-1),(site-d)&~(g-1)}){nint a=VirtualAllocEx(process,(nint)h,4096,MemCommit|MemReserve,PageReadWrite);if(a!=0)return(ulong)a.ToInt64();}throw new Win32Exception(Marshal.GetLastWin32Error());}
static byte[] Read(nint p,ulong a,int n){byte[] b=new byte[n];if(!ReadProcessMemory(p,(nint)a,b,(nuint)n,out nuint r)||r!=(nuint)n)throw new Win32Exception(Marshal.GetLastWin32Error());return b;}
static void Write(nint p,ulong a,byte[] b){if(!WriteProcessMemory(p,(nint)a,b,(nuint)b.Length,out nuint w)||w!=(nuint)b.Length)throw new Win32Exception(Marshal.GetLastWin32Error());}
static void Patch(nint p,ulong a,byte[] b){if(!VirtualProtectEx(p,(nint)a,(nuint)b.Length,PageExecuteReadWrite,out uint old))throw new Win32Exception(Marshal.GetLastWin32Error());try{Write(p,a,b);FlushInstructionCache(p,(nint)a,(nuint)b.Length);}finally{VirtualProtectEx(p,(nint)a,(nuint)b.Length,old,out _);}}
static bool Down(int k)=>(GetAsyncKeyState(k)&0x8000)!=0;
[DllImport("kernel32.dll",SetLastError=true)]static extern nint OpenProcess(uint a,bool i,int p);
[DllImport("kernel32.dll",SetLastError=true)]static extern nint VirtualAllocEx(nint p,nint a,nuint s,uint t,uint prot);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool VirtualFreeEx(nint p,nint a,nuint s,uint t);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool VirtualProtectEx(nint p,nint a,nuint s,uint n,out uint o);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadProcessMemory(nint p,nint a,[Out]byte[]b,nuint s,out nuint r);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool WriteProcessMemory(nint p,nint a,byte[]b,nuint s,out nuint w);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool FlushInstructionCache(nint p,nint a,nuint s);
[DllImport("kernel32.dll")]static extern bool CloseHandle(nint h);
[DllImport("user32.dll")]static extern short GetAsyncKeyState(int k);
