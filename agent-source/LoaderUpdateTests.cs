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
        string stage=Path.Combine(root,"loader-stage");Directory.CreateDirectory(Path.Combine(stage,"mods"));
        File.WriteAllText(Path.Combine(folder,"mods/test.jar"),"old mod");File.WriteAllText(Path.Combine(stage,"mods/test.jar"),"new mod");
        var state=new PackSyncState("test","test",["mods/test.jar"]);
        int count=0;rejected=false;
        try{ClientCore.CommitPack(folder,stage,["mods/test.jar"],[],state,_=>{if(++count==4)throw new InvalidOperationException("Game opened after loader switch");},update,()=>{});}catch(InvalidOperationException){rejected=true;}
        Assert(rejected&&File.ReadAllText(metadata)==before&&File.ReadAllText(Path.Combine(folder,"mods/test.jar"))=="old mod","Failed commit did not roll back loader and mods together");
        File.WriteAllText(Path.Combine(stage,"mods/test.jar"),"new mod");
        ClientCore.CommitPack(folder,stage,["mods/test.jar"],[],state,_=>{},update,()=>{});
        ClientCore.RequireMatchingLoader(folder,info);
        Assert(File.ReadAllText(Path.Combine(folder,"mods/test.jar"))=="new mod","Successful transaction missed mod update");
        string backup=Path.Combine(folder,"harbor-sync-backups","interrupted");Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(folder,".harbor-sync-transaction.json"),JsonSerializer.Serialize(new PackSyncTransaction(backup,[],[],before,update.After),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        ClientCore.Recover(folder,_=>{},()=>{});Assert(File.ReadAllText(metadata)==before,"Interrupted loader transaction was not recovered");
        checks.Add("NeoForge profile update preserves identity and preferences, rejects wrong Minecraft metadata, commits with mods, and restores both on failure or interruption");
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
        ClientCore.CommitPack(folder,root,[],[],new("test","test",[]),_=>{},update,()=>{});
        ClientCore.RequireMatchingLoader(folder,info);
        File.WriteAllText(requestFile+".result.json",JsonSerializer.Serialize(new{passed=true,officialInstaller=true,from="21.1.247",to="21.1.249",installedLibraries=Directory.GetFiles(Path.Combine(install,"libraries"),"*.jar",SearchOption.AllDirectories).Length,realProfileTouched=false},new JsonSerializerOptions{WriteIndented=true}));
    }
}
