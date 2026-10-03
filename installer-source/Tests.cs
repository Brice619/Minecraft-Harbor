using System.Text.Json;

namespace HarborClientSetup;
internal static class Tests
{
    public static void Run(string report)
    {
        var checks=new List<string>();var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"installer-fixture-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string Fixture(string name,string version="8.1",string loader="neoforge-21.1.249"){
            var p=Path.Combine(root,name);Directory.CreateDirectory(Path.Combine(p,"mods"));Directory.CreateDirectory(Path.Combine(p,"saves","Keep me"));
            File.WriteAllText(Path.Combine(p,"manifest.json"),JsonSerializer.Serialize(new{name="All the Mods 10",version,minecraft=new{version="1.21.1",modLoaders=new[]{new{id=loader,primary=true}}}}));
            File.WriteAllText(Path.Combine(p,"mods","ftb-ultimine-fixture.jar"),"custom-ultimine-original");File.WriteAllText(Path.Combine(p,"mods","unrelated.jar"),"other-mod");File.WriteAllText(Path.Combine(p,"options.txt"),"settings");File.WriteAllText(Path.Combine(p,"saves","Keep me","level.dat"),"world");return p;
        }
        void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        Dictionary<string,string> Snapshot(string p)=>Directory.GetFiles(p,"*",SearchOption.AllDirectories).ToDictionary(f=>Path.GetRelativePath(p,f),Installer.Hash);
        var fresh=Fixture("Existing ATM10");var before=Snapshot(fresh);var result=Installer.InstallCore(fresh,_=>{});var after=Snapshot(fresh);
        Assert(!result.AlreadyInstalled&&after.Count==before.Count+1,"Fresh install changed an unexpected number of files");foreach(var pair in before)Assert(after[pair.Key]==pair.Value,"Existing file changed: "+pair.Key);
        Assert(Installer.Hash(Path.Combine(fresh,"mods",Installer.HelperName))==Installer.HelperHash,"Helper bytes differ");checks.Add("Fresh install adds only the checksum-verified helper; all existing mods, settings and saves remain byte-identical");
        Assert(Installer.InstallCore(fresh,_=>{}).AlreadyInstalled,"Repeat install not idempotent");Assert(Snapshot(fresh).Count==after.Count,"Repeat added files");checks.Add("Repeat install is a no-op");
        var update=Fixture("Older helper");var old=Path.Combine(update,"mods","automodpack-old.jar");File.WriteAllText(old,"old-helper");
        result=Installer.InstallCore(update,_=>{});Assert(result.BackupPath!=null&&File.ReadAllText(Path.Combine(result.BackupPath,"automodpack-old.jar"))=="old-helper"&&!File.Exists(old),"Old helper not backed up");checks.Add("Older helper is backed up and replaced without duplicate JARs");
        var rollback=Fixture("Rollback");var original=Path.Combine(rollback,"mods","automodpack-old.jar");File.WriteAllText(original,"preserve-me");
        bool rejected=false;try{Installer.InstallCore(rollback,_=>{},()=>throw new IOException("Injected copy failure"));}catch(IOException){rejected=true;}
        Assert(rejected&&File.ReadAllText(original)=="preserve-me"&&!File.Exists(Path.Combine(rollback,"mods",Installer.HelperName))&&!Directory.GetFiles(Path.Combine(rollback,"mods"),"*.tmp").Any(),"Rollback failed");checks.Add("Failed replacement restores the previous helper and removes staging files");
        foreach(var p in new[]{Fixture("Wrong release","8.2"),Fixture("Wrong loader","8.1","forge-47.0.0"),root}){
            var unchanged=Snapshot(p);rejected=false;try{Installer.InstallCore(p,_=>{});}catch(InvalidDataException){rejected=true;}Assert(rejected,"Invalid profile accepted");var now=Snapshot(p);Assert(now.Count==unchanged.Count&&unchanged.All(x=>now[x.Key]==x.Value),"Invalid profile modified");
        }
        checks.Add("Wrong pack release, wrong loader and non-profile folders are rejected without writes");
        var busy=Fixture("Running game");rejected=false;try{Installer.InstallCore(busy,_=>throw new InvalidOperationException("Minecraft is running"));}catch(InvalidOperationException){rejected=true;}
        Assert(rejected&&!File.Exists(Path.Combine(busy,"mods",Installer.HelperName)),"Running-game check bypassed");
        Assert(Installer.IsGameProcess("javaw.exe","javaw --gameDir C:\\test",fresh),"Client process missed");Assert(!Installer.IsGameProcess("java.exe","java @server_args.txt --launchTarget neoforgeserver",fresh),"Unrelated dedicated server blocked");checks.Add("Running client prevents installation; a dedicated server is distinguished from Minecraft client");
        var cf=Fixture("CurseForge metadata");File.Delete(Path.Combine(cf,"manifest.json"));File.WriteAllText(Path.Combine(cf,"minecraftinstance.json"),JsonSerializer.Serialize(new{name="My ATM10",gameVersion="1.21.1",baseModLoader=new{name="neoforge-21.1.249"},installedModpack=new{name="All the Mods 10",installedFile=new{fileName="All the Mods 10-8.1.zip"}}}));Assert(Installer.Validate(cf).Name.Contains("My ATM10"),"CurseForge fallback failed");checks.Add("CurseForge metadata works when an export manifest is absent");
        Installer.RequireGameClosed(root);checks.Add("Bundled Windows process inspection runs successfully alongside the dedicated server");
        File.WriteAllText(report,JsonSerializer.Serialize(new{passed=true,checkedUtc=DateTime.UtcNow,checks,fixtureRoot=root,realClientModified=false},new JsonSerializerOptions{WriteIndented=true}));
    }
}
