namespace MinecraftHarbor;
internal sealed record PackWorld(string Path,string Name,string Origin,string MinecraftVersion,string ProfileId="",string Folder="")
{
    public override string ToString()=>Name+" · "+Origin;
}
internal static class PackWorlds
{
    internal static CurseForgeProfile Pack(ServerProfile profile)=>new(profile.CurseForgePath,profile.Name,profile.PackVersion,profile.MinecraftVersion,profile.Loader,profile.LoaderVersion,profile.ProjectId,profile.ServerFileId);
    internal static bool Matches(CurseForgeProfile source,CurseForgeProfile target)=>
        source.MinecraftVersion==target.MinecraftVersion&&source.Loader==target.Loader&&source.LoaderVersion==target.LoaderVersion&&source.PackVersion==target.PackVersion&&
        (source.ProjectId>0&&target.ProjectId>0 ? source.ProjectId==target.ProjectId : source.Path.Length>0&&target.Path.Length>0&&System.IO.Path.GetFullPath(source.Path).Equals(System.IO.Path.GetFullPath(target.Path),StringComparison.OrdinalIgnoreCase));
    internal static PackWorld Qualify(string folder,CurseForgeProfile pack,ServerLibrary library)
    {
        folder=System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder));
        if(!File.Exists(System.IO.Path.Combine(folder,"level.dat")))throw new InvalidDataException("Choose a saved Minecraft world folder containing level.dat.");
        if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Choose a regular world folder, not a linked folder.");
        var info=WorldImport.Read(folder);
        if(info.MinecraftVersion.Length>0&&info.MinecraftVersion!=pack.MinecraftVersion)throw new InvalidDataException("This world uses Minecraft "+info.MinecraftVersion+"; this pack uses "+pack.MinecraftVersion+".");
        foreach(var profile in library.Data.Profiles.Where(p=>!p.Deleted))foreach(var world in profile.Worlds){
            string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(library.ProfileRoot(profile),"server",world.Folder));
            if(!path.Equals(folder,StringComparison.OrdinalIgnoreCase))continue;
            if(!Matches(Pack(profile),pack))throw new InvalidDataException("This world belongs to a different modpack or release.");
            return new(folder,world.Name,profile.DisplayName,info.MinecraftVersion,profile.Id,world.Folder);
        }
        var parent=new DirectoryInfo(folder).Parent;
        while(parent!=null){
            if(File.Exists(System.IO.Path.Combine(parent.FullName,"minecraftinstance.json"))){
                var installed=CurseForgeProfiles.Read(parent.FullName);
                if(!Matches(installed,pack))throw new InvalidDataException("This world belongs to a different modpack, release, or loader. Select its matching pack.");
                string saves=System.IO.Path.GetFullPath(System.IO.Path.Combine(installed.Path,"saves"))+System.IO.Path.DirectorySeparatorChar;
                if(!folder.StartsWith(saves,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Choose a world inside this modpack's saves folder.");
                return new(folder,info.Name,installed.Name+" · "+installed.PackVersion,info.MinecraftVersion);
            }
            parent=parent.Parent;
        }
        throw new InvalidDataException("Harbor could not identify this world's modpack. Choose it from the matching pack's saves folder or an existing Harbor server.");
    }
    internal static List<PackWorld> Discover(CurseForgeProfile pack,ServerLibrary library,CancellationToken token)
    {
        var found=new List<PackWorld>();
        void Add(string folder){token.ThrowIfCancellationRequested();try{found.Add(Qualify(folder,pack,library));}catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException){} }
        var roots=library.Data.CurseForgeRoots.Concat(pack.Path.Length>0?[System.IO.Path.GetDirectoryName(pack.Path)!]:Array.Empty<string>());
        foreach(var installed in CurseForgeProfiles.Discover(roots).Where(p=>Matches(p,pack))){
            token.ThrowIfCancellationRequested();string saves=System.IO.Path.Combine(installed.Path,"saves");if(!Directory.Exists(saves))continue;
            try{foreach(string folder in Directory.EnumerateDirectories(saves))Add(folder);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        foreach(var profile in library.Data.Profiles.Where(p=>!p.Deleted&&Matches(Pack(p),pack)))foreach(var world in profile.Worlds)Add(System.IO.Path.Combine(library.ProfileRoot(profile),"server",world.Folder));
        return found.DistinctBy(w=>w.Path,StringComparer.OrdinalIgnoreCase).OrderBy(w=>w.Name,StringComparer.OrdinalIgnoreCase).ToList();
    }
    internal static Dictionary<string,string> Rules(string folder)
    {
        string modern=System.IO.Path.Combine(folder,"data","minecraft","game_rules.dat");
        return new WorldMetadata(File.Exists(modern)?modern:System.IO.Path.Combine(folder,"level.dat")).Rules();
    }
}
