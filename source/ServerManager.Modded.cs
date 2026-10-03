using System.Text;
namespace MinecraftHarbor;
public sealed partial class ServerManager
{
    internal Task CreateModdedAsync(ModpackDraft draft,Settings settings,string name,Dictionary<string,string> properties,Dictionary<string,string> rules,bool cheats,CurseForgeCatalog catalog,CancellationToken token,Action<SetupProgress>? progress=null)=>Perform("Creating modded server…",async()=>{
        CheckConfig(settings);SystemMemory.Validate(settings.MemoryGB);name=WorldName(name);settings.WorldName=WorldName(settings.WorldName);
        progress?.Invoke(new(0,0,"Preparing this modpack's server files…",WorldDeferred:true));
        var addedPaths=new Dictionary<long,string>();foreach(var mod in draft.Added)addedPaths[mod.Key]=await catalog.Download(mod.Value,token,s=>Activity=s);
        var profile=await PackInstaller.PrepareAsync(Root,draft.Source,s=>Activity=s,token,draft.Archive,p=>progress?.Invoke(p.Step>=2?new(2,0,"Applying your server and modpack settings…",WorldDeferred:true):p));string root=Library.ProfileRoot(profile),folder=Path.Combine(root,"server");
        try{
            token.ThrowIfCancellationRequested();profile.ServerName=name;profile.Worlds[0].Name=settings.WorldName;
            foreach(var id in draft.Removed){if(!draft.ModFiles.TryGetValue(id,out var file))throw new InvalidDataException("The selected mod isn't present in this server pack.");string path=Path.Combine(folder,"mods",file.Name);if(!File.Exists(path))throw new InvalidDataException("Cannot find the selected server mod: "+file.DisplayName);File.Delete(path);}
            foreach(var mod in draft.Added){string path=Path.Combine(folder,"mods",mod.Value.Name);if(File.Exists(path))throw new InvalidDataException("An added mod conflicts with an existing file.");File.Copy(addedPaths[mod.Key],path);}
            await Task.Run(()=>{foreach(var mod in draft.LocalMods.Values){token.ThrowIfCancellationRequested();LocalMods.Install(Path.Combine(folder,"mods"),mod,draft.Source.Loader);}},token);
            PackConfiguration.Apply(folder,draft.Config);
            var p=new Dictionary<string,string>();string propertyPath=Path.Combine(folder,"server.properties");if(File.Exists(propertyPath))foreach(var line in File.ReadAllLines(propertyPath)){int i=line.IndexOf('=');if(i>0&&!line.StartsWith('#'))p[line[..i]]=line[(i+1)..];}
            foreach(var entry in properties){if(entry.Key.Any(c=>c is '\n' or '\r')||entry.Value.Any(c=>c is '\n' or '\r'))throw new InvalidDataException("Invalid server property.");p[entry.Key]=entry.Value;}
            p["motd"]=name;p["level-name"]="world";p["max-players"]=settings.MaxPlayers.ToString();p["view-distance"]=settings.ViewDistance.ToString();p["simulation-distance"]=settings.SimulationDistance.ToString();p["server-ip"]="";p["server-port"]="25565";
            File.WriteAllLines(propertyPath,p.Select(p=>p.Key+"="+p.Value),new UTF8Encoding(false));
            if(!cheats)File.WriteAllText(Path.Combine(folder,"ops.json"),"[]");
            else if(!File.Exists(Path.Combine(folder,"ops.json")))throw new InvalidOperationException("Add an operator before enabling commands for existing operators.");
            settings.WorldFolder="world";settings.PackVersion=profile.PackVersion;settings.NeoForgeVersion=profile.LoaderVersion;WriteJson(Path.Combine(root,"settings.json"),settings);
            File.WriteAllLines(Path.Combine(folder,"user_jvm_args.txt"),new[]{"-Xms4G","-Xmx"+settings.MemoryGB+"G"});
            if(rules.Count>0)WriteJson(Path.Combine(root,"initial-game-rules.json"),rules);
            progress?.Invoke(new(3,1,"Startup settings are ready.",WorldDeferred:true));
            if(File.Exists(Path.Combine(folder,"world","level.dat")))new WorldMetadata(Path.Combine(folder,"world","level.dat")).Save(Path.Combine(folder,"world","level.dat"),settings.WorldName,new());
            progress?.Invoke(new(4,0,"Registering your server…",WorldDeferred:profile.Worlds[0].CanGenerate));token.ThrowIfCancellationRequested();
            Library.Data.Profiles.Add(profile);try{Library.Save();}catch{Library.Data.Profiles.Remove(profile);throw;}
            Log("Created "+name+" ("+profile+").");progress?.Invoke(new(4,1,profile.Worlds[0].CanGenerate?"Your server is ready. Its world generates on first start.":"Your server is ready.",true,profile.Worlds[0].CanGenerate));
        }catch{profile.Deleted=true;throw;}
    });
}
