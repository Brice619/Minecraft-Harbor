using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MinecraftHarbor;

// Only the supervisor holds this handle. If it is also forcibly ended, Windows
// terminates its Minecraft process rather than leaving another detached server.
internal sealed class ServerLifetimeJob : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits
    {
        internal long ProcessTime,JobTime;
        internal uint Flags;
        internal UIntPtr MinimumWorkingSet,MaximumWorkingSet;
        internal uint ActiveProcesses;
        internal UIntPtr Affinity;
        internal uint Priority,Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters
    {
        internal ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits
    {
        internal BasicLimits Basic;
        internal IoCounters Io;
        internal UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern SafeFileHandle CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool SetInformationJobObject(SafeFileHandle job,int informationClass,ref ExtendedLimits limits,uint size);
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
    readonly SafeFileHandle job;
    internal ServerLifetimeJob()
    {
        job=CreateJobObject(IntPtr.Zero,null);
        if(job.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits=new ExtendedLimits{Basic=new BasicLimits{Flags=0x2000}};
        if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimits>())){
            int error=Marshal.GetLastWin32Error();job.Dispose();throw new Win32Exception(error);
        }
    }
    internal void Own(Process process)
    {
        if(!AssignProcessToJobObject(job,process.Handle))throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose()=>job.Dispose();
}
