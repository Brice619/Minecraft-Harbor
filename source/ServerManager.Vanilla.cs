using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace MinecraftHarbor;
public sealed partial class ServerManager
{
    internal Task CreateVanillaAsync(VanillaVersion version,Settings settings,string name,Dictionary<string,string> properties,Dictionary<string,string> rules,CancellationToken token,bool allowCheats=false,Action<SetupProgress>? progress=null,string? runtimeRoot=null)=>Perform("Creating vanilla server…",async()=>{
        void Report(SetupProgress value){Activity=value.Detail;progress?.Invoke(value);}
        Report(new(0,0,"Preparing Minecraft and its Java runtime…"));
        CheckConfig(settings);SystemMemory.Validate(settings.MemoryGB);name=WorldName(name);settings.WorldName=WorldName(settings.WorldName);
        var allowedProps=VanillaCatalog.Properties(version);var allowedRules=VanillaCatalog.Rules(version);
        foreach(var property in properties){if(!allowedProps.ContainsKey(property.Key)||property.Value.Any(c=>c is '\r' or '\n'))throw new ArgumentException("Invalid game setting.");}
        foreach(var r in rules){if(!allowedRules.TryGetValue(r.Key,out var original)||bool.TryParse(original,out _)&&!bool.TryParse(r.Value,out _)||!bool.TryParse(original,out _)&&(!int.TryParse(r.Value,out var n)||n<0))throw new ArgumentException("Invalid game rule.");}
        using var details=await VanillaCatalog.Details(version);var info=details.RootElement;
        if(info.GetProperty("id").GetString()!=version.Id||!info.GetProperty("downloads").TryGetProperty("server",out var download))throw new InvalidOperationException("Mojang does not provide a server download for this version.");
        var profile=new ServerProfile{Name="Vanilla",ServerName=name,PackVersion=version.Id,MinecraftVersion=version.Id,Loader="vanilla",LoaderVersion="",UsesArgumentFile=false,LaunchFile="server.jar",Worlds=new(){new(){Name=settings.WorldName,CanGenerate=true}}};
        var stage=Path.Combine(Root,"profiles",".preparing-"+profile.Id);var folder=Path.Combine(stage,"server");Directory.CreateDirectory(folder);
        int java=info.TryGetProperty("javaVersion",out var j)?j.GetProperty("majorVersion").GetInt32():8;
        profile.JavaPath=await PackInstaller.EnsureJava(runtimeRoot??Root,java,s=>Activity=s,token);
        Activity="Downloading Minecraft "+version.Id+"…";
        Report(new(0,.35,"Downloading Minecraft "+version.Id+"…"));
        string jar=Path.Combine(folder,"server.jar");await PackInstaller.Download(download.GetProperty("url").GetString()!,jar,download.GetProperty("size").GetInt64(),s=>Activity=s,token,fraction:f=>Report(new(0,.35+.60*f,"Downloading Minecraft "+version.Id+"…")));
        using(var stream=File.OpenRead(jar))if(!Convert.ToHexString(await SHA1.HashDataAsync(stream,token)).Equals(download.GetProperty("sha1").GetString(),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Minecraft download checksum failed.");
        var p=new Dictionary<string,string>(properties){["level-name"]="world",["max-players"]=settings.MaxPlayers.ToString(),["view-distance"]=settings.ViewDistance.ToString(),["simulation-distance"]=settings.SimulationDistance.ToString(),["server-port"]="25565",["server-ip"]="",["motd"]=name,["white-list"]="false",["enforce-whitelist"]="false"};
        if(version.Number<new Version(1,13)){p["difficulty"]=Array.IndexOf(new[]{"peaceful","easy","normal","hard"},p["difficulty"]).ToString();p["gamemode"]=Array.IndexOf(new[]{"survival","creative","adventure","spectator"},p["gamemode"]).ToString();}
        File.WriteAllLines(Path.Combine(folder,"server.properties"),p.Select(e=>e.Key+"="+e.Value),new UTF8Encoding(false));
        if(allowCheats){string operators=Path.Combine(ServerDir,"ops.json");if(File.Exists(operators)){if(version.Number<new Version(1,7,6)){using var ops=JsonDocument.Parse(File.ReadAllText(operators));File.WriteAllLines(Path.Combine(folder,"ops.txt"),ops.RootElement.EnumerateArray().Select(op=>op.GetProperty("name").GetString()!));}else File.Copy(operators,Path.Combine(folder,"ops.json"));}else throw new InvalidOperationException("Add an operator to your current server before enabling commands for existing operators.");}
        if(rules.Count>0)WriteJson(Path.Combine(stage,"initial-game-rules.json"),rules);
        if(EulaAccepted)File.WriteAllText(Path.Combine(folder,"eula.txt"),"eula=true\n");
        bool deferred=!EulaAccepted;
        Report(new(1,0,deferred?"The world will generate after you accept the EULA and start it.":"Generating your new world…",WorldDeferred:deferred));
        if(!deferred){await VanillaWorldSetup.Generate(profile,folder,settings,rules,Report,token);File.Delete(Path.Combine(stage,"initial-game-rules.json"));}
        else Report(new(2,1,"Game settings are saved for the first start.",WorldDeferred:true));
        Report(new(3,0,"Preparing startup configuration…",WorldDeferred:deferred));
        settings.WorldFolder="world";settings.PackVersion=version.Id;settings.NeoForgeVersion="";WriteJson(Path.Combine(stage,"settings.json"),settings);
        File.WriteAllLines(Path.Combine(folder,"user_jvm_args.txt"),new[]{"-Xms4G","-Xmx"+settings.MemoryGB+"G"});
        Directory.CreateDirectory(Path.Combine(stage,"backups"));Report(new(4,0,"Finalizing the server folder…",WorldDeferred:deferred));await SetupFiles.FinishDirectory(stage,Library.ProfileRoot(profile),token);
        Library.Data.Profiles.Add(profile);try{Library.Save();}catch{Library.Data.Profiles.Remove(profile);throw;}
        Log("Created "+name+" (Vanilla "+version.Id+"). Make it active when you are ready to play.");
        Report(new(4,1,"Your server is ready.",true,deferred));
    });
    bool initialRulesSaving,initialRulesFailed;
    void ApplyInitialRules()
    {
        initialRulesSaving=false;initialRulesFailed=false;
        var file=Path.Combine(ProfileRoot,"initial-game-rules.json");if(!File.Exists(file))return;
        try{
            var rules=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(file))!;
            initialRulesSaving=true;
            foreach(var r in rules){var command=VanillaCatalog.RuleCommand(Profile.MinecraftVersion,r.Key,r.Value);SendCore("gamerule "+command.Key+" "+command.Value);}
            SendCore("save-all");
        }catch(Exception ex){initialRulesFailed=true;Log("Could not apply initial game settings: "+ex.Message);}
    }
    void ObserveInitialRules(string line)
    {
        if(!initialRulesSaving)return;
        if(line.Contains("Unknown",StringComparison.OrdinalIgnoreCase)||line.Contains("Incorrect argument",StringComparison.OrdinalIgnoreCase)||line.Contains("No game rule",StringComparison.OrdinalIgnoreCase))initialRulesFailed=true;
        if(line.Contains("Saved the game",StringComparison.OrdinalIgnoreCase)||line.Contains("Saved the world",StringComparison.OrdinalIgnoreCase)){
            initialRulesSaving=false;
            if(!initialRulesFailed){try{File.Delete(Path.Combine(ProfileRoot,"initial-game-rules.json"));}catch(IOException ex){Log(ex.Message);}}
            else Log("Some initial game settings were rejected. See Console; the pending settings have been kept.");
        }
    }
}
