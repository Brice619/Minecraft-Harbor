using System.Text.Json;
using System.Text.RegularExpressions;

namespace MinecraftHarbor;

public sealed class WorldEntry
{
    public string Folder { get; set; } = "world";
    public string Name { get; set; } = "New world";
    public bool CanGenerate { get; set; }
    public override string ToString()=>Name;
}
public sealed class ServerProfile
{
    public string ServerName { get; set; } = "";
    public bool Deleted { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(ServerName) ? Name : ServerName;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Modpack";
    public string PackVersion { get; set; } = "";
    public string MinecraftVersion { get; set; } = "1.21.1";
    public string Loader { get; set; } = "neoforge";
    public string LoaderVersion { get; set; } = "21.1.249";
    public string JavaPath { get; set; } = "";
    public string LaunchFile { get; set; } = "";
    public bool UsesArgumentFile { get; set; } = true;
    public bool LegacyLocation { get; set; }
    public bool CustomUltimineSync { get; set; }
    public string CurseForgePath { get; set; } = "";
    public string ClientSyncPath { get; set; } = "";
    public long ProjectId { get; set; }
    public long ServerFileId { get; set; }
    public List<WorldEntry> Worlds { get; set; } = new();
    public override string ToString()=>Name+(PackVersion.Length>0?" · "+PackVersion:"");
}
public sealed class LibraryData
{
    public string ActiveId { get; set; } = "original-atm10";
    public List<ServerProfile> Profiles { get; set; } = new();
    public List<string> CurseForgeRoots { get; set; } = new();
}
public sealed class ServerLibrary
{
    readonly string root;
    readonly object sync=new();
    public LibraryData Data { get; }
    public ServerProfile Active=>Data.Profiles.Single(p=>p.Id==Data.ActiveId);
    public ServerLibrary(string root)
    {
        this.root=root;var file=Path.Combine(root,"library.json");
        if(File.Exists(file))Data=JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(file))??throw new InvalidDataException("The server library could not be read.");
        else {
            var cfg=File.Exists(Path.Combine(root,"settings.json"))?JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(root,"settings.json")))??new():new Settings();
            Data=new();Data.Profiles.Add(new(){Id="original-atm10",Name="All the Mods 10",PackVersion=cfg.PackVersion,LoaderVersion=cfg.NeoForgeVersion,LegacyLocation=true,CustomUltimineSync=true,
                JavaPath=Path.Combine(root,"runtime","bin","java.exe"),LaunchFile="libraries/net/neoforged/neoforge/"+cfg.NeoForgeVersion+"/win_args.txt",
                CurseForgePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"curseforge","minecraft","Instances","All the Mods 10 - ATM10"),ProjectId=925200,ServerFileId=8764245,
                Worlds=new(){new(){Folder="world",Name=cfg.WorldName}}});
            Save();
        }
        if(Data.Profiles.Count==0||Data.Profiles.Select(p=>p.Id).Distinct().Count()!=Data.Profiles.Count||Data.Profiles.Count(p=>p.LegacyLocation)>1)throw new InvalidDataException("Invalid server library.");
        foreach(var p in Data.Profiles){if(!Regex.IsMatch(p.Id,@"^[a-zA-Z0-9-]{1,64}$"))throw new InvalidDataException("Invalid pack identifier.");foreach(var w in p.Worlds)ValidateWorldFolder(w.Folder);}
        _=Active;
    }
    public string ProfileRoot(ServerProfile profile)=>profile.LegacyLocation?root:Path.Combine(root,"profiles",profile.Id);
    public void Save(){lock(sync)ServerManager.WriteJson(Path.Combine(root,"library.json"),Data);}
    public static void ValidateWorldFolder(string folder){if(!Regex.IsMatch(folder,@"^world(?:-[a-f0-9]{32})?$"))throw new InvalidDataException("Invalid world folder.");}
}

public sealed record CurseForgeProfile(string Path,string Name,string PackVersion,string MinecraftVersion,string Loader,string LoaderVersion,long ProjectId,long ServerFileId)
{
    public string Logo {get;init;}="";
    public string Website {get;init;}="";
    public override string ToString()=>Name+" · "+PackVersion+" · "+MinecraftVersion;
}
public static class CurseForgeProfiles
{
    static string Text(JsonElement e,params string[] keys){foreach(var k in keys)if(e.ValueKind!=JsonValueKind.Object||!e.TryGetProperty(k,out e))return "";return e.ValueKind==JsonValueKind.String?e.GetString()!:e.ToString();}
    public static CurseForgeProfile Read(string folder)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(folder,"minecraftinstance.json")));var j=json.RootElement;
        var loader=Text(j,"baseModLoader","name");var dash=loader.IndexOf('-');if(dash<1)throw new InvalidDataException("This CurseForge profile does not identify its mod loader.");
        var name=Text(j,"name");var mc=Text(j,"gameVersion");
        var file=Text(j,"installedModpack","installedFile","fileName");string version=System.IO.Path.GetFileNameWithoutExtension(file);
        var manifest=System.IO.Path.Combine(folder,"manifest.json");
        if(File.Exists(manifest)){using var m=JsonDocument.Parse(File.ReadAllText(manifest));version=Text(m.RootElement,"version");}
        if(version.Length==0)version="Custom profile";
        long.TryParse(Text(j,"installedModpack","addonID"),out var project);long.TryParse(Text(j,"installedModpack","installedFile","serverPackFileId"),out var server);
        if(name.Length==0||mc.Length==0)throw new InvalidDataException("This is not a complete CurseForge profile.");
        return new(System.IO.Path.GetFullPath(folder),name,version,mc,loader[..dash].ToLowerInvariant(),loader[(dash+1)..],project,server){Logo=Text(j,"installedModpack","thumbnailUrl"),Website=Text(j,"installedModpack","webSiteURL")};
    }
    public static List<CurseForgeProfile> Discover(IEnumerable<string>? extraRoots=null)
    {
        var user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots=new[]{System.IO.Path.Combine(user,"curseforge","minecraft","Instances"),System.IO.Path.Combine(user,"Twitch","Minecraft","Instances"),System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Curse","Minecraft","Instances")}.Concat(extraRoots??Array.Empty<string>());
        var found=new List<CurseForgeProfile>();
        foreach(var root in roots.Distinct(StringComparer.OrdinalIgnoreCase)){
            if(!Directory.Exists(root))continue;
            try{foreach(var folder in Directory.EnumerateDirectories(root))try{if(File.Exists(System.IO.Path.Combine(folder,"minecraftinstance.json")))found.Add(Read(folder));}catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException){} }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return found.DistinctBy(p=>p.Path,StringComparer.OrdinalIgnoreCase).ToList();
    }
}
