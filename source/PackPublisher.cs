using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace MinecraftHarbor;
public sealed class PackPublisher
{
    readonly object gate=new();readonly ServerManager server;
    readonly Dictionary<string,(long Ticks,long Size,string Hash)> hashes=new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string,string> published=new(StringComparer.OrdinalIgnoreCase);
    PackSyncManifest? cached;string signature="";
    public PackPublisher(ServerManager server){this.server=server;}
    public string Source=>server.Profile.ClientSyncPath.Length>0?server.Profile.ClientSyncPath:server.Profile.CurseForgePath;
    public bool Available=>Source.Length>0&&!server.Profile.Deleted&&server.Profile.Loader!="vanilla"&&Directory.Exists(Path.Combine(Source,"mods"));
    public string CachedHash=>cached?.Hash??"";
    public PackSyncManifest Manifest()
    {
        lock(gate){
            if(!Available)throw new InvalidOperationException("Choose a client modpack folder in LAN PCs before syncing this server.");
            string packName=server.Profile.Name;long projectId=server.Profile.ProjectId;string metadata=Path.Combine(Source,"minecraftinstance.json");if(File.Exists(metadata)){var sourceProfile=CurseForgeProfiles.Read(Source);if(sourceProfile.MinecraftVersion!=server.Profile.MinecraftVersion||sourceProfile.Loader!=server.Profile.Loader||projectId>0&&sourceProfile.ProjectId!=projectId)throw new InvalidDataException("The shared client pack must match this server's Minecraft version, loader, and modpack.");using var json=JsonDocument.Parse(File.ReadAllText(metadata));if(json.RootElement.TryGetProperty("installedModpack",out var pack)&&pack.ValueKind==JsonValueKind.Object&&pack.TryGetProperty("name",out var name))packName=name.GetString()??packName;else packName=sourceProfile.Name;if(projectId==0)projectId=sourceProfile.ProjectId;}
            // Enumeration already checks paths and links. Avoid repeating every
            // ancestor check for each asset in large extracted shader packs.
            var files=PackSyncPaths.Enumerate(Source).ToDictionary(p=>p,p=>Path.Combine(Source,p.Replace('/',Path.DirectorySeparatorChar)),StringComparer.OrdinalIgnoreCase);
            var overlay=PackSyncPaths.Enumerate(server.ServerDir).ToDictionary(p=>p,p=>Path.Combine(server.ServerDir,p.Replace('/',Path.DirectorySeparatorChar)),StringComparer.OrdinalIgnoreCase);
            string stamp=server.Profile.Id+"|"+Source+"|"+string.Join('|',files.Concat(overlay).OrderBy(p=>p.Key).Select(p=>p.Value+":"+new FileInfo(p.Value).Length+":"+File.GetLastWriteTimeUtc(p.Value).Ticks));
            if(stamp==signature&&cached!=null)return cached;
            string baseline=Path.Combine(server.ProfileRoot,"client-sync-server-files.json");var old=File.Exists(baseline)?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(baseline))??new():new();
            var current=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var serverModIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var pair in overlay){string identity=pair.Key.StartsWith("mods/")?ModId(pair.Value):"";current[pair.Key]=identity;if(identity.Length>0)serverModIds.Add(identity);}
            var removedIds=old.Where(p=>!current.ContainsKey(p.Key)&&p.Value.Length>0&&!serverModIds.Contains(p.Value)).Select(p=>p.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var pair in files.ToArray()){
                string identity=pair.Key.StartsWith("mods/")?ModId(pair.Value):"";
                if(identity.Length>0&&(serverModIds.Contains(identity)||removedIds.Contains(identity))||old.ContainsKey(pair.Key)&&!current.ContainsKey(pair.Key))files.Remove(pair.Key);
            }
            foreach(var pair in overlay)files[pair.Key]=pair.Value;
            var entries=files.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>{var f=new FileInfo(p.Value);if(f.Length>512L*1024*1024)throw new InvalidDataException("A pack file exceeds the 512 MB limit: "+p.Key);if(!hashes.TryGetValue(p.Value,out var hash)||hash.Ticks!=f.LastWriteTimeUtc.Ticks||hash.Size!=f.Length){hash=(f.LastWriteTimeUtc.Ticks,f.Length,PackSyncPaths.HashFile(p.Value));hashes[p.Value]=hash;}return new PackSyncFile(p.Key,hash.Hash,f.Length);}).ToArray();
            if(entries.Length>100000||entries.Sum(f=>f.Size)>16L*1024*1024*1024)throw new InvalidDataException("The client pack exceeds the supported size.");
            var profile=server.Profile;cached=new(profile.Id,projectId,packName,profile.MinecraftVersion,profile.Loader,profile.LoaderVersion,PackSyncPaths.HashList(entries),entries);published=files;signature=stamp;foreach(var pair in current)old[pair.Key]=pair.Value;ServerManager.WriteJson(baseline,old);return cached;
        }
    }
    static string ModId(string path){if(!path.EndsWith(".jar",StringComparison.OrdinalIgnoreCase))return "";try{using var zip=ZipFile.OpenRead(path);var entry=zip.GetEntry("META-INF/neoforge.mods.toml")??zip.GetEntry("META-INF/mods.toml");if(entry!=null&&entry.Length<1024*1024){using var reader=new StreamReader(entry.Open());var match=Regex.Match(reader.ReadToEnd(),"(?m)^\\s*modId\\s*=\\s*\"([^\"]+)\"");if(match.Success)return match.Groups[1].Value;}entry=zip.GetEntry("fabric.mod.json");if(entry!=null&&entry.Length<1024*1024){using var json=JsonDocument.Parse(entry.Open());return json.RootElement.GetProperty("id").GetString()??"";}}catch(Exception ex)when(ex is IOException or InvalidDataException or JsonException){}return "";}
    public string FileFor(string expected,string path){lock(gate){var manifest=cached??Manifest();if(expected!=manifest.Hash)throw new InvalidOperationException("The pack changed during preparation. Check for updates again.");var entry=manifest.Files.SingleOrDefault(f=>f.Path==path)??throw new InvalidDataException("File is not in the published pack.");string source=published[path];string contentRoot=source.StartsWith(Path.GetFullPath(server.ServerDir)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)?server.ServerDir:Source;if(!Path.GetFullPath(source).Equals(PackSyncPaths.Resolve(contentRoot,path),StringComparison.OrdinalIgnoreCase)||PackSyncPaths.HashFile(source)!=entry.Hash)throw new InvalidOperationException("The pack changed during download. Check for updates again.");return source;}}
}

