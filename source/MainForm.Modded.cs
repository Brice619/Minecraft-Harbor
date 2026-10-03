using System.IO.Compression;
using System.Text.Json;
namespace MinecraftHarbor;
public sealed partial class MainForm
{
    CurseForgeCatalog? curseCatalog;
    CurseForgeCatalog Catalog=>curseCatalog??=new(server.Root);
    ModpackDraft? createPack;
    bool customizePack,installedPacksOnly;
    string packQuery="",modQuery="";
    int packSort=2,packPage,modSort=2,modPage;
    bool includedModsOnly;
    bool addingMod,selectingInstalledPack;
    string? selectedConfigFile;
    readonly Dictionary<PackConfigValue,Control> packConfigInputs=new();
    sealed class PageCancellation:IDisposable
    {
        readonly Control owner;readonly CancellationTokenSource source;readonly EventHandler disposed;
        internal CancellationToken Token=>source.Token;
        internal PageCancellation(Control owner,TimeSpan timeout){this.owner=owner;source=new(timeout);disposed=(_,_)=>source.Cancel();owner.Disposed+=disposed;}
        public void Dispose(){owner.Disposed-=disposed;source.Dispose();}
    }
    sealed record CatalogSort(string Name,int Id){public override string ToString()=>Name;}
    HarborDropdown CatalogSorting(int value,Action<int> selected)
    {
        var menu=new HarborDropdown();foreach(var option in new[]{new CatalogSort("Popular",2),new CatalogSort("Most Downloaded",6),new CatalogSort("Recently Updated",3),new CatalogSort("Newest",11),new CatalogSort("Name",4)})menu.Items.Add(option);
        menu.SelectedItem=menu.Items.Cast<CatalogSort>().First(s=>s.Id==value);menu.SelectedIndexChanged+=(_,_)=>selected(((CatalogSort)menu.SelectedItem!).Id);return menu;
    }
    void ModpackIdentity(string name,string detail,int y=155,string? image=null)
    {
        var card=MCard(y,65);IdentityArtwork(card,image,createPack?.Project);
        MLabel(card,name,85,8,MWidth-105,30,15,true);MLabel(card,detail,85,37,MWidth-105,24,11);
    }
    void IdentityArtwork(PaintedPanel card,string? image,CfProject? project)
    {
        card.Draw=(g,w,h)=>{HarborTheme.Card(g,new RectangleF(0,0,w,h));if(string.IsNullOrEmpty(image))HarborTheme.Icon(g,"cube",23,17,31,Ink);};
        if(!string.IsNullOrEmpty(image)){
            var art=new PictureBox{SizeMode=PictureBoxSizeMode.Zoom,AccessibleName=(project?.Name??"Modpack")+" artwork"};art.Disposed+=(_,_)=>art.Image?.Dispose();card.Controls.Add(art);MBounds(art,14,8,60,49);
            art.Paint+=(_,e)=>{if(art.Image==null)HarborTheme.Icon(e.Graphics,"cube",15,9,31,Ink);};
            if(CatalogArtwork.Valid(image)){using var source=Image.FromFile(image);art.Image=new Bitmap(source);}
            else if(project is {Logo.Length:>0})_=LoadCatalogArtwork(art,project);
        }
    }
    string PackLogo=>createPack==null?"":Path.Combine(server.Root,"artwork","curseforge",createPack.Project.Id+".png");
    void RenderCreateModded()
    {
        CreationHeading("Choose a Modpack","Browse Modrinth, import a CurseForge server pack, or choose an installed pack.","Create New Server",()=>TransitionManagement(RenderCreateChoice));
        ModpackIdentity("Modded Server",installedPacksOnly?"Installed CurseForge packs":Catalog.Connected?"Modrinth + CurseForge":"Modrinth + CurseForge server ZIPs");
        var search=new TextBox{Text=packQuery,PlaceholderText="Search modpacks…"};management.Controls.Add(new HarborInputFrame(search));var frame=search.Parent!;MBounds(frame,0,234,MWidth-490,40);
        search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){packQuery=search.Text;packPage=0;TransitionManagement(RenderCreateModded);e.SuppressKeyPress=true;}};
        var sort=CatalogSorting(packSort,n=>{packSort=n;packQuery=search.Text;packPage=0;TransitionManagement(RenderCreateModded);});management.Controls.Add(sort);MBounds(sort,MWidth-475,234,180,40);
        MButton(management,"Search",MWidth-282,234,110,()=>{packQuery=search.Text;packPage=0;TransitionManagement(RenderCreateModded);});
        MButton(management,Catalog.Connected?"Connection":"Connect CurseForge",MWidth-161,234,161,RenderCurseForgeConnection);
        MButton(management,installedPacksOnly?"Online Catalog":"Installed Packs",0,286,160,()=>{installedPacksOnly=!installedPacksOnly;packPage=0;TransitionManagement(RenderCreateModded);});
        MButton(management,"Import Server ZIP",173,286,183,ImportServerZip);
        MButton(management,"Browse CurseForge",MWidth-174,286,174,()=>OpenPath("https://www.curseforge.com/minecraft/search?class=modpacks&search="+Uri.EscapeDataString(search.Text)));
        var list=new PaintedPanel{AccessibleName="Combined modpack catalog",AutoScroll=true,SmoothScrolling=true};management.Controls.Add(list);MBounds(list,0,345,MWidth,Math.Max(130,management.ClientSize.Height*96/management.DeviceDpi-430));
        if(installedPacksOnly)RenderInstalledPackCards(list);
        else{MLabel(list,"Loading modpacks…",20,16,MWidth-40,40,15);_=LoadPackCards(list);}
    }
    void RenderInstalledPackCards(Control list)
    {
        var packs=CurseForgeProfiles.Discover(server.Library.Data.CurseForgeRoots).Where(p=>p.Name.Contains(packQuery,StringComparison.OrdinalIgnoreCase)).ToList();
        if(packSort==4)packs=packs.OrderBy(p=>p.Name).ToList();int width=(MWidth-32)/2;
        for(int i=0;i<packs.Count;i++){var pack=packs[i];var project=new CfProject(pack.ProjectId,pack.Name,"Installed CurseForge profile",pack.Logo,pack.Website,0,new());var card=CatalogCard(list,project,i%2*(width+14),i/2*124,width,"Minecraft "+pack.MinecraftVersion+" · "+pack.Loader,"Select",()=>OpenInstalledPack(pack));}
        ((PaintedPanel)list).AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(Math.Max(65,(packs.Count+1)/2*124)));
        if(packs.Count==0)MLabel(list,"No installed packs match your search.",20,15,MWidth-40,45,15);CatalogFooter(list.Bottom*96/management.DeviceDpi+14,false,0,0,()=>{});
    }
    PaintedPanel CatalogCard(Control parent,CfProject project,int x,int y,int width,string metadata,string action,Action click,string? image=null)
    {
        var card=new PaintedPanel{AccessibleName=project.Name};card.Draw=(g,w,h)=>HarborTheme.Card(g,new RectangleF(0,0,w,h));parent.Controls.Add(card);MBounds(card,x,y,width,113);
        var icon=new PictureBox{SizeMode=PictureBoxSizeMode.Zoom,AccessibleName=project.Name+" artwork"};card.Controls.Add(icon);MBounds(icon,8,8,91,97);icon.Disposed+=(_,_)=>icon.Image?.Dispose();
        icon.Paint+=(_,e)=>{if(icon.Image==null)HarborTheme.Icon(e.Graphics,"cube",24,28,40,Muted);};
        if(image!=null&&File.Exists(image)){using var source=Image.FromFile(image);icon.Image=new Bitmap(source);}else if(!string.IsNullOrEmpty(project.Logo))_=LoadCatalogArtwork(icon,project);
        int textWidth=width-230;MLabel(card,project.Name,112,10,textWidth,28,12,true);MLabel(card,project.Summary,112,40,textWidth,38,10);MLabel(card,metadata,112,83,textWidth,25,9).ForeColor=Muted;
        MButton(card,action,width-104,34,94,click,action=="Add");return card;
    }
    async Task LoadCatalogArtwork(PictureBox icon,CfProject project)
    {
        try{using var cancel=new PageCancellation(icon,TimeSpan.FromSeconds(45));string path="";for(int attempt=0;attempt<2;attempt++){try{path=await Catalog.Artwork(project,cancel.Token);break;}catch(Exception ex)when(attempt==0&&ex is HttpRequestException or IOException){await Task.Delay(450,cancel.Token);}}if(icon.IsDisposed||path.Length==0)return;using var source=Image.FromFile(path);icon.Image=new Bitmap(source);icon.AccessibleName=project.Name+" artwork";}
        catch(OperationCanceledException){}
        catch(Exception ex)when(ex is HttpRequestException or IOException or InvalidDataException or ArgumentException){if(!icon.IsDisposed){icon.AccessibleName=project.Name+" artwork unavailable";icon.Invalidate();server.Log("Artwork for "+project.Name+" could not load: "+ex.Message);}}
    }
    async Task LoadPackCards(Control list)
    {
        using var cancel=new PageCancellation(list,TimeSpan.FromSeconds(45));
        try{var page=await Catalog.CombinedSearch(packQuery,packSort,packPage,false,cancel.Token);if(list.IsDisposed)return;foreach(var c in list.Controls.Cast<Control>().Where(c=>c is not ScrollBar).ToArray())c.Dispose();int width=(MWidth-32)/2;
            for(int i=0;i<page.Items.Count;i++){var project=page.Items[i];CatalogCard(list,project,i%2*(width+14),i/2*124,width,$"{project.Source} · {project.Downloads:N0} downloads","Select",()=>OpenCatalogPack(project));}
            ((PaintedPanel)list).AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(Math.Max(70,(page.Items.Count+1)/2*124)));if(page.Items.Count==0)MLabel(list,"No modpacks match your search.",20,20,MWidth-40,40,15);CatalogFooter(list.Bottom*96/management.DeviceDpi+14,true,packPage,page.Total,()=>TransitionManagement(RenderCreateModded),pagingTotal:page.PagingTotal);
        }catch(Exception ex){if(!list.IsDisposed){list.Controls.OfType<Label>().First().Text=ex is OperationCanceledException?"The catalog took too long to respond. Try again.":ex.Message;MButton(list,"Try Again",20,70,150,()=>TransitionManagement(RenderCreateModded));MButton(management,"Cancel",MWidth-135,list.Bottom*96/management.DeviceDpi+14,135,()=>TransitionManagement(RenderServerList));}}
    }
    void CatalogFooter(int y,bool paging,int page,int total,Action refresh,bool mods=false,int pagingTotal=0)
    {
        if(paging){var previous=MButton(management,"Previous",0,y,125,()=>{if(mods)modPage--;else packPage--;refresh();});previous.Enabled=page>0;MLabel(management,$"Page {page+1} · {total:N0} results",142,y,MWidth-425,42,10);var next=MButton(management,"Next",MWidth-275,y,125,()=>{if(mods)modPage++;else packPage++;refresh();});next.Enabled=(page+1)*20<(pagingTotal>0?pagingTotal:total);}
        MButton(management,"Cancel",MWidth-135,y,135,()=>TransitionManagement(RenderServerList));management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(y+60));
    }
    void RenderCurseForgeConnection()
    {
        TransitionManagement(()=>{
            CreationHeading("Connect CurseForge","Enter the approved developer API key for Minecraft Harbor.","Choose a Modpack",()=>TransitionManagement(RenderCreateModded));var card=MCard(170,240);
            MLabel(card,Catalog.Connected?"CurseForge connection saved":"CurseForge developer API key",25,18,MWidth-50,34,18,true);
            var key=new TextBox{UseSystemPasswordChar=true,PlaceholderText="Paste your developer API key",MaxLength=512};var input=new HarborInputFrame(key);card.Controls.Add(input);MBounds(input,25,70,MWidth-50,40);
            MLabel(card,"Stored encrypted for your Windows account. This key is never included in server files.",25,123,MWidth-50,35,11);
            var status=MLabel(card,"",25,163,MWidth-290,55,11);
            var save=MButton(card,"Connect",MWidth-200,177,175,()=>{} ,true);save.Click+=async(_,_)=>{save.Enabled=false;try{using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(40));await Catalog.Connect(key.Text.Trim(),cancel.Token);key.Clear();installedPacksOnly=false;TransitionManagement(RenderCreateModded);}catch(Exception ex){status.Text=ex is OperationCanceledException?"Connection timed out.":ex.Message;}finally{if(!save.IsDisposed)save.Enabled=true;}};
            MButton(management,"Apply for Access",0,429,190,()=>OpenPath("https://support.curseforge.com/support/solutions/articles/9000208346"));MButton(management,"Back",MWidth-140,429,140,()=>TransitionManagement(RenderCreateModded));
        });
    }
    void OpenCatalogPack(CfProject project)
    {
        TransitionManagement(()=>{
            CreationHeading(project.Name,"Choose a published release with server files.","Choose a Modpack",()=>TransitionManagement(RenderCreateModded));ModpackIdentity("Selected Modpack",project.Name+" · "+project.Source,155);
            var status=MLabel(management,"Loading releases…",20,235,MWidth-40,40,15);_=LoadPackReleases(project,status);
        });
    }
    async Task LoadPackReleases(CfProject project,Label status)
    {
        using var cancel=new PageCancellation(status,TimeSpan.FromSeconds(45));try{var releases=project.ModrinthId.Length>0?await Catalog.Modrinth.Files(project.ModrinthId,cancel.Token):await Catalog.Files(project.Id,cancel.Token);if(status.IsDisposed)return;status.Text="Select a release";int y=281;
            foreach(var release in releases){var card=MCard(y,74);MLabel(card,release.DisplayName,18,9,MWidth-215,28,13,true);MLabel(card,string.Join(" · ",release.Versions),18,39,MWidth-215,25,10);var choose=MButton(card,release.ServerPack>0?"Select":"No Server Pack",MWidth-180,15,160,()=>InspectCatalogPack(project,release));choose.Enabled=release.ServerPack>0;y+=86;}
            if(project.Website.Length>0){MButton(management,"Open "+project.Source,MWidth-180,y,180,()=>OpenPath(project.Website+"/versions"));y+=55;}
            if(releases.Count==0)status.Text="No releases are available.";management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(y+30));
        }catch(Exception ex){if(!status.IsDisposed)status.Text=ex is OperationCanceledException?"CurseForge took too long to respond.":ex.Message;}
    }
    void InspectCatalogPack(CfProject project,CfFile release)
    {
        TransitionManagement(()=>{CreationHeading("Preparing Modpack","Reading the pack's manifest and server files.","Choose a Modpack",()=>TransitionManagement(RenderCreateModded));ModpackIdentity(project.Name,release.DisplayName,155);var status=MLabel(management,"Finding server files…",20,243,MWidth-40,65,15);_=InspectForPage(project,release,status);});
    }
    async Task InspectForPage(CfProject project,CfFile release,Label status)
    {
        using var cancel=new PageCancellation(status,TimeSpan.FromMinutes(30));MButton(management,"Cancel",MWidth-150,335,150,()=>TransitionManagement(RenderCreateModded));
        try{var draft=await Catalog.Inspect(project,release,cancel.Token,s=>{if(!status.IsDisposed)status.Text=s;});if(status.IsDisposed)return;createPack=draft;PrepareModdedDraft();while(managementTransition)await Task.Delay(30);TransitionManagement(RenderModpackChoice);}
        catch(Exception ex){if(!status.IsDisposed){status.Text=ex is OperationCanceledException?"Download cancelled.":ex.Message;MButton(management,"Try Again",20,335,150,()=>InspectCatalogPack(project,release));}}
    }
    async void OpenInstalledPack(CurseForgeProfile pack)
    {
        if(selectingInstalledPack)return;selectingInstalledPack=true;
        try{
            var prepared=InstalledPackSelection.FindPrepared(server.Root,server.Library,pack);
            string? archive=prepared?.Directory??InstalledPackSelection.FindArchive(server.Root,pack);
            if(archive==null&&Catalog.Connected&&pack.ProjectId>0&&pack.ServerFileId>0){
                TransitionManagement(()=>{CreationHeading("Preparing Modpack","Finding this pack's matching server files…","Choose a Modpack",()=>TransitionManagement(RenderCreateModded));var status=MLabel(management,"Finding server files…",20,180,MWidth-40,60,15);_=DownloadInstalledPack(pack,status);});return;
            }
            if(archive==null){using var picker=new OpenFileDialog{Title="Choose the matching server ZIP for "+pack.Name,Filter="Server packs (*.zip)|*.zip"};if(picker.ShowDialog(this)!=DialogResult.OK)return;archive=picker.FileName;}
            createPack=await Task.Run(()=>ReadInstalledPack(pack,archive,prepared));
            PrepareModdedDraft();while(managementTransition)await Task.Delay(30);TransitionManagement(RenderModpackChoice);
        }catch(Exception ex){MessageBox.Show(this,ex.Message,"Minecraft Harbor");}finally{selectingInstalledPack=false;}
    }
    async Task DownloadInstalledPack(CurseForgeProfile pack,Label status)
    {
        using var cancel=new PageCancellation(status,TimeSpan.FromMinutes(30));
        MButton(management,"Cancel",MWidth-150,275,150,()=>TransitionManagement(RenderCreateModded));
        try{
            string archive=await PackInstaller.DownloadServerPack(server.Root,pack,s=>{if(!status.IsDisposed)status.Text=s;},cancel.Token);
            if(status.IsDisposed)return;var draft=await Task.Run(()=>ReadInstalledPack(pack,archive,null),cancel.Token);if(status.IsDisposed)return;createPack=draft;
            PrepareModdedDraft();while(managementTransition)await Task.Delay(30);TransitionManagement(RenderModpackChoice);
        }catch(Exception ex){if(!status.IsDisposed){status.Text=ex is OperationCanceledException?"Download cancelled.":ex.Message;MButton(management,"Choose Server ZIP",20,275,190,ImportServerZip);}}
    }
    ModpackDraft ReadInstalledPack(CurseForgeProfile pack,string archive,PreparedPackSource? prepared)
    {
        var project=new CfProject(pack.ProjectId,pack.Name,"Installed CurseForge profile",pack.Logo,pack.Website,0,new());
        var draft=new ModpackDraft{Project=project,Source=pack,Archive=archive,Prepared=prepared,Release=new(pack.ServerFileId,pack.ProjectId,prepared==null?Path.GetFileName(archive):pack.Name,pack.PackVersion,prepared==null?new FileInfo(archive).Length:0,null,new(){pack.MinecraftVersion,pack.Loader},pack.ServerFileId,new(),null),Config=PackConfiguration.Discover(archive)};
        int id=-1;
        void Mod(string filename,long length){string name=Path.GetFileNameWithoutExtension(filename);draft.Mods[id]=new(id,name,"Included in the server pack","","",0,new());draft.ModFiles[id]=new(id,id,filename,name,length,null,new(){pack.MinecraftVersion,pack.Loader},0,new(),null);id--;}
        if(prepared!=null){foreach(string file in Directory.EnumerateFiles(Path.Combine(archive,"mods"),"*.jar"))Mod(Path.GetFileName(file),new FileInfo(file).Length);}
        else{using var zip=ZipFile.OpenRead(archive);foreach(var entry in zip.Entries.Where(e=>e.FullName.Replace('\\','/').Contains("mods/")&&e.Name.EndsWith(".jar",StringComparison.OrdinalIgnoreCase)))Mod(entry.Name,entry.Length);}
        return draft;
    }
    void ImportServerZip()
    {
        using var picker=new OpenFileDialog{Title="Choose a downloaded CurseForge server pack",Filter="Server packs (*.zip)|*.zip"};if(picker.ShowDialog(this)!=DialogResult.OK)return;
        string archive=picker.FileName;var detected=PackMetadata.Detect(archive);
        TransitionManagement(()=>{
            CreationHeading("Import Server Pack","Confirm the pack name, Minecraft version and loader.","Choose a Modpack",()=>TransitionManagement(RenderCreateModded));var card=MCard(170,260);
            var name=new TextBox{Text=Path.GetFileNameWithoutExtension(archive),MaxLength=90};var minecraft=new TextBox{Text=detected.Minecraft,MaxLength=20};var loader=new HarborDropdown();foreach(var item in new[]{"NeoForge","Forge","Fabric"})loader.Items.Add(item);loader.SelectedItem=loader.Items.Cast<string>().First(l=>l.Equals(detected.Loader,StringComparison.OrdinalIgnoreCase));var version=new TextBox{Text=detected.Version,MaxLength=40};
            void Row(string label,Control input,int y){MLabel(card,label,25,y,240,39,12,true);card.Controls.Add(input);MBounds(input,275,y,MWidth-300,39);}Row("Modpack Name",new HarborInputFrame(name),20);Row("Minecraft Version",new HarborInputFrame(minecraft),78);Row("Mod Loader",loader,136);Row("Loader Version",new HarborInputFrame(version),194);
            MButton(management,"Cancel",MWidth-295,450,130,()=>TransitionManagement(RenderCreateModded));MButton(management,"Continue",MWidth-153,450,153,()=>{
                if(string.IsNullOrWhiteSpace(name.Text)||!Version.TryParse(minecraft.Text,out _)||!System.Text.RegularExpressions.Regex.IsMatch(version.Text,@"^[a-zA-Z0-9.\-+_]+$"))throw new ArgumentException("Enter the pack's Minecraft version and exact loader version.");
                var pack=new CurseForgeProfile("",name.Text,Path.GetFileNameWithoutExtension(archive),minecraft.Text,loader.SelectedItem!.ToString()!.ToLowerInvariant(),version.Text,0,0);
                createPack=new(){Project=new(0,name.Text,"Imported server pack","","",0,new()),Source=pack,Archive=archive,Release=new(0,0,Path.GetFileName(archive),pack.PackVersion,new FileInfo(archive).Length,null,new(){pack.MinecraftVersion,pack.Loader},0,new(),null),Config=PackConfiguration.Discover(archive)};
                using var zip=ZipFile.OpenRead(archive);int id=-1;foreach(var entry in zip.Entries.Where(e=>e.FullName.Replace('\\','/').Contains("mods/")&&e.Name.EndsWith(".jar",StringComparison.OrdinalIgnoreCase))){string title=Path.GetFileNameWithoutExtension(entry.Name);createPack.Mods[id]=new(id,title,"Included in the server pack","","",0,new());createPack.ModFiles[id]=new(id,id,entry.Name,title,entry.Length,null,new(){pack.MinecraftVersion,pack.Loader},0,new(),null);id--;}
                PrepareModdedDraft();TransitionManagement(RenderModpackChoice);
            },true);
        });
    }
    void PrepareModdedDraft()
    {
        customizePack=false;selectedConfigFile=null;createName="My "+createPack!.Source.Name+" Server";createVersion=new(createPack.Source.MinecraftVersion,"",DateTime.MinValue);createSettings=new(){MemoryGB=8,MaxPlayers=20,ViewDistance=10,SimulationDistance=8,WorldName="world",WorldFolder="world"};createAllowCheats=false;SetCreationDefaults();
    }
    void SetCreationDefaults()
    {
        createProperties=VanillaCatalog.Properties(createVersion!);createRules=VanillaCatalog.Rules(createVersion!);if(createPack==null)return;
        string defaults="";
        if(createPack.Prepared!=null){string file=Path.Combine(createPack.Prepared.Directory,"server.properties");if(File.Exists(file)&&new FileInfo(file).Length<1048576)defaults=File.ReadAllText(file);}
        else{using var zip=ZipFile.OpenRead(createPack.Archive);var entry=zip.Entries.FirstOrDefault(e=>e.Name=="server.properties");if(entry is {Length:<1048576}){using var reader=new StreamReader(entry.Open());defaults=reader.ReadToEnd();}}
        foreach(var line in defaults.Split('\n')){int i=line.IndexOf('=');if(i>0&&createProperties.ContainsKey(line[..i]))createProperties[line[..i]]=line[(i+1)..].TrimEnd('\r');}
        foreach(var key in new[]{"difficulty","gamemode"})if(int.TryParse(createProperties[key],out var n)&&n>=0&&n<4)createProperties[key]=(key=="difficulty"?new[]{"peaceful","easy","normal","hard"}:new[]{"survival","creative","adventure","spectator"})[n];
        if(createPack.World!=null)foreach(var rule in PackWorlds.Rules(createPack.World.Path))if(createRules.ContainsKey(rule.Key))createRules[rule.Key]=rule.Value;
    }
    void RenderModpackChoice()
    {
        var pack=createPack!;CreationHeading(pack.Source.Name,"How would you like to use this modpack?","Choose a Modpack",()=>TransitionManagement(RenderCreateModded));ModpackIdentity("Selected Modpack",pack.Source.Name+" · "+pack.Source.PackVersion,155,PackLogo);
        int width=(MWidth-18)/2,height=Math.Clamp(management.ClientSize.Height*96/management.DeviceDpi-309,245,450);
        var asIs=new CreationTile("Use As Is",pack.Prepared==null?"Use the published server pack with its original mods.":"Use this pack with its existing server mods and configuration.",Path.Combine(server.Root,"artwork","create","modded.png"));management.Controls.Add(asIs);MBounds(asIs,0,234,width,height);asIs.Click+=(_,_)=>{customizePack=false;pack.Removed.Clear();pack.Added.Clear();pack.LocalMods.Clear();foreach(var config in pack.Config)config.Value=config.Original;TransitionManagement(RenderCreateSettings);};
        var customize=new CreationTile("Customize Further","Adjust the included mods and add more before setup.",Path.Combine(server.Root,"artwork","create","customize.png"),"settings");management.Controls.Add(customize);MBounds(customize,width+18,234,width,height);customize.Click+=(_,_)=>OpenCustomize();
        MButton(management,"Cancel",MWidth-140,247+height,140,()=>TransitionManagement(RenderServerList));management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(height+309));
    }
    void OpenCustomize()
    {
        customizePack=true;includedModsOnly=true;modQuery="";modPage=0;
        if(createPack!.MetadataResolved){TransitionManagement(RenderCustomizeModpack);return;}
        TransitionManagement(()=>{CreationHeading("Customize Modpack","Reading the mods included in the server pack…","Modpack Choice",()=>TransitionManagement(RenderModpackChoice));var status=MLabel(management,"Loading installed mod details…",20,180,MWidth-40,60,15);_=ResolveForPage(status);});
    }
    async Task ResolveForPage(Label status)
    {
        using var cancel=new PageCancellation(status,TimeSpan.FromMinutes(5));try{if(createPack!.Included.Count>0){await Catalog.ResolveIncluded(createPack,cancel.Token);createPack.MetadataResolved=true;}else await Catalog.Modrinth.IdentifyIncluded(createPack,cancel.Token);if(status.IsDisposed)return;while(managementTransition)await Task.Delay(30);TransitionManagement(RenderCustomizeModpack);}catch(Exception ex){if(!status.IsDisposed){status.Text=ex is OperationCanceledException?"Loading cancelled.":ex.Message;MButton(management,"Try Again",20,260,150,OpenCustomize);if(createPack!.ModFiles.Count>0)MButton(management,"Use Included Files",185,260,190,()=>{createPack.MetadataResolved=true;TransitionManagement(RenderCustomizeModpack);});}}
    }
    void RenderCustomizeModpack()
    {
        var draft=createPack!;CreationHeading("Customize Modpack","Browse mods for "+draft.Source.MinecraftVersion+" · "+draft.Source.Loader+" or manage the included mods.","Modpack Choice",()=>TransitionManagement(RenderModpackChoice));ModpackIdentity("Selected Modpack",draft.Source.Name+" · "+draft.Source.PackVersion,155,PackLogo);
        var search=new TextBox{Text=modQuery,PlaceholderText="Search compatible mods…"};var frame=new HarborInputFrame(search);management.Controls.Add(frame);MBounds(frame,0,234,MWidth-475,40);
        void Search(){modQuery=search.Text;modPage=0;TransitionManagement(RenderCustomizeModpack);}search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){Search();e.SuppressKeyPress=true;}};
        var sort=CatalogSorting(modSort,n=>{modSort=n;modQuery=search.Text;modPage=0;TransitionManagement(RenderCustomizeModpack);});management.Controls.Add(sort);MBounds(sort,MWidth-460,234,175,40);MButton(management,"Search",MWidth-272,234,110,Search);
        MButton(management,includedModsOnly?"Browse Mods":"Installed Mods",MWidth-150,234,150,()=>{includedModsOnly=!includedModsOnly;modPage=0;TransitionManagement(RenderCustomizeModpack);});
        var list=new PaintedPanel{AccessibleName="Modpack customization",AutoScroll=true,SmoothScrolling=true};management.Controls.Add(list);MBounds(list,0,291,MWidth,Math.Max(130,management.ClientSize.Height*96/management.DeviceDpi-380));
        if(includedModsOnly){var projects=draft.Mods.Values.Where(p=>(draft.ModFiles.ContainsKey(p.Id)||draft.Added.ContainsKey(p.Id)||draft.LocalMods.ContainsKey(p.Id))&&p.Name.Contains(modQuery,StringComparison.OrdinalIgnoreCase));if(modSort==4)projects=projects.OrderBy(p=>p.Name);RenderModCards(list,projects.ToList());}
        else{MLabel(list,"Loading matching mods…",20,16,MWidth-40,40,15);_=LoadModCards(list);}
    }
    async Task LoadModCards(Control list)
    {
        using var cancel=new PageCancellation(list,TimeSpan.FromSeconds(45));try{var page=await Catalog.CombinedSearch(modQuery,modSort,modPage,true,cancel.Token,createPack!.Source.MinecraftVersion,createPack.Source.Loader);if(!list.IsDisposed)RenderModCards(list,page.Items,page.PagingTotal);}catch(Exception ex){if(!list.IsDisposed){list.Controls.OfType<Label>().First().Text=ex is OperationCanceledException?"The catalog took too long to respond.":ex.Message;MButton(list,"Try Again",20,70,150,()=>TransitionManagement(RenderCustomizeModpack));MButton(management,"Back",MWidth-140,list.Bottom*96/management.DeviceDpi+14,140,()=>TransitionManagement(RenderModpackChoice));}}
    }
    void RenderModCards(Control list,List<CfProject> projects,int total=0)
    {
        foreach(var c in list.Controls.Cast<Control>().Where(c=>c is not ScrollBar).ToArray())c.Dispose();var draft=createPack!;int width=(MWidth-32)/2;
        for(int i=0;i<projects.Count;i++){var p=projects[i];long canonical=draft.Aliases.GetValueOrDefault(p.Id,p.Id);bool installed=draft.ModFiles.ContainsKey(canonical)&&!draft.Removed.Contains(canonical)||draft.Added.ContainsKey(canonical)||draft.LocalMods.ContainsKey(canonical);if(!draft.Mods.ContainsKey(canonical))draft.Mods[canonical]=p with{Id=canonical};if(canonical!=p.Id)p=p with{Id=canonical};
            CatalogCard(list,p,i%2*(width+14),i/2*124,width,(installed?"Installed · ":p.Source+" · ")+draft.Source.MinecraftVersion+" · "+draft.Source.Loader,installed?"Remove":"Add",()=>ChangeMod(p,installed));}
        ((PaintedPanel)list).AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(Math.Max(65,(projects.Count+1)/2*124)));if(projects.Count==0)MLabel(list,"No mods match your search.",20,15,MWidth-40,40,15);int y=list.Bottom*96/management.DeviceDpi+14;
        if(includedModsOnly)MButton(management,"Add Local Mod",0,y,166,AddLocalMod);
        MButton(management,"CurseForge Mods",includedModsOnly?178:260,y,165,()=>OpenPath("https://www.curseforge.com/minecraft/search?class=mc-mods&search="+Uri.EscapeDataString(modQuery)));
        MButton(management,"Cancel",MWidth-295,y,130,()=>TransitionManagement(RenderServerList));MButton(management,"Continue",MWidth-153,y,153,()=>TransitionManagement(RenderCreateSettings),true);
        if(!includedModsOnly){var prev=MButton(management,"Previous",0,y,120,()=>{modPage--;TransitionManagement(RenderCustomizeModpack);});prev.Enabled=modPage>0;var next=MButton(management,"Next",132,y,110,()=>{modPage++;TransitionManagement(RenderCustomizeModpack);});next.Enabled=(modPage+1)*20<total;}
        management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(y+63));
    }
    async void ChangeMod(CfProject project,bool installed)
    {
        if(addingMod)return;addingMod=true;var draft=createPack!;try{if(installed){if(draft.LocalMods.Remove(project.Id)){draft.Mods.Remove(project.Id);TransitionManagement(RenderCustomizeModpack);return;}var requiredBy=draft.ModFiles.Values.Where(f=>f.ModId!=project.Id&&!draft.Removed.Contains(f.ModId)).Concat(draft.Added.Values.Where(f=>f.ModId!=project.Id)).Any(f=>f.Dependencies.Any(d=>d.Relation==3&&d.ModId==project.Id));if(requiredBy)throw new InvalidOperationException("Another selected mod requires this mod. Remove that mod first.");if(draft.Added.Remove(project.Id)){}else draft.Removed.Add(project.Id);TransitionManagement(RenderCustomizeModpack);return;}
            if(draft.ModFiles.ContainsKey(project.Id)){draft.Removed.Remove(project.Id);TransitionManagement(RenderCustomizeModpack);return;}
            foreach(var b in management.Controls.OfType<HarborButton>())b.Enabled=false;
            using var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(2));var added=await Catalog.ResolveAddition(draft,project,cancel.Token);foreach(var item in added){draft.Added[item.Key]=item.Value;draft.Removed.Remove(item.Key);}var projects=await Catalog.GetProjects(added.Keys,cancel.Token);foreach(var item in projects)draft.Mods[item.Key]=item.Value;TransitionManagement(RenderCustomizeModpack);
        }catch(Exception ex){MessageBox.Show(this,ex is OperationCanceledException?"Mod lookup timed out.":ex.Message,"Minecraft Harbor");TransitionManagement(RenderCustomizeModpack);}finally{addingMod=false;}
    }
    async void AddLocalMod()
    {
        using var picker=new OpenFileDialog{Title="Add a mod for "+createPack!.Source.MinecraftVersion+" · "+createPack.Source.Loader,Filter="Minecraft mods (*.jar)|*.jar"};if(picker.ShowDialog(this)!=DialogResult.OK)return;
        try{string id=LocalMods.Identity(picker.FileName,createPack.Source.Loader);using var stream=File.OpenRead(picker.FileName);string hash=Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream));string cache=Path.Combine(server.Root,"downloads","local-mods",hash,Path.GetFileName(picker.FileName));Directory.CreateDirectory(Path.GetDirectoryName(cache)!);if(!File.Exists(cache))File.Copy(picker.FileName,cache);
            long key=-10000-createPack.LocalMods.Count;while(createPack.Mods.ContainsKey(key))key--;createPack.LocalMods[key]=new(cache,Path.GetFileName(cache),id);createPack.Mods[key]=new(key,id,"Local mod · replaces a matching mod in this server only.","","",0,new());includedModsOnly=true;TransitionManagement(RenderCustomizeModpack);
        }catch(Exception ex){MessageBox.Show(this,ex.Message,"Minecraft Harbor");}
    }
    void RenderPackConfiguration()
    {
        var draft=createPack!;CreationHeading("Modpack Settings","Settings discovered in this pack's published server configuration.","Game Settings",()=>{CapturePackConfiguration();TransitionManagement(RenderCreateGame);});ModpackIdentity(draft.Source.Name,draft.Source.PackVersion+" · "+draft.Config.Count+" settings",155,PackLogo);packConfigInputs.Clear();int y=234;
        if(draft.Config.Count>0){var files=new HarborDropdown();foreach(var file in draft.Config.Select(c=>c.File).Distinct())files.Items.Add(file);selectedConfigFile=selectedConfigFile!=null&&files.Items.Contains(selectedConfigFile)?selectedConfigFile:files.Items[0]!.ToString();files.SelectedItem=selectedConfigFile;management.Controls.Add(files);MBounds(files,0,y,MWidth,40);files.SelectedIndexChanged+=(_,_)=>{CapturePackConfiguration();selectedConfigFile=files.SelectedItem!.ToString();TransitionManagement(RenderPackConfiguration);};y+=54;}
        foreach(var group in draft.Config.Where(v=>v.File==selectedConfigFile).GroupBy(v=>v.File)){
            var card=MCard(y,48+group.Count()*49);MLabel(card,group.Key,20,9,MWidth-40,32,12,true);int row=46;
            foreach(var value in group){MLabel(card,value.Key,20,row,MWidth/2-35,38,10);Control input=value.Kind=="bool"?new HarborToggle{Checked=value.Value=="true",ShowStateLabel=false}:new HarborNumber(int.Parse(value.Value),int.MinValue);card.Controls.Add(input);MBounds(input,value.Kind=="bool"?MWidth-95:MWidth/2,row,value.Kind=="bool"?64:MWidth/2-25,36);packConfigInputs[value]=input;row+=49;}
            y+=60+group.Count()*49;
        }
        if(draft.Config.Count==0){MLabel(management,"No editable boolean or integer settings were found in this pack.",20,y,MWidth-40,50,14);y+=65;}
        MButton(management,"Reset Defaults",0,y,174,()=>{foreach(var v in draft.Config)v.Value=v.Original;TransitionManagement(RenderPackConfiguration);});MButton(management,"Back",MWidth-140,y,140,()=>{CapturePackConfiguration();TransitionManagement(RenderCreateGame);});management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(y+60));
    }
    void CapturePackConfiguration(){foreach(var item in packConfigInputs)if(!item.Value.IsDisposed)item.Key.Value=GameValue(item.Value);}
}
