using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

if (args.Length != 4 || args[0] != "--pid" || args[2] != "--execute" || !int.TryParse(args[1], out int pid))
{
    Console.Error.WriteLine("Usage: GRWStackFollow --pid 1234 --execute 0xABCDEF"); return 2;
}
string text = args[3].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[3][2..] : args[3];
if (!ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong writer)) return 2;

const uint ExceptionDebugEvent=1, CreateThreadDebugEvent=2, CreateProcessDebugEvent=3, ExitProcessDebugEvent=5;
const uint ExceptionSingleStep=0x80000004, DbgContinue=0x00010002, DbgExceptionNotHandled=0x80010001;
const uint ThreadGetContext=0x0008, ThreadSetContext=0x0010, ThreadQueryInformation=0x0040;
const uint ProcessVmRead=0x0010, ProcessQueryInformation=0x0400;
const uint ContextDebug=0x00100010, ContextDebugControlInteger=0x00100013;
const int ContextSize=1232, FlagsOffset=48, Dr0Offset=72, Dr6Offset=104, Dr7Offset=112, RipOffset=248, RdiOffset=176;

using Process target=Process.GetProcessById(pid);
nint read=OpenProcess(ProcessVmRead|ProcessQueryInformation,false,pid);
if(read==0) throw new Win32Exception(Marshal.GetLastWin32Error());
nint evt=Marshal.AllocHGlobal(176);
bool attached=false, following=false;
ulong stackAddress=0;
int stackHits=0;
try
{
    if(!DebugActiveProcess(pid)) throw new Win32Exception(Marshal.GetLastWin32Error());
    attached=true;
    if(!DebugSetProcessKillOnExit(false)) throw new Win32Exception(Marshal.GetLastWin32Error());
    Console.WriteLine($"Attached to PID {pid}; waiting for writer 0x{writer:X16}.");
    Stopwatch timer=Stopwatch.StartNew();
    while(timer.Elapsed<TimeSpan.FromSeconds(120))
    {
        if(!WaitForDebugEvent(evt,500)){int error=Marshal.GetLastWin32Error();if(error==121)continue;throw new Win32Exception(error);}
        uint code=(uint)Marshal.ReadInt32(evt,0);int ep=Marshal.ReadInt32(evt,4),tid=Marshal.ReadInt32(evt,8);uint status=DbgContinue;
        bool finished=false;ulong consumerRip=0;
        if(code is CreateProcessDebugEvent or CreateThreadDebugEvent)
        {
            SetBreak(tid,following?stackAddress:writer,following);
            if(code==CreateProcessDebugEvent) SetAll(target,following?stackAddress:writer,following);
        }
        else if(code==ExceptionDebugEvent)
        {
            uint exception=(uint)Marshal.ReadInt32(evt,16);
            if(exception==ExceptionSingleStep && State(tid,out ulong dr6,out ulong rip,out ulong rdi) && (dr6&1)!=0)
            {
                if(!following)
                {
                    stackAddress=rdi;following=true;stackHits=0;
                    SetAll(target,stackAddress,true);
                    Console.WriteLine($"Writer hit on thread {tid}; following RDI stack field 0x{stackAddress:X16}.");
                }
                else
                {
                    stackHits++;
                    if(stackHits==1)
                    {
                        Console.WriteLine($"Ignored the stack write itself at next RIP 0x{rip:X16}.");
                        SetAll(target,stackAddress,true);
                    }
                    else
                    {
                        consumerRip=rip;SetAll(target,stackAddress,false);finished=true;
                    }
                }
            }
            else if(exception!=0x80000003) status=DbgExceptionNotHandled;
        }
        else if(code==ExitProcessDebugEvent)return 4;
        if(!ContinueDebugEvent(ep,tid,status))throw new Win32Exception(Marshal.GetLastWin32Error());
        if(finished)
        {
            byte[] bytes=new byte[64];ReadProcessMemory(read,(nint)(consumerRip-24),bytes,(nuint)bytes.Length,out _);
            Console.WriteLine($"STACK CONSUMER: next RIP 0x{consumerRip:X16}, thread {tid}.");
            Console.WriteLine("Bytes around next RIP (-24..+39): "+Convert.ToHexString(bytes));
            if(DebugActiveProcessStop(pid))attached=false;
            Console.WriteLine("Breakpoints cleared and debugger detached.");return 0;
        }
    }
    SetAll(target,following?stackAddress:writer,following,false);if(DebugActiveProcessStop(pid))attached=false;
    Console.WriteLine("Timed out; detached.");return 5;
}
finally
{
    if(attached){try{SetAll(target,following?stackAddress:writer,following,false);}catch{}DebugActiveProcessStop(pid);}
    Marshal.FreeHGlobal(evt);CloseHandle(read);
}

void SetAll(Process p,ulong address,bool access,bool enabled=true){p.Refresh();foreach(ProcessThread t in p.Threads)try{SetBreak(t.Id,address,access,enabled);}catch(Win32Exception){}}
void SetBreak(int tid,ulong address,bool access,bool enabled=true)
{
    nint thread=OpenThread(ThreadGetContext|ThreadSetContext|ThreadQueryInformation,false,tid);if(thread==0)throw new Win32Exception(Marshal.GetLastWin32Error());
    try{Context(ctx=>{Marshal.WriteInt32(ctx,FlagsOffset,(int)ContextDebugControlInteger);if(!GetThreadContext(thread,ctx))throw new Win32Exception(Marshal.GetLastWin32Error());ulong dr7=(ulong)Marshal.ReadInt64(ctx,Dr7Offset);dr7&=~(3UL|(0xFUL<<16));Marshal.WriteInt64(ctx,Dr0Offset,enabled?(long)address:0);Marshal.WriteInt64(ctx,Dr6Offset,0);if(enabled){dr7|=1;if(access)dr7|=(3UL<<16)|(3UL<<18);}Marshal.WriteInt64(ctx,Dr7Offset,(long)dr7);Marshal.WriteInt32(ctx,FlagsOffset,(int)ContextDebug);if(!SetThreadContext(thread,ctx))throw new Win32Exception(Marshal.GetLastWin32Error());});}
    finally{CloseHandle(thread);}
}
bool State(int tid,out ulong dr6,out ulong rip,out ulong rdi)
{
    ulong a=0,b=0,c=0;bool ok=false;nint thread=OpenThread(ThreadGetContext|ThreadQueryInformation,false,tid);
    if(thread!=0){try{Context(ctx=>{Marshal.WriteInt32(ctx,FlagsOffset,(int)ContextDebugControlInteger);ok=GetThreadContext(thread,ctx);if(ok){a=(ulong)Marshal.ReadInt64(ctx,Dr6Offset);b=(ulong)Marshal.ReadInt64(ctx,RipOffset);c=(ulong)Marshal.ReadInt64(ctx,RdiOffset);}});}finally{CloseHandle(thread);}}
    dr6=a;rip=b;rdi=c;return ok;
}
void Context(Action<nint> action){nint raw=Marshal.AllocHGlobal(ContextSize+16);try{nint aligned=(nint)((raw.ToInt64()+15)&~15L);for(int i=0;i<ContextSize;i+=8)Marshal.WriteInt64(aligned,i,0);action(aligned);}finally{Marshal.FreeHGlobal(raw);}}

[DllImport("kernel32.dll",SetLastError=true)]static extern bool DebugSetProcessKillOnExit(bool v);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool DebugActiveProcess(int p);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool DebugActiveProcessStop(int p);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool WaitForDebugEvent(nint e,uint ms);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool ContinueDebugEvent(int p,int t,uint s);
[DllImport("kernel32.dll",SetLastError=true)]static extern nint OpenThread(uint a,bool i,int t);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool GetThreadContext(nint t,nint c);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool SetThreadContext(nint t,nint c);
[DllImport("kernel32.dll",SetLastError=true)]static extern nint OpenProcess(uint a,bool i,int p);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadProcessMemory(nint p,nint a,[Out]byte[] b,nuint s,out nuint r);
[DllImport("kernel32.dll")]static extern bool CloseHandle(nint h);
