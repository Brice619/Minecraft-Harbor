using System.Text.Json;
namespace MinecraftHarbor;

public sealed record PreparedPackSource(string Directory,ServerProfile Profile);
internal static class InstalledPackSelection
{
    internal static bool Matches(ServerProfile profile,CurseForgeProfile pack)=>
        !profile.Deleted&&profile.Loader.Equals(pack.Loader,StringComparison.OrdinalIgnoreCase)&&
        profile.LoaderVersion==pack.LoaderVersion&&profile.MinecraftVersion==pack.MinecraftVersion&&
        (pack.ProjectId>0 ? profile.ProjectId==pack.ProjectId : pack.Path.Length>0&&profile.CurseForgePath.Equals(pack.Path,StringComparison.OrdinalIgnoreCase))&&
        (pack.ServerFileId>0 ? profile.ServerFileId==pack.ServerFileId : profile.PackVersion==pack.PackVersion);

    internal static bool Ready(PreparedPackSource source)
    {
        if(!System.IO.Directory.Exists(source.Directory)||string.IsNullOrEmpty(source.Profile.LaunchFile))return false;
        string prefix=Path.GetFullPath(source.Directory)+Path.DirectorySeparatorChar;
        string launcher=Path.GetFullPath(Path.Combine(source.Directory,source.Profile.LaunchFile));
        return launcher.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&File.Exists(launcher)&&
            System.IO.Directory.Exists(Path.Combine(source.Directory,"mods"))&&
            System.IO.Directory.EnumerateFiles(Path.Combine(source.Directory,"mods"),"*.jar").Any();
    }

    internal static PreparedPackSource? FindPrepared(string root,ServerLibrary library,CurseForgeProfile pack)
    {
        string cache=Path.Combine(root,"pack-cache");
        if(System.IO.Directory.Exists(cache))foreach(string folder in System.IO.Directory.EnumerateDirectories(cache).OrderBy(p=>p,StringComparer.Ordinal)){
            // Incomplete cache writes are never usable templates.
            if(Path.GetFileName(folder).Contains(".building-"))continue;
            string receipt=Path.Combine(folder,"template.json");if(!File.Exists(receipt))continue;
            try{var profile=JsonSerializer.Deserialize<ServerProfile>(File.ReadAllText(receipt));if(profile!=null&&Matches(profile,pack)){var found=new PreparedPackSource(Path.Combine(folder,"server"),profile);if(Ready(found))return found;}}
            catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){}
        }
        foreach(var profile in library.Data.Profiles.Where(p=>Matches(p,pack))){
            var found=new PreparedPackSource(Path.Combine(library.ProfileRoot(profile),"server"),profile);
            if(Ready(found))return found;
        }
        return null;
    }

    internal static string? FindArchive(string root,CurseForgeProfile pack)
    {
        if(pack.ProjectId<=0||pack.ServerFileId<=0)return null;
        string cache=Path.Combine(root,"downloads"),release=Path.Combine(cache,"curseforge",pack.ProjectId+"-"+pack.ServerFileId);
        if(System.IO.Directory.Exists(release)){
            var archive=System.IO.Directory.EnumerateFiles(release,"*.zip").OrderBy(p=>p,StringComparer.Ordinal).FirstOrDefault();
            if(archive!=null)return archive;
        }
        return System.IO.Directory.Exists(cache)?System.IO.Directory.EnumerateFiles(cache,pack.ServerFileId+"-*.zip",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal).FirstOrDefault():null;
    }

    internal static void CopyPack(PreparedPackSource source,string target,CancellationToken token)
    {
        if(!Ready(source))throw new InvalidDataException("The prepared server files are no longer available. Select the pack again.");
        if((File.GetAttributes(source.Directory)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked pack folders are not supported.");
        System.IO.Directory.CreateDirectory(target);
        // Carry pack content and loader dependencies, never a running server's worlds or player records.
        foreach(string name in new[]{"mods","config","defaultconfigs","kubejs","scripts","libraries","resources","global_packs"}){
            string folder=Path.Combine(source.Directory,name);if(!System.IO.Directory.Exists(folder))continue;
            if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked pack folders are not supported.");
            PackInstaller.CopyTree(folder,Path.Combine(target,name),token);
        }
        foreach(string file in System.IO.Directory.EnumerateFiles(source.Directory)){
            token.ThrowIfCancellationRequested();string name=Path.GetFileName(file);
            if(!name.EndsWith(".jar",StringComparison.OrdinalIgnoreCase)&&name is not ("user_jvm_args.txt" or "unix_args.txt" or "win_args.txt"))continue;
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked pack files are not supported.");
            File.Copy(file,Path.Combine(target,name));
        }
        if(!File.Exists(Path.Combine(target,source.Profile.LaunchFile)))throw new InvalidDataException("The prepared pack's launcher could not be copied. Import its original server ZIP.");
    }
}
