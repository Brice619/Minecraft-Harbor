using System.Text.Json;
using System.Text;
namespace MinecraftHarbor;
public sealed partial class ServerManager
{
    public Settings ReadProfileSettings(ServerProfile profile)=>JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(profile.Id==Profile.Id?Config:LoadSettings(profile)))!;
    public Dictionary<string,string> ReadGameProperties(ServerProfile profile)
    {
        var file=Path.Combine(Library.ProfileRoot(profile),"server","server.properties");var values=new Dictionary<string,string>();
        if(File.Exists(file))foreach(var line in File.ReadAllLines(file)){int i=line.IndexOf('=');if(i>0&&!line.StartsWith('#'))values[line[..i]]=line[(i+1)..];}
        if(profile.Loader=="vanilla")foreach(string key in new[]{"difficulty","gamemode"})if(values.TryGetValue(key,out var raw)&&int.TryParse(raw,out int number)&&number>=0&&number<4)values[key]=(key=="difficulty"?new[]{"peaceful","easy","normal","hard"}:new[]{"survival","creative","adventure","spectator"})[number];
        return values;
    }
    public Dictionary<string,string> ReadGameRules(ServerProfile profile)
    {
        string root=Library.ProfileRoot(profile),world=Path.Combine(root,"server",SelectedWorldFolder(profile)),file=Path.Combine(world,"level.dat"),modern=Path.Combine(world,"data","minecraft","game_rules.dat");
        var values=File.Exists(modern)?new WorldMetadata(modern).Rules():File.Exists(file)?new WorldMetadata(file).Rules():new Dictionary<string,string>();
        string pending=Path.Combine(root,"initial-game-rules.json");if(File.Exists(pending))foreach(var rule in JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(pending))!)values[rule.Key]=rule.Value;
        return values;
    }
    void RequireEditable(ServerProfile profile)
    {
        if(profile.Deleted)throw new InvalidOperationException("This server has been deleted.");
        if(Busy||OtherServerRunning()||HasProcess)throw new InvalidOperationException("Stop the running server before saving or deleting a server.");
    }
    public void SaveProfile(ServerProfile profile,Settings config,string name)
    {
        RequireEditable(profile);SaveProfileCore(profile,config,name);
    }
    internal Task SaveProfileWorldAsync(ServerProfile profile,Settings config,string name,PackWorld? selected)
    {
        RequireEditable(profile);
        if(selected==null){SaveProfileCore(profile,config,name);return Task.CompletedTask;}
        CheckConfig(config);SystemMemory.Validate(config.MemoryGB);name=WorldName(name);config.WorldName=WorldName(config.WorldName);
        return Perform("Copying the selected world…",async()=>{
            RequireStopped();var chosen=PackWorlds.Qualify(selected.Path,PackWorlds.Pack(profile),Library);
            if(chosen.ProfileId==profile.Id){config.WorldFolder=chosen.Folder;SaveProfileCore(profile,config,name);return;}
            var info=WorldImport.Read(chosen.Path);var world=new WorldEntry{Folder="world-"+Guid.NewGuid().ToString("N"),Name=info.Name};
            string root=Library.ProfileRoot(profile),stage=Path.Combine(root,"imports",world.Folder);
            await Task.Run(()=>WorldImport.Copy(chosen.Path,stage,info,s=>Activity=s));
            Directory.Move(stage,Path.Combine(root,"server",world.Folder));profile.Worlds.Add(world);string previous=config.WorldFolder;
            try{config.WorldFolder=world.Folder;SaveProfileCore(profile,config,name);}catch{config.WorldFolder=previous;profile.Worlds.Remove(world);throw;}
            Log("Selected "+config.WorldName+" for "+profile.DisplayName+". The source save is unchanged.");
        });
    }
    void SaveProfileCore(ServerProfile profile,Settings config,string name)
    {
        if(!Library.Data.Profiles.Contains(profile))throw new InvalidOperationException("This server is not in your library.");
        CheckConfig(config);SystemMemory.Validate(config.MemoryGB);name=WorldName(name);config.WorldName=WorldName(config.WorldName);
        var world=profile.Worlds.Single(w=>w.Folder==config.WorldFolder);string root=Library.ProfileRoot(profile);
        var metadata=Path.Combine(root,"server",world.Folder,"level.dat");
        if(File.Exists(metadata)&&world.Name!=config.WorldName)new WorldMetadata(metadata).Save(metadata,config.WorldName,new());
        WriteJson(Path.Combine(root,"settings.json"),config);profile.ServerName=name;world.Name=config.WorldName;Library.Save();
        if(profile.Id==Profile.Id){Config=config;ApplySettings();}
    }
    public void SaveGameSettings(ServerProfile profile,Dictionary<string,string> properties,Dictionary<string,string> rules)
    {
        RequireEditable(profile);var existing=ReadGameProperties(profile);var currentRules=ReadGameRules(profile);
        foreach(var entry in properties){
            if(!existing.TryGetValue(entry.Key,out var old)||entry.Value.Any(c=>c is '\r' or '\n'))throw new ArgumentException("Invalid server property.");
            if(bool.TryParse(old,out _)&&entry.Value is not ("true" or "false"))throw new ArgumentException("Invalid toggle: "+entry.Key);
            if(int.TryParse(old,out _)&&(!int.TryParse(entry.Value,out int number)||number<(entry.Key is "max-tick-time" or "pause-when-empty-seconds" or "network-compression-threshold"? -1:0)))throw new ArgumentException("Invalid number: "+entry.Key);
            if(entry.Key is "op-permission-level" or "function-permission-level" && (!int.TryParse(entry.Value,out int permission)||permission<1||permission>4))throw new ArgumentException("Permission level must be between 1 and 4.");
            if(entry.Key=="max-world-size"&&(!int.TryParse(entry.Value,out int size)||size<1||size>29999984))throw new ArgumentException("World size must be between 1 and 29,999,984.");
            if(entry.Key=="difficulty"&&!new[]{"peaceful","easy","normal","hard"}.Contains(entry.Value))throw new ArgumentException("Choose a difficulty.");
            if(entry.Key=="gamemode"&&!new[]{"survival","creative","adventure","spectator"}.Contains(entry.Value))throw new ArgumentException("Choose a game mode.");
            existing[entry.Key]=entry.Value;
        }
        foreach(var entry in rules){if(!currentRules.TryGetValue(entry.Key,out var old))throw new ArgumentException("Unknown game rule.");if(bool.TryParse(old,out _)){if(entry.Value is not ("true" or "false"))throw new ArgumentException("Invalid game rule.");}else if(!int.TryParse(entry.Value,out int number)||number<0)throw new ArgumentException("Invalid game rule number.");}
        string root=Path.Combine(Library.ProfileRoot(profile),"server"),file=Path.Combine(root,SelectedWorldFolder(profile),"level.dat");
        string modern=Path.Combine(root,SelectedWorldFolder(profile),"data","minecraft","game_rules.dat");
        if(File.Exists(modern))new WorldMetadata(modern).Save(modern,null,rules);
        else if(File.Exists(file))new WorldMetadata(file).Save(file,ReadProfileSettings(profile).WorldName,rules);
        else WriteJson(Path.Combine(Library.ProfileRoot(profile),"initial-game-rules.json"),rules);
        if(profile.Loader=="vanilla"&&Version.Parse(profile.MinecraftVersion)<new Version(1,13)){existing["difficulty"]=Array.IndexOf(new[]{"peaceful","easy","normal","hard"},existing["difficulty"]).ToString();existing["gamemode"]=Array.IndexOf(new[]{"survival","creative","adventure","spectator"},existing["gamemode"]).ToString();}
        File.WriteAllLines(Path.Combine(root,"server.properties"),existing.OrderBy(p=>p.Key).Select(p=>p.Key+"="+p.Value),new UTF8Encoding(false));
    }
    public void DeleteProfile(ServerProfile profile)
    {
        RequireEditable(profile);
        string directory=Path.GetFullPath(Path.Combine(Library.ProfileRoot(profile),"server"));
        string prefix=Path.EndsInDirectorySeparator(Root)?Root:Root+Path.DirectorySeparatorChar;
        if(!Library.Data.Profiles.Contains(profile)||!directory.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(directory).Equals("server",StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid server folder.");
        if(Directory.Exists(directory))Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(directory,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        profile.Deleted=true;
        if(profile.Id==Profile.Id&&Library.Data.Profiles.FirstOrDefault(p=>!p.Deleted) is {} next){Library.Data.ActiveId=next.Id;Config=LoadSettings(next);Directory.CreateDirectory(BackupDir);}
        Library.Save();
    }
}

