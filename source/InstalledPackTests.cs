namespace MinecraftHarbor;
internal static class InstalledPackTests
{
    internal static async Task Live(string root,string client,string report)
    {
        var pack=CurseForgeProfiles.Read(client);var library=new ServerLibrary(root);
        var source=InstalledPackSelection.FindPrepared(root,library,pack)??throw new Exception("Installed ATM10 did not resolve to existing server files.");
        if(pack.Logo.Length==0)throw new Exception("Installed ATM10 logo was missing.");
        var catalog=new CurseForgeCatalog(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"live-logo-check"));
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(45));string art=await catalog.Artwork(new(pack.ProjectId,pack.Name,"",pack.Logo,pack.Website,0,new()),cancel.Token);
        if(!CatalogArtwork.Valid(art))throw new Exception("ATM10 publisher logo failed to decode.");
        ServerManager.WriteJson(report,new{passed=true,pack.Name,pack.PackVersion,preparedDirectory=source.Directory,logo=art,realServerModified=false});
    }
    internal static void Ui(string report)
    {
        report=Path.GetFullPath(report);string root=Path.Combine(Path.GetDirectoryName(report)!,"installed-ui-fixture-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"server","mods"));File.WriteAllText(Path.Combine(root,"server","mods","test.jar"),"fixture mod");File.WriteAllText(Path.Combine(root,"server","test-launch.jar"),"fixture launcher");
        ServerManager.WriteJson(Path.Combine(root,"settings.json"),new Settings{MemoryGB=8,WorldName="Test world"});
        ServerManager.WriteJson(Path.Combine(root,"library.json"),new LibraryData{ActiveId="test",Profiles=[new(){Id="test",Name="All the Mods 10 - ATM10",PackVersion="8.1",MinecraftVersion="1.21.1",Loader="neoforge",LoaderVersion="21.1.249",ProjectId=925200,ServerFileId=8764245,LegacyLocation=true,UsesArgumentFile=false,LaunchFile="test-launch.jar",Worlds=[new(){CanGenerate=true}]}]});
        using var server=new ServerManager(root);using var form=new MainForm(server,visualTestReport:"__installed_pack_test__:"+report);Application.Run(form);
    }
}
public sealed partial class MainForm
{
    async Task VerifyInstalledPack(string report)
    {
        var checks=new List<string>();
        void Assert(bool ok,string message){if(!ok)throw new Exception(message);checks.Add(message);}
        void Capture(string name){using var image=new Bitmap(Width,Height);DrawToBitmap(image,new Rectangle(Point.Empty,Size));image.Save(Path.Combine(Path.GetDirectoryName(report)!,name+".png"));}
        async Task Settled(){var watch=System.Diagnostics.Stopwatch.StartNew();while(managementTransition&&watch.Elapsed.TotalSeconds<10)await Task.Delay(25);if(managementTransition)throw new Exception("Page transition did not finish.");await Task.Delay(75);}
        IEnumerable<Control> ControlsBelow(Control parent){foreach(Control c in parent.Controls){yield return c;foreach(var child in ControlsBelow(c))yield return child;}}
        try{
            ShowPage("Server Management");await Task.Delay(500);
            var pack=new CurseForgeProfile("",server.Profile.Name,"8.1","1.21.1","neoforge","21.1.249",925200,8764245){Logo="https://media.forgecdn.net/avatars/thumbnails/1182/438/256/256/638755918649288941.png"};
            OpenInstalledPack(pack);
            var timeout=System.Diagnostics.Stopwatch.StartNew();
            while((createPack==null||managementTransition||!ControlsBelow(management).OfType<CreationTile>().Any())&&timeout.Elapsed.TotalSeconds<10)await Task.Delay(50);
            Assert(createPack?.Prepared!=null&&ControlsBelow(management).OfType<CreationTile>().Any(),"Selecting an already prepared installed pack reaches the next screen without a ZIP dialog");
            timeout.Restart();while(!ControlsBelow(management).OfType<PictureBox>().Any(p=>p.Image!=null)&&timeout.Elapsed.TotalSeconds<45)await Task.Delay(100);
            Assert(ControlsBelow(management).OfType<PictureBox>().Any(p=>p.Image!=null),"The publisher logo loads in the selected-pack banner even without a preloaded catalog card");Capture("installed-pack-choice");
            Assert(server.Library.Data.Profiles.Count==1&&!Directory.Exists(Path.Combine(server.Root,"profiles")),"Selecting the pack makes no new server copy");
            typeof(Control).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(ControlsBelow(management).OfType<CreationTile>().First(t=>t.AccessibleName=="Use As Is"),[EventArgs.Empty]);
            await Settled();Assert(ControlsBelow(management).OfType<Label>().Any(l=>l.Text=="Server Settings"),"Use As Is proceeds to server settings with the same fade transition");Capture("installed-pack-settings");
            Assert(ControlsBelow(management).OfType<PictureBox>().Any(p=>p.Image!=null),"Server settings displays the pack logo");
            TransitionManagement(RenderCreateGame);await Settled();Assert(ControlsBelow(management).OfType<Label>().Any(l=>l.Text=="Game Settings")&&ControlsBelow(management).OfType<PictureBox>().Any(p=>p.Image!=null),"Game settings displays the pack logo");Capture("installed-pack-game");
            createPack!.MetadataResolved=true;TransitionManagement(RenderCustomizeModpack);await Settled();
            Assert(ControlsBelow(management).OfType<Label>().Any(l=>l.Text=="Customize Modpack"),"Prepared server mods remain available for customization");Capture("installed-pack-customize");
            TransitionManagement(()=>RenderSetupLoading(createName,"8.1","modded",()=>{}));await Settled();Assert(ControlsBelow(management).OfType<PictureBox>().Any(p=>p.Image!=null),"Loading screen displays the pack logo");Capture("installed-pack-loading");
            ServerManager.WriteJson(report,new{passed=true,checks,realServerModified=false});
        }catch(Exception ex){ServerManager.WriteJson(report,new{passed=false,error=ex.ToString(),checks,realServerModified=false});}
        finally{exiting=true;Close();}
    }
}
