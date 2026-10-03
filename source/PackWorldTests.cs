using System.IO.Compression;
using System.Text.Json;
namespace MinecraftHarbor;
internal static class PackWorldTests
{
    internal static void Profile(string folder,long project=73126,string version="1",string minecraft="1.21.1")
    {
        Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"minecraftinstance.json"),JsonSerializer.Serialize(new{name="Fixture Adventure",gameVersion=minecraft,baseModLoader=new{name="neoforge-21.1.249"},installedModpack=new{addonID=project,installedFile=new{fileName="Pack-"+version+".zip",serverPackFileId=20}}}));File.WriteAllText(Path.Combine(folder,"manifest.json"),JsonSerializer.Serialize(new{version}));
    }
    internal static void Fixture(string root)
    {
        string client=Path.Combine(root,"clients","Adventure");Profile(client);LibraryTests.WriteWorld(Path.Combine(client,"saves","Disk world"),"Disk world");
        Directory.CreateDirectory(Path.Combine(root,"server","mods"));File.WriteAllText(Path.Combine(root,"server","mods","fixture.jar"),"fixture mod");File.WriteAllText(Path.Combine(root,"server","fixture-launch.jar"),"fixture launcher");LibraryTests.WriteWorld(Path.Combine(root,"server","world"),"Original world");
        Directory.CreateDirectory(Path.Combine(root,"runtime","bin"));File.WriteAllText(Path.Combine(root,"runtime","bin","java.exe"),"fixture, never executed");
        ServerManager.WriteJson(Path.Combine(root,"settings.json"),new Settings{MemoryGB=8,WorldName="Original world",PackVersion="1",NeoForgeVersion="21.1.249"});
        ServerManager.WriteJson(Path.Combine(root,"library.json"),new LibraryData{ActiveId="fixture",CurseForgeRoots=[Path.Combine(root,"clients")],Profiles=[new(){Id="fixture",Name="Fixture Adventure",PackVersion="1",MinecraftVersion="1.21.1",Loader="neoforge",LoaderVersion="21.1.249",ProjectId=73126,ServerFileId=20,CurseForgePath=client,LegacyLocation=true,LaunchFile="fixture-launch.jar",UsesArgumentFile=false,Worlds=[new(){Name="Original world"}]}]});
    }
    internal static ModpackDraft Draft(ServerManager manager)
    {
        var pack=CurseForgeProfiles.Read(manager.Profile.CurseForgePath.Length>0?manager.Profile.CurseForgePath:Path.Combine(manager.Root,"clients","Adventure"));
        return new(){Source=pack,Project=new(pack.ProjectId,pack.Name,"","","",0,new()),Release=new(20,pack.ProjectId,"server.zip","1",0,null,new(){"1.21.1","neoforge"},20,new(),null),Archive=manager.ServerDir,Prepared=new(manager.ServerDir,manager.Profile)};
    }
    internal static async Task Run(string report)
    {
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"world-fixture-"+Guid.NewGuid().ToString("N"));Fixture(root);using var manager=new ServerManager(root);var checks=new List<string>();
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks.Add(message);}
        var pack=CurseForgeProfiles.Read(manager.Profile.CurseForgePath);string save=Path.Combine(pack.Path,"saves","Disk world");
        string other=Path.Combine(root,"other-drive","Other Adventure");Profile(other);LibraryTests.WriteWorld(Path.Combine(other,"saves","Another world"),"Another world");manager.Library.Data.CurseForgeRoots.Add(Path.GetDirectoryName(other)!);
        string wrong=Path.Combine(root,"clients","Other Pack");Profile(wrong,98989);LibraryTests.WriteWorld(Path.Combine(wrong,"saves","Wrong pack"),"Wrong pack");
        string old=Path.Combine(root,"clients","Old Release");Profile(old,version:"0");LibraryTests.WriteWorld(Path.Combine(old,"saves","Old world"));
        var worlds=PackWorlds.Discover(pack,manager.Library,CancellationToken.None);
        Check(worlds.Count==3&&worlds.Any(w=>w.Name=="Disk world")&&worlds.Any(w=>w.Name=="Another world")&&worlds.Any(w=>w.ProfileId==manager.Profile.Id),"Discovery includes matching profiles on multiple roots and existing Harbor worlds, excluding other packs and releases");
        bool rejected=false;try{PackWorlds.Qualify(Path.Combine(wrong,"saves","Wrong pack"),pack,manager.Library);}catch(InvalidDataException){rejected=true;}Check(rejected,"A world from another modpack is rejected");
        string unknown=Path.Combine(root,"unidentified-world");LibraryTests.WriteWorld(unknown);rejected=false;try{PackWorlds.Qualify(unknown,pack,manager.Library);}catch(InvalidDataException){rejected=true;}Check(rejected,"A standalone folder without identifiable pack provenance is not labeled compatible");
        string wrongGame=Path.Combine(pack.Path,"saves","Wrong Minecraft");LibraryTests.WriteWorld(wrongGame,minecraft:"1.20.1");rejected=false;try{PackWorlds.Qualify(wrongGame,pack,manager.Library);}catch(InvalidDataException){rejected=true;}Check(rejected,"A different Minecraft save version is rejected");
        var disk=PackWorlds.Qualify(save,pack,manager.Library);byte[] source=File.ReadAllBytes(Path.Combine(save,"level.dat")),original=File.ReadAllBytes(Path.Combine(manager.WorldDir,"level.dat"));File.WriteAllText(Path.Combine(save,"mod-data.txt"),"exact mod state");
        using(var locked=File.Open(Path.Combine(save,"session.lock"),FileMode.Create,FileAccess.ReadWrite,FileShare.None)){
            rejected=false;try{WorldImport.Copy(save,Path.Combine(root,"locked-import"),WorldImport.Read(save));}catch(IOException ex){rejected=ex.Message.StartsWith("This world is currently open");}
            Check(rejected&&!Directory.Exists(Path.Combine(root,"locked-import")),"Open worlds are rejected with a clear message before any files are copied");
        }
        var draft=Draft(manager);draft.World=disk;var catalog=new CurseForgeCatalog(root);
        await manager.CreateModdedAsync(draft,new Settings{MemoryGB=8,WorldName="Imported world"},"New adventure",new(),new(),false,catalog,CancellationToken.None);
        var created=manager.Library.Data.Profiles.Last();var cfg=manager.ReadProfileSettings(created);string imported=Path.Combine(manager.Library.ProfileRoot(created),"server",cfg.WorldFolder);var info=WorldImport.Read(save);
        Check(!created.Worlds[0].CanGenerate&&WorldImport.Read(imported).Name=="Imported world"&&File.ReadAllText(Path.Combine(imported,"mod-data.txt"))=="exact mod state"&&manager.ReadGameProperties(created)["level-name"]==cfg.WorldFolder,"New server uses the selected imported world and preserves its mod data");
        using(var input=File.OpenRead(Path.Combine(imported,"playerdata",info.PlayerUuid+".dat")))using(var gzip=new GZipStream(input,CompressionMode.Decompress))using(var bytes=new MemoryStream()){gzip.CopyTo(bytes);Check(bytes.ToArray().SequenceEqual(new byte[]{10,0,0}.Concat(info.PlayerPayload!)),"Singleplayer inventory and embedded mod player data carry over exactly");}
        Check(File.ReadAllBytes(Path.Combine(save,"level.dat")).SequenceEqual(source)&&File.ReadAllBytes(Path.Combine(manager.WorldDir,"level.dat")).SequenceEqual(original),"Creating a server leaves the source save and active Harbor world unchanged");
        var activeConfig=manager.ReadProfileSettings(manager.Profile);activeConfig.WorldName="Selected disk world";await manager.SaveProfileWorldAsync(manager.Profile,activeConfig,"Existing adventure",disk);
        Check(manager.Config.WorldFolder!="world"&&manager.World.Name=="Selected disk world"&&manager.Properties()["level-name"]==manager.Config.WorldFolder,"Saving an existing server imports and selects its chosen world");
        int count=manager.Profile.Worlds.Count;var own=PackWorlds.Qualify(Path.Combine(root,"server","world"),pack,manager.Library);activeConfig=manager.ReadProfileSettings(manager.Profile);activeConfig.WorldName=own.Name;await manager.SaveProfileWorldAsync(manager.Profile,activeConfig,"Existing adventure",own);
        Check(manager.Config.WorldFolder=="world"&&manager.Profile.Worlds.Count==count&&File.ReadAllBytes(Path.Combine(manager.WorldDir,"level.dat")).SequenceEqual(original),"Selecting a world already in this server switches without another copy");
        cfg=manager.ReadProfileSettings(created);cfg.WorldName="Inactive imported world";await manager.SaveProfileWorldAsync(created,cfg,"Inactive adventure",disk);
        Check(manager.Config.WorldFolder=="world"&&manager.ReadProfileSettings(created).WorldFolder!=created.Worlds[0].Folder,"Editing an inactive server leaves the active selection unchanged");
        Profile(pack.Path,version:"2");rejected=false;try{await manager.SaveProfileWorldAsync(manager.Profile,manager.ReadProfileSettings(manager.Profile),"Existing adventure",disk);}catch(InvalidDataException){rejected=true;}Check(rejected&&manager.Profile.Worlds.Count==count,"Pack identity is checked again on save, catching a changed profile after selection");
        rejected=false;try{WorldImport.Copy(save,Path.Combine(root,"cancelled-import"),info,token:new CancellationToken(true));}catch(OperationCanceledException){rejected=true;}Check(rejected&&!Directory.Exists(Path.Combine(root,"cancelled-import")),"Cancelled import leaves no new world and never changes the source");
        ServerManager.WriteJson(report,new{passed=true,checks,fixtureRoot=root,realWorldModified=false});
    }
    internal static void Ui(string report)
    {
        report=Path.GetFullPath(report);string root=Path.Combine(Path.GetDirectoryName(report)!,"world-ui-fixture-"+Guid.NewGuid().ToString("N"));Fixture(root);
        using var manager=new ServerManager(root);using var form=new MainForm(manager,visualTestReport:"__world_picker_test__:"+report);Application.Run(form);
    }
    internal static void Live(string root,string client,string report)
    {
        var library=new ServerLibrary(root);var pack=CurseForgeProfiles.Read(client);var worlds=PackWorlds.Discover(pack,library,CancellationToken.None);
        if(worlds.Count==0)throw new Exception("No matching existing saves were found.");
        ServerManager.WriteJson(report,new{passed=true,worlds,realWorldModified=false});
    }
}
public sealed partial class MainForm
{
    async Task VerifyWorldPicker(string report)
    {
        var checks=new List<string>();void Check(bool ok,string message){if(!ok)throw new Exception(message);checks.Add(message);}
        IEnumerable<Control> Below(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Below(c))yield return child;}}
        async Task Settled(){var watch=System.Diagnostics.Stopwatch.StartNew();while(managementTransition&&watch.Elapsed.TotalSeconds<10)await Task.Delay(25);if(managementTransition)throw new Exception("Transition timed out.");}
        async Task Loaded(){var watch=System.Diagnostics.Stopwatch.StartNew();while(!Below(management).OfType<Label>().Any(l=>l.Text=="Disk world")&&watch.Elapsed.TotalSeconds<15)await Task.Delay(50);Check(Below(management).OfType<Label>().Any(l=>l.Text=="Disk world"),"Matching saved worlds load in the picker");await Settled();}
        void Capture(string name){using var bitmap=new Bitmap(Width,Height);DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(Path.Combine(Path.GetDirectoryName(report)!,name+".png"));}
        try{
            ShowPage("Server Management");await Task.Delay(500);createPack=PackWorldTests.Draft(server);PrepareModdedDraft();TransitionManagement(RenderCreateSettings);await Settled();
            Check(Below(management).OfType<HarborButton>().Any(b=>b.Text=="Choose World"),"New-server settings expose Choose World beside the name");Capture("world-create-settings");
            OpenWorldPicker(true);await Loaded();Capture("world-picker");
            string path=Path.Combine(createPack.Source.Path,"saves","Disk world");SelectWorldChoice(PackWorlds.Qualify(path,createPack.Source,server.Library));await Settled();
            Check(createPack.World?.Path==path&&createSettings.WorldName=="Disk world"&&server.Library.Data.Profiles.Count==1&&!Directory.Exists(Path.Combine(server.Root,"profiles")),"World selection returns to settings and creates no copy before Create Server");Capture("world-selected-settings");
            OpenServerEditor(server.Profile);await Settled();OpenWorldPicker(false);await Loaded();Capture("world-editor-picker");
            SelectWorldChoice(PackWorlds.Qualify(path,PackWorlds.Pack(server.Profile),server.Library));await Settled();Check(editingDiskWorld!=null&&editingConfig!.WorldName=="Disk world"&&server.Config.WorldFolder=="world","Editor stages the selected disk world until Save Changes");Capture("world-editor-selected");
            ClientSize=new Size(1160,680);await Task.Delay(100);OpenWorldPicker(false);await Loaded();Capture("world-picker-small");
            Check(Below(management).OfType<HarborButton>().Count(b=>b.Text=="Selected")==1,"Only the staged world is shown as selected");
            Check(Below(management).OfType<HarborButton>().Where(b=>b.Text=="Browse Folder").All(b=>b.Right<=management.ClientSize.Width),"Browse action stays within the page at the minimum window size");
            ServerManager.WriteJson(report,new{passed=true,checks,realWorldModified=false});
        }catch(Exception ex){ServerManager.WriteJson(report,new{passed=false,error=ex.ToString(),checks,realWorldModified=false});}
        finally{exiting=true;Close();}
    }
}
