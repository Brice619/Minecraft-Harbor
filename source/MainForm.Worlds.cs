namespace MinecraftHarbor;
public sealed partial class MainForm
{
    PackWorld? editingDiskWorld;
    bool choosingCreationWorld;
    readonly List<PackWorld> browsedWorlds=new();
    PaintedPanelBase WorldNameField(TextBox input,int width,Action choose)
    {
        var field=new PaintedPanelBase{AccessibleName="World selection"};var frame=new HarborInputFrame(input);field.Controls.Add(frame);MBounds(frame,0,0,width-149,36);
        var button=MButton(field,"Choose World",width-139,0,139,choose);MBounds(button,width-139,0,139,36);return field;
    }
    void OpenWorldPicker(bool creation)
    {
        choosingCreationWorld=creation;browsedWorlds.Clear();
        if(!creation){CaptureServerDraft();draftServerName=editServerName.Text;}
        TransitionManagement(RenderWorldPicker);
    }
    CurseForgeProfile WorldPickerPack=>choosingCreationWorld?createPack!.Source:PackWorlds.Pack(editingServer!);
    void ReturnFromWorldPicker()=>TransitionManagement(choosingCreationWorld?RenderCreateSettings:RenderServerEditor);
    void RenderWorldPicker()
    {
        var pack=WorldPickerPack;
        CreationHeading("Choose World","Choose a saved world from "+pack.Name+".","Server Settings",ReturnFromWorldPicker);
        var identity=MCard(155,65);IdentityArtwork(identity,Path.Combine(server.Root,"artwork","curseforge",pack.ProjectId+".png"),new(pack.ProjectId,pack.Name,"",pack.Logo,pack.Website,0,new()));
        MLabel(identity,pack.Name,85,8,MWidth-110,30,15,true);MLabel(identity,pack.PackVersion+" · Minecraft "+pack.MinecraftVersion,85,37,MWidth-110,24,11);
        if(choosingCreationWorld)MButton(management,"Generate New World",0,234,190,()=>{createPack!.World=null;createSettings.WorldName="world";SetCreationDefaults();ReturnFromWorldPicker();},glyph:"plus");
        MButton(management,"Browse Folder",MWidth-173,234,173,BrowseWorldFolder,glyph:"folder");
        var list=new BackupViewport{AccessibleName="Matching saved worlds"};management.Controls.Add(list);MBounds(list,0,291,MWidth,Math.Max(140,management.ClientSize.Height*96/management.DeviceDpi-365));
        MLabel(list,"Finding matching saved worlds…",18,18,MWidth-40,35,13);_=LoadWorldChoices(list,pack);
        MButton(management,"Cancel",MWidth-140,list.Bottom*96/management.DeviceDpi+13,140,ReturnFromWorldPicker);
        management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(list.Bottom*96/management.DeviceDpi+72));
    }
    async Task LoadWorldChoices(BackupViewport list,CurseForgeProfile pack)
    {
        using var cancel=new PageCancellation(list,TimeSpan.FromSeconds(45));
        try{
            var worlds=await Task.Run(()=>PackWorlds.Discover(pack,server.Library,cancel.Token),cancel.Token);if(list.IsDisposed)return;
            worlds.AddRange(browsedWorlds);
            if(!choosingCreationWorld)foreach(var world in editingServer!.Worlds){
                string path=Path.Combine(server.Library.ProfileRoot(editingServer),"server",world.Folder);
                if(!worlds.Any(w=>w.Path.Equals(path,StringComparison.OrdinalIgnoreCase)))worlds.Add(new(path,world.Name,editingServer.DisplayName+(world.CanGenerate?" · New world":""),pack.MinecraftVersion,editingServer.Id,world.Folder));
            }
            foreach(var c in list.Controls.Cast<Control>().Where(c=>c is not BackupScrollRail).ToArray())c.Dispose();int y=0,width=(list.Width-list.LogicalToDeviceUnits(18))*96/list.DeviceDpi;
            foreach(var world in worlds.DistinctBy(w=>w.Path,StringComparer.OrdinalIgnoreCase).OrderBy(w=>w.Name,StringComparer.OrdinalIgnoreCase)){
                var card=new PaintedPanel{AccessibleName=world.Name};card.Draw=(g,w,h)=>HarborTheme.Card(g,new RectangleF(0,0,w,h));list.Controls.Add(card);MBounds(card,0,y,width,112);
                MLabel(card,world.Name,20,10,width-205,30,16,true);MLabel(card,world.Origin,20,42,width-205,25,11);var path=MLabel(card,world.Path,20,74,width-205,23,9);path.ForeColor=Muted;path.AutoEllipsis=true;tips.SetToolTip(path,world.Path);
                bool selected=choosingCreationWorld?createPack?.World?.Path.Equals(world.Path,StringComparison.OrdinalIgnoreCase)==true:editingDiskWorld!=null?editingDiskWorld.Path.Equals(world.Path,StringComparison.OrdinalIgnoreCase):world.ProfileId==editingServer!.Id&&world.Folder==editingConfig!.WorldFolder;
                var button=MButton(card,selected?"Selected":"Select",width-165,33,149,()=>SelectWorldChoice(world),!selected);button.Enabled=!selected;y+=124;
            }
            if(y==0)MLabel(list,"No matching saved worlds found. Browse to the modpack's saves folder on disk.",18,18,MWidth-40,70,13);
            list.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(Math.Max(100,y)));
        }catch(Exception ex){if(!list.IsDisposed)list.Controls.OfType<Label>().First().Text=ex is OperationCanceledException?"World search cancelled.":ex.Message;}
    }
    void SelectWorldChoice(PackWorld world)
    {
        if(choosingCreationWorld){
            createPack!.World=PackWorlds.Qualify(world.Path,createPack.Source,server.Library);createSettings.WorldName=world.Name;SetCreationDefaults();
        }else{
            if(world.ProfileId==editingServer!.Id){editingDiskWorld=null;editingConfig!.WorldFolder=world.Folder;}
            else editingDiskWorld=PackWorlds.Qualify(world.Path,PackWorlds.Pack(editingServer),server.Library);
            editingConfig!.WorldName=world.Name;
        }
        ReturnFromWorldPicker();
    }
    void BrowseWorldFolder()
    {
        using var picker=new FolderBrowserDialog{Description="Choose a saved world or the modpack's saves folder",UseDescriptionForTitle=true};
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        try{
            if(File.Exists(Path.Combine(picker.SelectedPath,"level.dat"))){SelectWorldChoice(PackWorlds.Qualify(picker.SelectedPath,WorldPickerPack,server.Library));return;}
            string folder=Directory.Exists(Path.Combine(picker.SelectedPath,"saves"))?Path.Combine(picker.SelectedPath,"saves"):picker.SelectedPath;
            var found=new List<PackWorld>();foreach(string child in Directory.EnumerateDirectories(folder))try{found.Add(PackWorlds.Qualify(child,WorldPickerPack,server.Library));}catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException){}
            if(found.Count==0)throw new InvalidDataException("No worlds from this modpack and release were found in that folder.");
            browsedWorlds.AddRange(found);TransitionManagement(RenderWorldPicker);
        }catch(Exception ex){MessageBox.Show(this,ex.Message,"Choose World");}
    }
}
