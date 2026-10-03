using System.Text.Json;
namespace MinecraftHarbor;
internal static class VanillaTests
{
    internal static async Task Run(string report)
    {
        var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"vanilla-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"server"));File.WriteAllText(Path.Combine(root,"server","eula.txt"),"eula=true");
        using var manager=new ServerManager(root);var checks=new List<string>();
        try{
            var versions=await VanillaCatalog.Load(root);if(versions.Last().Id!="1.2.5"||!versions.Any(v=>v.Id=="1.21.1"))throw new Exception("Catalog range incorrect");
            using var oldest=await VanillaCatalog.Details(versions.Last());if(!oldest.RootElement.GetProperty("downloads").TryGetProperty("server",out _))throw new Exception("Oldest release has no server");
            checks.Add("Live catalog includes every official release from the newest through 1.2.5, whose server download exists");
            if(VanillaCatalog.Rules(versions.Last()).Count!=0||VanillaCatalog.RuleCommand("1.21.11","doFireTick","false").Value!="0")throw new Exception("Version compatibility failed");
            var v=versions.First();var rules=VanillaCatalog.Rules(v);rules["keepInventory"]="true";rules["doFireTick"]="false";
            var stages=new List<SetupProgress>();
            await manager.CreateVanillaAsync(v,new Settings{MemoryGB=8,WorldName="Harbor creation test",KeepAwake=false},"Harbor creation test",VanillaCatalog.Properties(v),rules,CancellationToken.None,progress:p=>stages.Add(p),runtimeRoot:Path.GetFullPath("outputs/Minecraft-Harbor"));
            var p=manager.Library.Data.Profiles.Single(p=>p.Loader=="vanilla");if(manager.Profile.Id==p.Id||manager.HasProcess)throw new Exception("Creating a server activated or started it");
            if(!stages.Select(s=>s.Step).Distinct().SequenceEqual(new[]{0,1,2,3,4})||!stages.Last().Complete)throw new Exception("Setup phases did not complete in order");
            if(File.Exists(Path.Combine(manager.Library.ProfileRoot(p),"initial-game-rules.json"))||!File.Exists(Path.Combine(manager.Library.ProfileRoot(p),"server","world","level.dat"))||p.Worlds[0].CanGenerate)throw new Exception("Creation did not finish generating and saving the world");
            if(!File.ReadAllText(Path.Combine(manager.Library.ProfileRoot(p),"server","server.properties")).Contains("server-port=25565"))throw new Exception("Setup port was not restored");
            checks.Add("Creation verifies the official jar, generates and saves a real world on a temporary localhost port, reports all five phases, and leaves the current server inactive and untouched");
            var saved=manager.ReadGameRules(p);if(!saved.TryGetValue("minecraft:keep_inventory",out var keep)||keep!="true"||saved["minecraft:fire_spread_radius_around_player"]!="0")throw new Exception("Saved game rules differ from creation choices");
            manager.SaveGameSettings(p,new(),new(){{"minecraft:keep_inventory","false"}});if(manager.ReadGameRules(p)["minecraft:keep_inventory"]!="false")throw new Exception("Editing saved modern rules failed");
            checks.Add("Creation persists chosen game rules, stops the setup process cleanly, and modern world rules remain editable");
            var folder=Path.Combine(manager.Library.ProfileRoot(p),"server");string originalProps=File.ReadAllText(Path.Combine(folder,"server.properties"));using var cancellation=new CancellationTokenSource();bool cancelled=false;
            try{await VanillaWorldSetup.Generate(p,folder,new Settings{WorldName="Harbor creation test"},rules,stage=>{if(stage.Step==2)cancellation.Cancel();},cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
            if(!cancelled||File.ReadAllText(Path.Combine(folder,"server.properties"))!=originalProps||manager.HasProcess||manager.Library.Data.Profiles.Count!=2)throw new Exception("Cancellation failed to restore configuration or affected the active server");
            checks.Add("Cancelling world setup stops only its temporary process and restores the normal server configuration");
            string locked=Path.Combine(root,"locked-stage"),finished=Path.Combine(root,"finished-stage");Directory.CreateDirectory(locked);string lockFile=Path.Combine(locked,"file.txt");File.WriteAllText(lockFile,"preserved");
            var handle=File.Open(lockFile,FileMode.Open,FileAccess.Read,FileShare.Read);
            var move=SetupFiles.FinishDirectory(locked,finished,CancellationToken.None);await Task.Delay(700);if(move.IsCompleted)throw new Exception("Sharing lock did not exercise retry");handle.Dispose();await move;
            if(File.ReadAllText(Path.Combine(finished,"file.txt"))!="preserved")throw new Exception("Folder retry lost contents");checks.Add("Final folder setup retries an actual Windows sharing lock and preserves the prepared files");
            ServerManager.WriteJson(report,new{passed=true,root,version=v.Id,checks});
        }catch(Exception ex){ServerManager.WriteJson(report,new{passed=false,root,error=ex.ToString(),checks});throw;}
        finally{if(manager.HasProcess)await manager.StopAsync();}
    }
}
