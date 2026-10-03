using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace HarborAgent;

internal static class PackUpdateStorage
{
    internal static string Root(string folder)=>Path.Combine(ClientConfig.Root,"pack-updates",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()))));
    internal static string NewStage(string folder){string path=Path.Combine(Root(folder),"staging",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
    internal static void ValidateStage(string folder,string stage)
    {
        string parent=Path.GetFullPath(Path.Combine(Root(folder),"staging"))+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(stage).StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid pending update location.");
    }
    internal static bool IsRollbackFolder(string folder)
    {
        for(var directory=new DirectoryInfo(Path.GetFullPath(folder));directory!=null;directory=directory.Parent)
            if(directory.Name.Equals("harbor-sync-backups",StringComparison.OrdinalIgnoreCase)||directory.FullName.Equals(Path.Combine(ClientConfig.Root,"pack-updates"),StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }
    internal static void RetireLegacyMetadata(string folder,Action launcherCheck)
    {
        string root=Path.Combine(folder,"harbor-sync-backups");if(!Directory.Exists(root))return;
        var files=Directory.EnumerateDirectories(root).Where(d=>Regex.IsMatch(Path.GetFileName(d),@"^\d{8}-\d{6}-\d{3}-[a-fA-F0-9]{8}$"))
            .Where(d=>(File.GetAttributes(d)&FileAttributes.ReparsePoint)==0)
            .Select(d=>Path.Combine(d,"minecraftinstance.json")).Where(File.Exists).ToArray();
        if(files.Length==0)return;launcherCheck();
        // Keep old data, but prevent Harbor's old rollback metadata from looking like playable instances.
        foreach(string file in files)File.Move(file,Path.Combine(Path.GetDirectoryName(file)!,"retired-profile-"+Guid.NewGuid().ToString("N")+".json"));
    }
    internal static void CleanStage(string stage){if(Directory.Exists(stage))Directory.Delete(stage,true);}
}
