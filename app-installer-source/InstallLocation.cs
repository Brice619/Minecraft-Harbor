using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace HarborSetup;

internal static class InstallLocation
{
    internal static string Default=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Minecraft Harbor");
    internal static string Find()
    {
        // Keep updates on the copy the user runs, including earlier portable installations.
        foreach(var process in Process.GetProcessesByName("Minecraft Harbor"))using(process)
        {
            try{if(process.SessionId==Process.GetCurrentProcess().SessionId&&Existing(process.MainModule?.FileName) is string running)return running;}
            catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
        }
        using(var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MinecraftHarbor"))
            if(key?.GetValue("InstallLocation") is string installed&&Existing(Path.Combine(installed,"Minecraft Harbor.exe")) is string registered)return registered;
        foreach(var path in new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Minecraft Harbor.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),"Programs","Minecraft Harbor.lnk")})
        {
            if(!File.Exists(path))continue;
            try
            {
                var type=Type.GetTypeFromProgID("WScript.Shell");if(type==null)continue;
                dynamic shell=Activator.CreateInstance(type)!;
                try{dynamic shortcut=shell.CreateShortcut(path);try{if(Existing((string)shortcut.TargetPath) is string existing)return existing;}finally{Marshal.FinalReleaseComObject(shortcut);}}
                finally{Marshal.FinalReleaseComObject(shell);}
            }
            catch(COMException){}
        }
        return Default;
    }
    static string? Existing(string? executable)=>!string.IsNullOrEmpty(executable)&&Path.GetFileName(executable).Equals("Minecraft Harbor.exe",StringComparison.OrdinalIgnoreCase)&&File.Exists(executable)?Path.GetDirectoryName(Path.GetFullPath(executable)):null;
}
