using System.Diagnostics;
using System.Security.Cryptography;
namespace HarborAgent;

internal static class ClientInstallation
{
    internal static bool Exists(string root)=>File.Exists(Path.Combine(root,"Minecraft Harbor Client.exe"));
    internal static void Install(string source,string root)
    {
        string target=Path.Combine(root,"Minecraft Harbor Client.exe");
        HarborUpdates.UpdateShutdown.CloseApplication(target,TimeSpan.FromMinutes(5));
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
        catch(IOException ex){throw new IOException("Client update failed: "+ex.Message,ex);}
        finally{if(File.Exists(staged))File.Delete(staged);}
    }
}

