using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Management;

namespace HarborUpdates;

internal static class UpdateShutdown
{
    internal const string CurseForgeExtension="cchhcaiapeikjbdbpfplgmpobbcdkdaphclbmkbj";
    internal static bool IsCurseForgeRenderer(string name,string commandLine)=>
        name.Equals("OverwolfBrowser.exe",StringComparison.OrdinalIgnoreCase)&&
        System.Text.RegularExpressions.Regex.IsMatch(commandLine,@"(?:^|\s)--uid=""?"+CurseForgeExtension+@"""?(?:\s|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    internal static Process[] CurseForgeProcesses()
    {
        var ids=new HashSet<int>();
        using var search=new ManagementObjectSearcher("SELECT Name,ProcessId,ParentProcessId,CommandLine FROM Win32_Process WHERE Name = 'CurseForge.exe' OR Name = 'OverwolfBrowser.exe' OR Name = 'Overwolf.exe'");
        using var rows=search.Get();
        var hosts=new HashSet<int>();var parents=new HashSet<int>();
        foreach(ManagementObject row in rows)using(row)
        {
            string name=row["Name"]?.ToString()??"";int id=Convert.ToInt32(row["ProcessId"]);
            if(name.Equals("Overwolf.exe",StringComparison.OrdinalIgnoreCase))hosts.Add(id);
            else if(name.Equals("CurseForge.exe",StringComparison.OrdinalIgnoreCase))ids.Add(id);
            else if(IsCurseForgeRenderer(name,row["CommandLine"]?.ToString()??"")){ids.Add(id);parents.Add(Convert.ToInt32(row["ParentProcessId"]));}
        }
        // The Overwolf host owns CurseForge's native profile cache. Closing only its window
        // leaves that cache alive, which overwrites minecraftinstance.json on the next launch.
        ids.UnionWith(parents.Where(hosts.Contains));
        var found=new List<Process>();
        try{foreach(int id in ids){try{var process=Process.GetProcessById(id);if(process.SessionId==Process.GetCurrentProcess().SessionId&&!process.HasExited)found.Add(process);else process.Dispose();}catch(ArgumentException){}}return found.ToArray();}
        catch{foreach(var process in found)process.Dispose();throw;}
    }
    delegate bool WindowCallback(IntPtr window,IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback,IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
    [DllImport("user32.dll",SetLastError=true)] static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);

    internal static Process[] Matching(string executable)
    {
        var matches=new List<Process>();
        foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            bool keep=false;
            try{keep=process.Id!=Environment.ProcessId&&process.SessionId==Process.GetCurrentProcess().SessionId&&!process.HasExited&&string.Equals(Path.GetFullPath(process.MainModule!.FileName!),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase);if(keep)matches.Add(process);}
            catch(InvalidOperationException){}
            catch(System.ComponentModel.Win32Exception){process.Dispose();throw new IOException("Windows could not access the running application.");}
            finally{if(!keep)process.Dispose();}
        }
        return matches.ToArray();
    }

    internal static void CloseApplication(string executable,TimeSpan timeout,Action<string>? status=null)
    {
        var timer=Stopwatch.StartNew();
        while(true)
        {
            var processes=Matching(executable);if(processes.Length==0)return;
            try
            {
                status?.Invoke("Closing "+Path.GetFileNameWithoutExtension(executable)+"…");
                foreach(var process in processes)RequestClose(process.Id);
                if(timer.Elapsed>timeout)throw new IOException("The application did not finish closing. Nothing was replaced.");
                Thread.Sleep(400);
            }
            finally{foreach(var process in processes)process.Dispose();}
        }
    }

    internal static void RequestClose(int processId)
    {
        // WM_CLOSE follows WinForms FormClosing, including Harbor's asynchronous world save.
        // Enumerating top-level windows also finds an app hidden in the system tray.
        EnumWindows((window,_)=>{
            GetWindowThreadProcessId(window,out uint owner);
            if(owner!=(uint)processId)return true;
            var title=new StringBuilder(512);GetWindowText(window,title,title.Capacity);
            if(title.Length>0)PostMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero);
            return true;
        },IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)] struct UniqueProcess {public uint Id;public System.Runtime.InteropServices.ComTypes.FILETIME Started;}
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] static extern int RmStartSession(out uint session,uint flags,StringBuilder key);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] static extern int RmRegisterResources(uint session,uint fileCount,string[]? files,uint processCount,UniqueProcess[] processes,uint serviceCount,string[]? services);
    [DllImport("rstrtmgr.dll")] static extern int RmShutdown(uint session,uint flags,IntPtr callback);
    [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint session);

    internal static void CloseCurseForge()
    {
        var processes=new List<Process>();
        try
        {
            foreach(var process in CurseForgeProcesses())
            {
                if(process.SessionId==Process.GetCurrentProcess().SessionId&&!process.HasExited)processes.Add(process);else process.Dispose();
            }
            if(processes.Count==0)return;
            var registered=new List<UniqueProcess>();
            foreach(var process in processes)
            {
                try{long time=process.StartTime.ToUniversalTime().ToFileTimeUtc();registered.Add(new(){Id=(uint)process.Id,Started=new(){dwLowDateTime=(int)time,dwHighDateTime=(int)(time>>32)}});}
                catch(InvalidOperationException){}
            }
            if(registered.Count==0)return;
            int error=RmStartSession(out uint session,0,new StringBuilder(33));
            if(error!=0)throw new IOException("Windows could not prepare CurseForge for the update ("+error+").");
            try
            {
                error=RmRegisterResources(session,0,null,(uint)registered.Count,registered.ToArray(),0,null);
                if(error!=0)throw new IOException("Windows could not prepare CurseForge for the update ("+error+").");
                // Register this session's CurseForge processes and its Overwolf cache host,
                // never Minecraft, Java, or Harbor. Do not terminate a process tree.
                // Electron can refuse Restart Manager's normal shutdown (ERROR_FAIL_SHUTDOWN, 351).
                // Ask it to close first, then let Windows finish closing those registered processes.
                foreach(var process in processes)if(!process.HasExited)RequestClose(process.Id);
                var graceful=Stopwatch.StartNew();
                while(processes.Any(p=>!p.HasExited)&&graceful.Elapsed<TimeSpan.FromSeconds(3))Thread.Sleep(100);
                // Normal Restart Manager shutdown can wait indefinitely for a tray-hosted
                // Overwolf app. Finish closing only the registered cache-owning processes.
                if(processes.Any(p=>!p.HasExited))error=RmShutdown(session,1,IntPtr.Zero);
                foreach(var process in processes)if(!process.HasExited&&!process.WaitForExit(10000))throw new IOException("CurseForge could not finish closing. Update cancelled ("+error+").");
            }
            finally{RmEndSession(session);}
        }
        finally{foreach(var process in processes)process.Dispose();}
    }
}
