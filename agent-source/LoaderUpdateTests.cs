using System.Text.Json;
using System.Text.Json.Nodes;
using MinecraftHarbor;
namespace HarborAgent;

internal static class LoaderUpdateTests
{
    internal static void Transactions(string root,List<string> checks)
    {
        void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        string folder=Path.Combine(root,"loader-transaction");Directory.CreateDirectory(Path.Combine(folder,"mods"));
        string metadata=Path.Combine(folder,"minecraftinstance.json");
        string before="{\"gameVersion\":\"1.21.1\",\"baseModLoader\":{\"name\":\"neoforge-21.1.247\"},\"guid\":\"original\",\"allocatedMemory\":12000,\"installedModpack\":{\"addonID\":925200}}";
        File.WriteAllText(metadata,before);
        var info=new LanSyncInfo("Test","ATM10","1.21.1",true,"","","",925200,"neoforge","21.1.249",true);
        const string version="{\"id\":\"neoforge-21.1.249\",\"inheritsFrom\":\"1.21.1\",\"libraries\":[]}";
        const string profile="{\"version\":\"neoforge-21.1.249\",\"minecraft\":\"1.21.1\"}";
        var update=NeoForgeUpdate.BuildProfile(before,info,version,profile);
        var after=JsonNode.Parse(update.After)!;
        Assert(after["guid"]!.GetValue<string>()=="original"&&after["allocatedMemory"]!.GetValue<int>()==12000&&after["installedModpack"]!["addonID"]!.GetValue<int>()==925200,"Profile identity or preferences changed");
        bool rejected=false;try{NeoForgeUpdate.BuildProfile(before,info,version.Replace("1.21.1","1.20.1"),profile);}catch(InvalidDataException){rejected=true;}Assert(rejected,"Wrong Minecraft installer accepted");
        string stage=PackUpdateStorage.NewStage(folder);Directory.CreateDirectory(Path.Combine(stage,"mods"));
        File.WriteAllText(Path.Combine(folder,"mods/test.jar"),"old mod");File.WriteAllText(Path.Combine(stage,"mods/test.jar"),"new mod");
        var state=new PackSyncState("test","test",["mods/test.jar"],[new("mods/test.jar",PackSyncPaths.HashFile(Path.Combine(stage,"mods/test.jar")),7,0)]);
        rejected=false;
        try{ClientCore.CommitPack(folder,stage,["mods/test.jar"],[],state,_=>{if(File.ReadAllText(Path.Combine(folder,"mods/test.jar"))=="new mod")throw new InvalidOperationException("Game opened after mod replacement");},update,()=>{});}catch(InvalidOperationException){rejected=true;}
        Assert(rejected&&File.ReadAllText(metadata)==before&&File.ReadAllText(Path.Combine(folder,"mods/test.jar"))=="new mod"&&File.Exists(Path.Combine(folder,".harbor-sync-transaction.json")),"Interrupted update lost its pending record");
        ClientCore.Recover(folder,_=>{},()=>{});
        ClientCore.RequireMatchingLoader(folder,info);
        Assert(File.ReadAllText(Path.Combine(folder,"mods/test.jar"))=="new mod"&&!Directory.Exists(stage),"Recovery missed mod update or temporary cleanup");
        for(int i=0;i<5;i++)
        {
            string nextStage=PackUpdateStorage.NewStage(folder);Directory.CreateDirectory(Path.Combine(nextStage,"mods"));File.WriteAllText(Path.Combine(nextStage,"mods/test.jar"),"update "+i);
            var nextState=new PackSyncState("test","test",["mods/test.jar"],[new("mods/test.jar",PackSyncPaths.HashFile(Path.Combine(nextStage,"mods/test.jar")),8,0)]);
            ClientCore.CommitPack(folder,nextStage,["mods/test.jar"],[],nextState,_=>{});
        }
        Assert(Directory.GetFiles(folder,"minecraftinstance.json",SearchOption.AllDirectories).Length==1&&!Directory.Exists(Path.Combine(folder,"harbor-sync-backups"))&&!Directory.Exists(Path.Combine(PackUpdateStorage.Root(folder),"backups")),"Repeated updates created a profile copy or backup");
        Assert(Directory.GetFiles(PackUpdateStorage.Root(folder),"minecraftinstance.json",SearchOption.AllDirectories).Length==0&&Directory.GetDirectories(Path.Combine(PackUpdateStorage.Root(folder),"staging")).Length==0,"Temporary updates created playable metadata or were not cleaned");
        checks.Add("Five repeated updates modify one profile in place, create no backups or extra launcher profiles, preserve its identity, and clean temporary downloads; interruptions resume the verified download");
        string backup=Path.Combine(folder,"harbor-sync-backups","interrupted");Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(folder,".harbor-sync-transaction.json"),JsonSerializer.Serialize(new PackSyncTransaction(backup,[],[],before,update.After),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        ClientCore.Recover(folder,_=>{},()=>{});Assert(File.ReadAllText(metadata)==before,"Interrupted loader transaction was not recovered");
        string legacy=Path.Combine(folder,"harbor-sync-backups","20261003-120000-123-01234567");Directory.CreateDirectory(legacy);File.WriteAllText(Path.Combine(legacy,"minecraftinstance.json"),before);
        PackUpdateStorage.RetireLegacyMetadata(folder,()=>{});
        Assert(!File.Exists(Path.Combine(legacy,"minecraftinstance.json"))&&Directory.GetFiles(legacy,"retired-profile-*.json").Length==1,"Legacy rollback copy remains launchable");
        checks.Add("NeoForge preserves profile identity and preferences and rejects wrong Minecraft metadata; old-client recovery stays compatible and old rollback metadata is retired without deleting its data");
    }

    internal static async Task Install(string requestFile)
    {
        var request=JsonNode.Parse(File.ReadAllText(requestFile))!;
        string root=Path.GetFullPath(request["testRoot"]!.GetValue<string>()),java=request["java"]!.GetValue<string>();
        if(Directory.Exists(root))throw new InvalidOperationException("Installation test requires a new empty directory.");
        string folder=Path.Combine(root,"Instances","Test"),install=Path.Combine(root,"Install");Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(install,"versions"));
        string before="{\"gameVersion\":\"1.21.1\",\"baseModLoader\":{\"name\":\"neoforge-21.1.247\"},\"guid\":\"test-only\",\"allocatedMemory\":8000}";
        File.WriteAllText(Path.Combine(folder,"minecraftinstance.json"),before);
        var info=new LanSyncInfo("Test","ATM10","1.21.1",true,"","","",925200,"neoforge","21.1.249",true);
        var update=await NeoForgeUpdate.Prepare(folder,info,message=>File.AppendAllText(requestFile+".progress.txt",message+Environment.NewLine),_=>{},()=>{},java);
        if(File.ReadAllText(Path.Combine(folder,"minecraftinstance.json"))!=before)throw new Exception("Preparation switched profile before mods were ready");
        NeoForgeUpdate.VerifyInstalled(install,info);
        ClientCore.CommitPack(folder,PackUpdateStorage.NewStage(folder),[],[],new("test","test",[],[]),_=>{},update,()=>{});
        ClientCore.RequireMatchingLoader(folder,info);
        File.WriteAllText(requestFile+".result.json",JsonSerializer.Serialize(new{passed=true,officialInstaller=true,from="21.1.247",to="21.1.249",installedLibraries=Directory.GetFiles(Path.Combine(install,"libraries"),"*.jar",SearchOption.AllDirectories).Length,realProfileTouched=false},new JsonSerializerOptions{WriteIndented=true}));
    }
}
