using System.Text.Json;
using System.Text.Json.Nodes;
using MinecraftHarbor;
namespace HarborAgent;

internal static class LoaderUpdateTests
{
    internal static void CacheTransactions(string root,List<string> checks)
    {
        void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        string folder=Path.Combine(root,"cache-transaction"),stage=Path.Combine(root,"cache-stage");Directory.CreateDirectory(folder);Directory.CreateDirectory(stage);
        string metadata=Path.Combine(folder,"minecraftinstance.json"),cache=Path.Combine(root,"instance-cache.json");
        string before=new JsonObject{["guid"]="selected",["gameVersion"]="1.21.1",["baseModLoader"]=new JsonObject{["name"]="neoforge-21.1.247"},["allocatedMemory"]=8000}.ToJsonString();
        File.WriteAllText(metadata,before);
        string originalCache=new JsonArray(new JsonObject{["guid"]="selected",["installPath"]=folder+Path.DirectorySeparatorChar,["gameVersion"]="1.21.1",["baseModLoader"]=new JsonObject{["name"]="neoforge-21.1.247"},["installedAddons"]=new JsonArray("keep-addon")},new JsonObject{["guid"]="other",["name"]="Untouched profile",["baseModLoader"]=new JsonObject{["name"]="neoforge-21.1.174"}}).ToJsonString();
        File.WriteAllText(cache,originalCache);CurseForgeProfileCache.TestPath=cache;
        try
        {
            var info=new LanSyncInfo("Test","ATM10","1.21.1",true,"","","",925200,"neoforge","21.1.249",true);
            var update=CurseForgeProfileCache.Attach(folder,NeoForgeUpdate.BuildProfile(before,info,"{\"id\":\"neoforge-21.1.249\",\"inheritsFrom\":\"1.21.1\",\"libraries\":[]}","{\"version\":\"neoforge-21.1.249\",\"minecraft\":\"1.21.1\"}"));
            var changed=JsonNode.Parse(update.CacheAfter!)!.AsArray();var original=JsonNode.Parse(originalCache)!.AsArray();
            Assert(changed[0]!["baseModLoader"]!["name"]!.GetValue<string>()=="neoforge-21.1.249"&&JsonNode.DeepEquals(changed[0]!["installedAddons"],original[0]!["installedAddons"])&&JsonNode.DeepEquals(changed[1],original[1]),"Cache update changed another profile or addons");
            int calls=0;bool failed=false;
            try{ClientCore.CommitPack(folder,stage,[],[],new("test","test",[]),_=>{if(++calls==3)throw new InvalidOperationException("Game opened");},update,()=>{});}catch(InvalidOperationException){failed=true;}
            Assert(failed&&File.ReadAllText(cache)==originalCache&&File.ReadAllText(metadata)==before,"Cache and profile did not roll back together");
            ClientCore.CommitPack(folder,stage,[],[],new("test","test",[]),_=>{},update,()=>{});
            Assert(File.ReadAllText(cache)==update.CacheAfter&&File.ReadAllText(metadata)==update.After,"Cache and profile did not commit together");
            string backup=Path.Combine(folder,"harbor-sync-backups","interrupted-cache");Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(folder,".harbor-sync-transaction.json"),JsonSerializer.Serialize(new PackSyncTransaction(backup,[],[],before,update.After,cache,originalCache,update.CacheAfter),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            ClientCore.Recover(folder,_=>{},()=>{});Assert(File.ReadAllText(cache)==originalCache&&File.ReadAllText(metadata)==before,"Interrupted cache update was not recovered");
            File.WriteAllText(cache,originalCache+" ");failed=false;try{ClientCore.CommitPack(folder,stage,[],[],new("test","test",[]),_=>{},update,()=>{});}catch(InvalidOperationException){failed=true;}Assert(failed&&File.ReadAllText(metadata)==before,"Changed cache snapshot was overwritten");
            failed=false;try{CurseForgeProfileCache.Build(originalCache,Path.Combine(folder,"different"),update.After);}catch(InvalidDataException){failed=true;}Assert(failed,"Wrong cache profile path was accepted");
            failed=false;try{CurseForgeProfileCache.ValidatePath(Path.Combine(root,"outside.json"));}catch(InvalidDataException){failed=true;}Assert(failed,"Arbitrary cache recovery path was accepted");
            checks.Add("CurseForge's persistent cache and local profile commit and roll back together, recover after interruption, preserve other profiles and addons, and reject changed snapshots and wrong paths");
        }
        finally{CurseForgeProfileCache.TestPath=null;}
    }

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
        string combinedProfile="{\"version\":\"neoforge-21.1.249\",\"minecraft\":\"1.21.1\",\"data\":{},\"processors\":[{\"sides\":[\"server\"],\"jar\":\"server-only\"},{\"sides\":[\"client\"],\"jar\":\"client-only\"},{\"jar\":\"both\"}]}";
        var compatible=JsonNode.Parse(JsonNode.Parse(NeoForgeUpdate.BuildProfile(before,info,version,combinedProfile).After)!["baseModLoader"]!["installProfileJson"]!.GetValue<string>())!;
        Assert(compatible["path"]!.GetValue<string>()=="net.neoforged:neoforge:21.1.249"&&compatible["data"]!["SIDE"]!["client"]!.GetValue<string>()=="client"&&compatible["processors"]!.AsArray().Count==2,"CurseForge processor metadata was not normalized");
        var after=JsonNode.Parse(update.After)!;
        string runtime=Path.Combine(root,"reuse-loader"),id="neoforge-"+info.LoaderVersion;
        string versionFile=Path.Combine(runtime,"versions",id,id+".json"),library=Path.Combine(runtime,"libraries","test.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(versionFile)!);Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        File.WriteAllText(library,"verified library");
        string sha1=Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(library)));
        string official=new JsonObject{["id"]=id,["inheritsFrom"]=info.MinecraftVersion,["mainClass"]="test.Main",["libraries"]=new JsonArray(new JsonObject{["downloads"]=new JsonObject{["artifact"]=new JsonObject{["path"]="test.jar",["sha1"]=sha1}}})}.ToJsonString();
        File.WriteAllText(versionFile,official);
        Assert(NeoForgeUpdate.CanReuseInstalled(runtime,info,official),"Valid existing NeoForge was not reused");
        File.WriteAllText(library,"corrupt library");Assert(!NeoForgeUpdate.CanReuseInstalled(runtime,info,official),"Corrupt existing library was reused");
        File.WriteAllText(library,"verified library");File.Delete(library);Assert(!NeoForgeUpdate.CanReuseInstalled(runtime,info,official),"Missing library was reused");
        File.WriteAllText(library,"verified library");File.WriteAllText(versionFile,official.Replace("1.21.1","1.20.1"));Assert(!NeoForgeUpdate.CanReuseInstalled(runtime,info,official),"Wrong Minecraft version was reused");
        File.WriteAllText(versionFile,version);Assert(!NeoForgeUpdate.CanReuseInstalled(runtime,info,official),"Incomplete local manifest was reused");
        checks.Add("Existing NeoForge is reused only against official metadata with complete verified libraries; corrupt, missing and wrong-game installations require repair");
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
        string id="neoforge-"+info.LoaderVersion,installer=Path.Combine(install,"versions",id,id+".jar");
        if(!NeoForgeUpdate.HasCurseForgeLaunchFiles(folder,info))throw new Exception("CurseForge's required game JAR or patch is missing");
        string launchVersion=File.ReadAllText(Path.Combine(install,"versions",id,id+".json"));
        File.Move(installer,installer+".saved");
        if(NeoForgeUpdate.HasCurseForgeLaunchFiles(folder,info)||NeoForgeUpdate.CanReuseInstalled(install,info,launchVersion,true))throw new Exception("Incomplete v1.2 loader install was accepted");
        File.Move(installer+".saved",installer);
        ClientCore.CommitPack(folder,root,[],[],new("test","test",[]),_=>{},update,()=>{});
        ClientCore.RequireMatchingLoader(folder,info);
        File.WriteAllText(Path.Combine(folder,"minecraftinstance.json"),before);
        var reuse=await NeoForgeUpdate.Prepare(folder,info,message=>File.AppendAllText(requestFile+".progress.txt",message+Environment.NewLine),_=>{},()=>{},Path.Combine(root,"must-not-run-java.exe"));
        if(reuse.After!=update.After||File.ReadAllText(Path.Combine(folder,"minecraftinstance.json"))!=before)throw new Exception("Existing loader reuse changed the transaction");
        ClientCore.CommitPack(folder,root,[],[],new("test","test",[]),_=>{},reuse,()=>{});
        ClientCore.RequireMatchingLoader(folder,info);
        File.WriteAllText(requestFile+".result.json",JsonSerializer.Serialize(new{passed=true,officialInstaller=true,reusedVerifiedInstallation=true,from="21.1.247",to="21.1.249",installedLibraries=Directory.GetFiles(Path.Combine(install,"libraries"),"*.jar",SearchOption.AllDirectories).Length,realProfileTouched=false},new JsonSerializerOptions{WriteIndented=true}));
    }
}
