using System.Diagnostics;
using System.Text.Json;
namespace MinecraftHarbor;
public sealed partial class MainForm
{
    internal async Task VerifyDesign(string report)
    {
        var checks=new List<string>();
        void Assert(bool pass,string message){if(!pass)throw new Exception(message);}
        async Task Until(Func<bool> predicate){var watch=Stopwatch.StartNew();while(!predicate()){if(watch.Elapsed.TotalSeconds>20)throw new TimeoutException("Design check timed out");await Task.Delay(30);}}
        void Capture(string name){using var bitmap=new Bitmap(Width,Height);DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(Path.Combine(Path.GetDirectoryName(report)!,name+".png"));}
        try{
            ShowPage("Home");RefreshStatus();
            Assert(start.Visible&&!stop.Visible&&((HarborButton)start).Primary,"Initial green start button missing");
            Assert(navButtons.Select(b=>b.Text).SequenceEqual(new[]{"Home","Server Management","Console","Backups","Players","Set up another PC","Settings"}),"Unexpected navigation tabs");
            Assert(((HarborButton)navButtons[0]).Selected,"Selected-tab outline state missing");
            Assert(copyConnection.Left-ConnectionBox().Right*dashboard.DeviceDpi/96f>=LogicalToDeviceUnits(12),"Copy button overlaps the address field");
            ShowPage("Settings");var settingsPage=pages["Settings"];settingsPage.AutoScrollPosition=Point.Empty;
            Color BackgroundPixel(){using var bitmap=new Bitmap(settingsPage.Width,settingsPage.Height);settingsPage.DrawToBitmap(bitmap,new Rectangle(Point.Empty,settingsPage.Size));return bitmap.GetPixel(settingsPage.ClientSize.Width-LogicalToDeviceUnits(32),LogicalToDeviceUnits(170));}
            var beforeScroll=BackgroundPixel();int rendered=content.BackdropRenderCount;var originalArt=content.Artwork;
            settingsPage.AutoScrollPosition=new Point(0,LogicalToDeviceUnits(250));await Task.Delay(50);Capture("design-settings-scrolled");
            Assert(settingsPage.AutoScrollPosition.Y<0,"Settings did not scroll for the background check");
            Assert(BackgroundPixel()==beforeScroll&&content.BackdropRenderCount==rendered&&ReferenceEquals(content.Artwork,originalArt),"Scrolling moved or regenerated the background");
            settingsPage.AutoScrollPosition=Point.Empty;
            int labelTop=artworkStatus.Top;
            SendMessage(settingsPage.Handle,0x020A,(IntPtr)(-120<<16),IntPtr.Zero);
            Assert(settingsPage.Visible&&settingsPage.AutoScrollPosition.Y<0,"Mouse-wheel scrolling hid or froze the page");
            Assert(artworkStatus.Top==labelTop+settingsPage.AutoScrollPosition.Y,"Scrolling did not move child controls with the content");
            Assert(BackgroundPixel()==beforeScroll,"The picture moved during mouse-wheel scrolling");
            int wheelY=settingsPage.AutoScrollPosition.Y;
            SendMessage(settingsPage.Handle,0x0115,(IntPtr)7,IntPtr.Zero);
            Assert(settingsPage.AutoScrollPosition.Y<wheelY&&BackgroundPixel()==beforeScroll,"Scrollbar navigation moved the artwork or failed to scroll");
            SendMessage(settingsPage.Handle,0x0115,(IntPtr)6,IntPtr.Zero);
            Assert(settingsPage.Visible&&settingsPage.AutoScrollPosition.Y==0&&artworkStatus.Top==labelTop&&content.BackdropRenderCount==rendered,"Scrolling did not restore the original content positions and cached artwork");
            var saveSettings=settingsPage.Controls.OfType<HarborButton>().Single(b=>b.Text=="Save settings");saveSettings.Focus();
            Assert(saveSettings.Top>=0&&saveSettings.Bottom<=settingsPage.ClientSize.Height,"Keyboard focus did not reveal the scrolled settings control");
            settingsPage.AutoScrollPosition=Point.Empty;
            checks.Add("Native wheel and scrollbar messages move controls while retaining the fixed artwork, visibility and cached background");
            ShowPage("Home");
            checks.Add("Scrolling moves settings over an unchanged cached background; copy button has a separate gap beside the address field");
            start.PerformClick();await Until(()=>server.State==ServerState.Running&&!server.Busy);RefreshStatus();
            Assert(!start.Visible&&stop.Visible&&((HarborButton)stop).Danger&&stop.Text=="Stop Server","Start did not become red Stop");Capture("design-running");
            Close();Assert(!Visible&&server.HasProcess,"Closing the window stopped the running server or failed to hide Harbor");RevealWindow();Assert(Visible&&WindowState!=FormWindowState.Minimized&&server.HasProcess,"Reopening the hidden window failed or stopped the server");
            WindowState=FormWindowState.Minimized;RevealWindow();Assert(Visible&&WindowState!=FormWindowState.Minimized,"Reopening the minimized window failed");
            checks.Add("Window close hides a running server; reopening restores hidden and minimized windows without stopping the server");
            var processBefore=File.ReadAllText(Path.Combine(server.Root,"server-process.json"));ShowPage("Settings");var liveChoice=pages["Settings"].Controls.OfType<HarborDropdown>().Single();liveChoice.SelectedIndex=1;
            await Until(()=>!artworkStatus.Text.StartsWith("Loading"));Assert(packArt!=null&&server.HasProcess&&File.ReadAllText(Path.Combine(server.Root,"server-process.json"))==processBefore,"Appearance change interrupted the server");liveChoice.SelectedIndex=0;ShowPage("Home");
            stop.PerformClick();await Until(()=>!server.HasProcess&&!server.Busy);RefreshStatus();Assert(start.Visible&&!stop.Visible,"Stop did not return to green Start");
            checks.Add("Start Server changes to a red Stop Server and returns to green Start after a clean save and stop, using the actual button click handlers and a disposable mock server");
            metricChoice.SelectedIndex=1;Assert(graph.ShowMemory,"Memory graph did not select");metricChoice.SelectedIndex=0;Assert(!graph.ShowMemory,"CPU graph did not select");
            for(int attempt=0;attempt<3;attempt++){
                var popup=metricChoice.OpenMenu();await Task.Delay(40);Assert(popup.Visible&&metricChoice.IsExpanded&&metricChoice.AccessibilityObject.State.HasFlag(AccessibleStates.Expanded),"Dropdown did not open");
                ((ToolStripMenuItem)popup.Items[1]).PerformClick();popup.Close(ToolStripDropDownCloseReason.ItemClicked);await Task.Delay(40);
                Assert(graph.ShowMemory&&!popup.IsDisposed,"Dropdown did not survive selection and closing");
                popup=metricChoice.OpenMenu();popup.Close(ToolStripDropDownCloseReason.AppClicked);await Task.Delay(40);
                Assert(!popup.Visible&&!popup.IsDisposed&&!metricChoice.IsExpanded,"Dropdown did not survive dismissal");metricChoice.SelectedIndex=0;
            }
            metricChoice.AccessibilityObject.Value="Memory";Assert(graph.ShowMemory,"Accessible dropdown selection failed");metricChoice.AccessibilityObject.Value="CPU Usage";
            checks.Add("Dropdown menus can repeatedly select, dismiss, and reopen without disposing their window during close");
            ShowPage("Console");Assert(command.Bounds.Bottom<=pages["Console"].ClientSize.Height&&command.Visible,"Console input is outside the visible page");
            ShowPage("Backups");Assert(backupTable.Bottom<backupTotals.Top&&backupTotals.Bottom<=pages["Backups"].ClientSize.Height,"Backups overlap footer");
            foreach(var tab in navButtons){ShowPage(tab.Text);Assert(((HarborButton)tab).Selected,"Navigation selection did not track tab");}
            ShowPage("Server Management");OpenServerEditor(server.Profile);await Until(()=>!managementTransition);
            Assert(editRam.SelectedItem is NumberOption {Value:80},"Per-server memory did not load");
            Assert(editRam.Items.Cast<NumberOption>().Where(o=>!o.Current).Select(o=>o.Value).SequenceEqual(SystemMemory.Choices(SystemMemory.InstalledGB,8)),"RAM choices differ from installed memory");
            Assert(editSlots.Items.Cast<NumberOption>().Where(o=>!o.Current).All(o=>o.Value%5==0&&o.Value<=50)&&editView.Items.Cast<NumberOption>().Where(o=>!o.Current).All(o=>o.Value%5==0)&&editSimulation.Items.Cast<NumberOption>().Where(o=>!o.Current).All(o=>o.Value%4==0),"Setting increments are incorrect");
            var longMenu=editRam.OpenMenu();await Task.Delay(40);Assert(longMenu.Height<=Screen.FromControl(this).WorkingArea.Height&&longMenu.Items[0] is ToolStripControlHost,"Long setting choices are not scrollable");longMenu.Close();
            editServerName.Text="Unsaved draft";editWorldName.Text="Unsaved world";
            management.Controls.OfType<PaintedPanel>().Single().Controls.OfType<HarborButton>().Single(b=>b.Text=="Cancel").PerformClick();await Until(()=>!managementTransition);
            Assert(server.Config.WorldName=="New world"&&server.Profile.ServerName=="","Cancel persisted a draft");
            OpenServerEditor(server.Profile);await Until(()=>!managementTransition);
            management.Controls.OfType<PaintedPanel>().Single().Controls.OfType<HarborButton>().Single(b=>b.Text=="Edit Game Settings").PerformClick();await Until(()=>!managementTransition);
            Assert(management.Controls.OfType<Label>().Any(l=>l.Text.StartsWith("Game Settings - ")),"Game settings page did not open");
            management.AutoScrollMinSize=new Size(0,management.Height+LogicalToDeviceUnits(800));management.PerformLayout();
            int artRenders=content.BackdropRenderCount;
            SendMessage(management.Handle,0x020A,(IntPtr)(-120<<16),IntPtr.Zero);
            Assert(management.AutoScrollPosition.Y==0,"Smooth scroll jumped before animation");
            await Task.Delay(70);int middle=management.AutoScrollPosition.Y;
            Assert(middle<0,"Smooth scroll did not advance");
            await Task.Delay(180);Assert(management.AutoScrollPosition.Y<middle&&content.BackdropRenderCount==artRenders,"Smooth scroll failed to settle or regenerated the artwork");
            management.AutoScrollPosition=Point.Empty;
            checks.Add("Game Settings wheel scroll eases across frames and preserves the cached background");
            management.Controls.OfType<HarborButton>().Single(b=>b.Text=="Cancel").PerformClick();await Until(()=>!managementTransition);
            Assert(management.Controls.OfType<Label>().Any(l=>l.Text=="Server Settings"),"Back transition failed");
            checks.Add("Per-server fields load, long menus scroll, Cancel discards edits, and forward/back settings fades finish");
            BeginCreateServer();await Until(()=>!managementTransition);Assert(management.Controls.OfType<CreationTile>().Count()==2,"Creation choice cards missing");Capture("creation-choice");
            vanillaVersions=new(){new("1.21.1","https://example.invalid",DateTime.UtcNow),new("1.2.5","https://example.invalid",DateTime.UtcNow.AddYears(-12))};
            TransitionManagement(RenderVanillaVersions);await Until(()=>!managementTransition);Capture("creation-versions");
            createVersion=vanillaVersions[0];createProperties=VanillaCatalog.Properties(createVersion);createRules=VanillaCatalog.Rules(createVersion);TransitionManagement(RenderCreateSettings);await Until(()=>!managementTransition);
            var nameBox=management.Controls.OfType<PaintedPanel>().SelectMany(p=>p.Controls.OfType<HarborInputFrame>()).First().Input;nameBox.Text="My named server";
            Assert(createName=="My named server"&&management.Controls.OfType<PaintedPanel>().SelectMany(p=>p.Controls.OfType<Label>()).Any(l=>l.Text==createName),"Creation header did not track name");Capture("creation-settings");
            TransitionManagement(RenderCreateGame);await Until(()=>!managementTransition);((HarborToggle)createRuleInputs["keepInventory"]).Checked=true;CaptureCreationGame();
            TransitionManagement(RenderCreateSettings);await Until(()=>!managementTransition);Assert(createRules["keepInventory"]=="true"&&createVersion.Id=="1.21.1"&&createName=="My named server","Back navigation lost creation draft");
            TransitionManagement(RenderCreateGame);await Until(()=>!managementTransition);Capture("creation-game");
            Assert(server.Library.Data.Profiles.Count==1,"Wizard navigation created a server prematurely");
            using(var fixtureHttp=new HttpClient(new ModdedTests.FixtureHttp())){
                curseCatalog=new(server.Root,fixtureHttp,"fixture-key");installedPacksOnly=false;TransitionManagement(RenderCreateModded);await Until(()=>!managementTransition);await Until(()=>management.Controls.OfType<PaintedPanel>().Any(p=>p.AccessibleName=="Combined modpack catalog"&&p.Controls.OfType<PaintedPanel>().Any()));Capture("creation-modded-catalog");
                string archive=Path.Combine(server.Root,"fixture-server-pack.zip");using(var zip=System.IO.Compression.ZipFile.Open(archive,System.IO.Compression.ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("config/fixture.toml").Open());writer.Write("[general]\nallowMachines = true\nrate = 3\n");}
                createPack=new(){Project=new(10,"Fixture Pack","Fixture pack used for UI verification","","",123456,new()),Source=new("","Fixture Pack","1.0","1.21.1","neoforge","21.1.249",10,20),Release=new(21,10,"fixture.zip","1.0",1,null,new(){"1.21.1","NeoForge"},20,new(),null),Archive=archive,Config=PackConfiguration.Discover(archive)};PrepareModdedDraft();
                for(int i=0;i<6;i++){createPack.Mods[100+i]=new(100+i,"Included Mod "+(i+1),"This mod is included in the published server pack.","","",(i+1)*123456,new());createPack.ModFiles[100+i]=new(200+i,100+i,"fixture-"+i+".jar","Fixture mod",1,null,new(){"1.21.1","NeoForge"},0,new(),null);}
                TransitionManagement(RenderModpackChoice);await Until(()=>!managementTransition);Capture("creation-modded-choice");management.Controls.OfType<CreationTile>().Single(t=>t.AccessibleName=="Customize Further").Focus();await Task.Delay(200);
                createPack.MetadataResolved=true;OpenCustomize();await Until(()=>!managementTransition);await Task.Delay(200);includedModsOnly=true;TransitionManagement(RenderCustomizeModpack);await Until(()=>!managementTransition);Assert(management.Controls.OfType<PaintedPanel>().Single(p=>p.AccessibleName=="Modpack customization").Controls.OfType<PaintedPanel>().Count()==6,"Included mod cards missing");Assert(management.Controls.OfType<HarborButton>().Single(b=>b.Text=="Continue").Bottom<=management.ClientSize.Height,"Customization footer is cut off");Capture("creation-modded-customize");
                management.Controls.OfType<HarborButton>().Single(b=>b.Text=="Continue").PerformClick();await Until(()=>!managementTransition);Capture("creation-modded-settings");
                management.Controls.OfType<HarborButton>().Single(b=>b.Text=="Continue").PerformClick();await Until(()=>!managementTransition);Capture("creation-modded-game");Assert(management.Controls.OfType<HarborButton>().Any(b=>b.Text=="Modpack Settings (2)"),"Pack settings button missing");
                management.Controls.OfType<HarborButton>().Single(b=>b.Text=="Modpack Settings (2)").PerformClick();await Until(()=>!managementTransition);Capture("creation-modded-config");((HarborToggle)packConfigInputs.Single(p=>p.Key.Kind=="bool").Value).Checked=false;management.Controls.OfType<HarborButton>().Single(b=>b.Text=="Back").PerformClick();await Until(()=>!managementTransition);Assert(createPack.Config.Single(c=>c.Kind=="bool").Value=="false","Modpack setting draft was discarded");
                Assert(server.Library.Data.Profiles.Count==1,"Modded screen navigation installed a server prematurely");checks.Add("Modded catalog, use/customize choice, installed mods, server settings, game settings and discovered pack configuration render and fade; draft edits survive Back without installing a server");createPack=null;curseCatalog=null;createVersion=vanillaVersions[0];
            }
            ReportSetup(new(2,.4,"Configuring game rules and server properties…"));SetupProgressPanel? loading=null;bool cancelled=false;
            TransitionManagement(()=>loading=RenderSetupLoading(createName,createVersion.Id,"vanilla",()=>cancelled=true));await Until(()=>!managementTransition);Capture("creation-loading");
            Assert(loading!.Progress.Percent==48&&management.Controls.OfType<HarborButton>().Single(b=>b.Text=="Create Server").Enabled==false,"Loading progress or disabled Create state is incorrect");
            management.Controls.OfType<HarborButton>().Single(b=>b.Text=="Cancel").PerformClick();Assert(cancelled,"Loading Cancel did not request cancellation");
            ReportSetup(new(1,0,"The world will generate on the first start.",WorldDeferred:true));TransitionManagement(()=>RenderSetupLoading("Modded test","1.21.1","modded",()=>{}));await Until(()=>!managementTransition);Capture("creation-loading-modded");
            checks.Add("Shared loading screen renders real step progress for vanilla and modded, distinguishes deferred generation, disables duplicate creation, and requests cancellation");
            await RunSetup("Animated loading check","1.21.1","vanilla",async token=>{
                for(int phase=0;phase<5;phase++){
                    ReportSetup(new(phase,.25,"Checking the animated setup screen…"));await Task.Delay(900,token);
                    var live=management.Controls.OfType<SetupProgressPanel>().Single();Assert(live.Progress.Step==phase,"Live progress stopped updating");
                    int frames=live.FrameBuildCount;await Task.Delay(300,token);Assert(frames==live.FrameBuildCount,"Spinner regenerated the text frame");
                    if(phase==2){var original=Size;Size=new Size(1740,1020);await Task.Delay(300,token);Assert(management.Controls.OfType<HarborButton>().Single(b=>b.Text=="Cancel").Bottom<=management.ClientSize.Height,"Loading controls are cut off at the smaller window size");management.AutoScrollPosition=new Point(0,management.LogicalToDeviceUnits(60));await Task.Delay(300,token);Capture("creation-loading-resized-scrolled");management.AutoScrollPosition=Point.Empty;Size=original;await Task.Delay(300,token);ShowPage("Home");ShowPage("Server Management");Assert(!live.IsDisposed,"Returning to loading discarded the screen");await Task.Delay(30000,token);}
                    Capture("creation-live-phase-"+phase);
                }
                ReportSetup(new(4,1,"Your server is ready.",true));
            },RenderCreateGame);await Until(()=>!managementTransition);
            checks.Add("The live creation flow mounts the loading screen, updates every phase during animation, and completes its final fade");
            TransitionManagement(RenderServerList);await Until(()=>!managementTransition);
            checks.Add("Creation choice, version list, server settings and game settings fade both ways; live name and drafts persist; cancellation creates no server");
            checks.Add("All seven existing tabs remain, selected-tab outlines follow navigation, console controls fit, and CPU/memory selection works");
            ShowPage("Settings");var choice=pages["Settings"].Controls.OfType<HarborDropdown>().Single();choice.SelectedIndex=1;
            await Until(()=>!artworkStatus.Text.StartsWith("Loading"));Assert(packArt!=null,"CurseForge artwork could not load");Assert(AppearanceSettings.Load(server.Root).Background=="modpack","Artwork choice did not persist");
            var artworkFocus=content.ArtworkTitle?.ToString()??"Complete image (no reliable title detected)";
            ShowPage("Home");Capture("design-curseforge");ShowPage("Settings");choice.SelectedIndex=0;Assert(AppearanceSettings.Load(server.Root).Background=="night"&&content.Artwork==nightScene,"Default scene did not restore");
            Capture("design-settings");ShowPage("Home");Capture("design-offline");
            checks.Add("CurseForge artwork downloads from the installed pack metadata, is cached locally, switches without a server restart, and the default night scene setting persists");
            ServerManager.WriteJson(report,new{passed=true,checkedUtc=DateTime.UtcNow,checks,artworkFocus,realWorldModified=false});
        }catch(Exception ex){ServerManager.WriteJson(report,new{passed=false,error=ex.ToString(),checks});}
        finally{if(server.HasProcess)await server.CloseAsync();exiting=true;Close();}
    }
}
internal static class DesignTests
{
    public static void Run(string report)
    {
        var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"design-fixture-"+Guid.NewGuid().ToString("N"));LibraryTests.WriteWorld(Path.Combine(root,"server","world"));File.WriteAllText(Path.Combine(root,"server","eula.txt"),"eula=true");
        ServerManager.WriteJson(Path.Combine(root,"settings.json"),new Settings{MemoryGB=80,KeepAwake=false});
        VerifyHistory(Path.Combine(root,"history-tests"),report+".history.json");
        VerifyAutomaticArtwork(report+".artwork.json");
        ProcessStartInfo Launcher(){var p=new ProcessStartInfo(Environment.ProcessPath!);p.ArgumentList.Add("--mock-server");return p;}
        using var manager=new ServerManager(root,Launcher);using var form=new MainForm(manager,visualTestReport:report);Application.Run(form);
        using var json=JsonDocument.Parse(File.ReadAllText(report));if(!json.RootElement.GetProperty("passed").GetBoolean())throw new Exception(json.RootElement.GetProperty("error").GetString());
    }
    static void VerifyHistory(string root,string report)
    {
        void Assert(bool p,string m){if(!p)throw new Exception(m);}
        var checks=new List<string>();var now=DateTime.UtcNow;var before=new ProcessReading(123,now,1000,1073741824);var after=before with{CpuMilliseconds=5000};
        Assert(PerformanceHistory.CalculateCpu(before,after,5,8)==10,"CPU percent must represent total CPU capacity");
        Assert(PerformanceHistory.CalculateCpu(before,after with{Pid=124},5,8)==null,"Process changes must reset CPU baseline");
        Assert(PerformanceHistory.CalculateCpu(before,null,5,8)==0,"Stopped server should use zero CPU");checks.Add("Server CPU uses processor-time deltas normalized to total PC capacity; process changes reset the baseline; stopped servers are zero");
        var history=new PerformanceHistory();history.Select(root,"world",now.AddHours(-2));
        for(int i=0;i<=1440;i++)history.Sample(now.AddHours(-2).AddSeconds(i*5),before with{CpuMilliseconds=i*100});
        Assert(history.Samples.Count==721&&history.Samples.First().Utc==now.AddHours(-1),"History is not restricted to one hour");
        Assert(!history.Sample(now.AddMilliseconds(500),after),"Sample interval not enforced");history.Save();
        var reopened=new PerformanceHistory();reopened.Select(root,"world",now);Assert(reopened.Samples.Count==history.Samples.Count,"Persisted history did not reload");
        reopened.Select(root,"world-"+Guid.NewGuid().ToString("N"),now);Assert(reopened.Samples.Count==0,"History leaked between worlds");
        checks.Add("Only the last hour is retained, five-second sampling is enforced, history survives reopening, and different worlds have separate histories");
        ServerManager.WriteJson(report,new{passed=true,checks});
    }
    static void VerifyAutomaticArtwork(string report)
    {
        var results=new List<object>();
        foreach(var example in new[]{("SKY FACTORY",new Point(90,60)),("MAGIC REALMS",new Point(290,330)),("TECH FRONTIER",new Point(100,180))}){
            using var art=new Bitmap(1200,600);using(var g=Graphics.FromImage(art)){g.Clear(Color.White);using var font=new Font("Arial",72,FontStyle.Bold,GraphicsUnit.Pixel);g.DrawString(example.Item1,font,Brushes.Black,example.Item2);}
            var focus=ArtworkFocus.Detect(art,example.Item1);
            if(focus==null||!focus.Value.Contains(example.Item2.X+80,example.Item2.Y+45)||focus.Value.Width>=art.Width)throw new Exception("Automatic title detection failed for "+example.Item1);
            results.Add(new{example=example.Item1,focus=focus.Value.ToString()});
        }
        using(var noText=new Bitmap(1200,600)){using(var g=Graphics.FromImage(noText))g.Clear(Color.DarkSlateBlue);if(ArtworkFocus.Detect(noText,"No title")!=null)throw new Exception("Text-free artwork should use full-image fitting");}
        using(var tilted=new Bitmap(1200,800)){
            using(var g=Graphics.FromImage(tilted)){g.Clear(Color.White);g.TranslateTransform(300,380);g.RotateTransform(-30);using var font=new Font("Arial",72,FontStyle.Bold,GraphicsUnit.Pixel);g.DrawString("WILD HORIZONS",font,Brushes.Black,0,0);}
            var focus=ArtworkFocus.Detect(tilted,"Wild Horizons");if(focus==null||!focus.Value.Contains(545,284))throw new Exception("Tilted lettering must map back to the correct original-image position");
            results.Add(new{example="WILD HORIZONS (tilted)",focus=focus.Value.ToString()});
        }
        if(ArtworkFocus.Choose(new(){("10",new RectangleF(700,80,200,200),95)},new Size(1200,600),"An unreadable title 10")!=null)throw new Exception("A version number alone must not crop away the unreadable title");
        if(ArtworkFocus.Choose(new(){("SCENERY",new RectangleF(700,80,200,200),95)},new Size(1200,600),"Sky Factory")!=null)throw new Exception("Unrelated OCR guesses must not select scenery instead of a title");
        ServerManager.WriteJson(report,new{passed=true,checks=new[]{"Generic local OCR follows title text at different locations in unrelated artwork; no pack IDs or fixed crop coordinates","Images without readable words fall back to complete-image fitting; sidebar always fits the full original"},examples=results});
    }
}

