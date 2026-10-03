using System.Diagnostics;
using System.Security.Cryptography;
namespace HarborAgent;

internal static class ClientInstallation
{
    internal static bool Exists(string root)=>File.Exists(Path.Combine(root,"Minecraft Harbor Client.exe"));
    internal static void Install(string source,string root)
    {
        string target=Path.Combine(root,"Minecraft Harbor Client.exe");
        foreach(var process in Process.GetProcessesByName("Minecraft Harbor Client"))using(process)
        {
            try{if(string.Equals(process.MainModule?.FileName,target,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Close Harbor Client, then click Update Client again. Your connection and selected modpack will be kept.");}
            catch(System.ComponentModel.Win32Exception){throw new InvalidOperationException("Close Harbor Client before updating it.");}
        }
        Directory.CreateDirectory(root);
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("The client installation folder cannot be a link.");
        string staged=Path.Combine(root,".harbor-client-update-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            File.Copy(source,staged);
            static string Hash(string file){using var input=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(input));}
            if(Hash(source)!=Hash(staged))throw new IOException("The client update failed verification. Try again.");
            // Replace just the application, leaving connection.json and the selected pack untouched.
            if(File.Exists(target))File.Replace(staged,target,null);else File.Move(staged,target);
        }
        catch(IOException ex){throw new IOException("Could not update Harbor Client. Close its window and retry. "+ex.Message,ex);}
        finally{if(File.Exists(staged))File.Delete(staged);}
    }
}
