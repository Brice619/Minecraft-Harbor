using System.Text.Json;

namespace MinecraftHarbor;
public sealed partial class MainForm
{
    readonly HarborDropdown readyPacks=new(),worldChoices=new(),installedPacks=new();
    readonly Label libraryHint=new(),libraryActivity=new(),installedHint=new();
    readonly Button useSelection=new HarborButton(),setUpPack=new HarborButton(),chooseZip=new HarborButton(),cancelSetup=new HarborButton(),importWorld=new HarborButton(),newWorld=new HarborButton();
    readonly List<CurseForgeProfile> discovered=new();
    CancellationTokenSource? packPreparation;
    bool refreshingLibrary;
    string? selectedServerZip;
    string? worldChoicesPackId;
    void ComboStyle(HarborDropdown combo,int x,int y,int width,Control parent)
    {
        combo.BackColor=Card;combo.ForeColor=Ink;combo.Font=new Font("Segoe UI",12);combo.SetBounds(x,y,width,35);parent.Controls.Add(combo);
    }
    void BuildLibrary()
    {
        var p=pages["Worlds & modpacks"];Heading(p,"Worlds & modpacks","Pick a ready server and world, or set up one of your installed CurseForge packs.");
        LabelAt(p,"READY SERVERS",1,112,850,23,9,Mint,true);ComboStyle(readyPacks,0,146,895,p);
        LabelAt(p,"WORLD FOR THIS PACK",1,199,850,23,9,Mint,true);ComboStyle(worldChoices,0,231,650,p);
        CopyButtonStyle(useSelection,"Use this selection",true);useSelection.SetBounds(676,226,219,43);p.Controls.Add(useSelection);
        libraryHint.SetBounds(2,281,895,42);libraryHint.ForeColor=Muted;p.Controls.Add(libraryHint);
        CopyButtonStyle(importWorld,"Import saved world");importWorld.SetBounds(0,335,219,43);p.Controls.Add(importWorld);
        CopyButtonStyle(newWorld,"Create new world");newWorld.SetBounds(234,335,215,43);p.Controls.Add(newWorld);
        LabelAt(p,"Worlds are kept with their modpack. Select a pack before adding worlds to it.",2,391,895,27,10,Muted);
        LabelAt(p,"INSTALLED IN CURSEFORGE",1,445,850,23,9,Mint,true);ComboStyle(installedPacks,0,479,895,p);
        installedHint.SetBounds(2,525,895,44);installedHint.ForeColor=Muted;p.Controls.Add(installedHint);
        CopyButtonStyle(setUpPack,"Set up server",true);setUpPack.SetBounds(0,582,195,43);p.Controls.Add(setUpPack);
        CopyButtonStyle(chooseZip,"Choose server ZIP");chooseZip.SetBounds(207,582,205,43);p.Controls.Add(chooseZip);
        var browse=MakeButton("Browse CurseForge",215);browse.SetBounds(424,582,215,43);p.Controls.Add(browse);browse.Click+=(_,_)=>OpenPath("https://www.curseforge.com/minecraft/search?class=modpacks");
        var locate=MakeButton("Find profile folder",205);locate.SetBounds(0,640,205,43);p.Controls.Add(locate);
        var rescan=MakeButton("Refresh installed",195);rescan.SetBounds(219,640,195,43);p.Controls.Add(rescan);rescan.Click+=(_,_)=>ScanInstalled();
        CopyButtonStyle(cancelSetup,"Cancel setup");cancelSetup.SetBounds(675,640,220,43);cancelSetup.Visible=false;p.Controls.Add(cancelSetup);cancelSetup.Click+=(_,_)=>packPreparation?.Cancel();
        libraryActivity.SetBounds(2,710,895,53);libraryActivity.ForeColor=Mint;p.Controls.Add(libraryActivity);
        readyPacks.SelectedIndexChanged+=(_,_)=>{if(!refreshingLibrary)RefreshWorldChoices();};
        installedPacks.SelectedIndexChanged+=(_,_)=>{selectedServerZip=null;RefreshLibraryActivity();};
        useSelection.Click+=async(_,_)=>await Run(async()=>{
            if(readyPacks.SelectedItem is not ServerProfile pack||worldChoices.SelectedItem is not WorldEntry world)return;
            await server.SelectAsync(pack.Id,world.Folder);recent.Clear();console.Clear();RefreshRoster();
        });
        importWorld.Click+=async(_,_)=>await Run(async()=>{
            RequireSelectedActivePack();var result=ChooseSavedWorld();if(result==null)return;
            await server.ImportWorldAsync(result.Value.Path,result.Value.Name);RefreshLibrary();worldChoices.SelectedIndex=worldChoices.Items.Count-1;
        });
        newWorld.Click+=async(_,_)=>await Run(async()=>{RequireSelectedActivePack();var name=AskWorldName();if(name==null)return;await server.CreateWorldAsync(name);RefreshLibrary();worldChoices.SelectedIndex=worldChoices.Items.Count-1;});
        setUpPack.Click+=async(_,_)=>await PrepareSelected(selectedServerZip);
        chooseZip.Click+=(_,_)=>{
            if(installedPacks.SelectedItem is not CurseForgeProfile)return;
            using var dialog=new OpenFileDialog{Title="Choose this pack's matching server ZIP",Filter="Server pack (*.zip)|*.zip"};
            if(dialog.ShowDialog(this)==DialogResult.OK){selectedServerZip=dialog.FileName;RefreshLibraryActivity();}
        };
        locate.Click+=(_,_)=>{
            using var dialog=new FolderBrowserDialog{Description="Choose an installed CurseForge profile containing minecraftinstance.json",UseDescriptionForTitle=true,ShowNewFolderButton=false};
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            try{var cf=CurseForgeProfiles.Read(dialog.SelectedPath);if(!discovered.Any(c=>c.Path.Equals(cf.Path,StringComparison.OrdinalIgnoreCase)))discovered.Add(cf);var root=Directory.GetParent(cf.Path)!.FullName;if(!server.Library.Data.CurseForgeRoots.Contains(root,StringComparer.OrdinalIgnoreCase)){server.Library.Data.CurseForgeRoots.Add(root);server.Library.Save();}FillInstalled(cf.Path);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Choose a CurseForge profile");}
        };
        ScanInstalled();RefreshLibrary();
    }
    void RequireSelectedActivePack(){if(readyPacks.SelectedItem is not ServerProfile p||p.Id!=server.Profile.Id)throw new InvalidOperationException("Click Use this selection first, then add worlds to that pack.");if(server.HasProcess||server.Busy)throw new InvalidOperationException("Stop and save the server before adding a world.");}
    async Task PrepareSelected(string? zip)
    {
        if(installedPacks.SelectedItem is not CurseForgeProfile cf)return;
        using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(45));packPreparation=cancellation;cancelSetup.Visible=true;
        try{await Run(()=>server.SetUpPackAsync(cf,cancellation.Token,zip));}finally{packPreparation=null;cancelSetup.Visible=false;RefreshLibrary();RefreshLibraryActivity();}
    }
    void ScanInstalled()
    {
        var selected=(installedPacks.SelectedItem as CurseForgeProfile)?.Path;var extras=discovered.Select(c=>Directory.GetParent(c.Path)!.FullName).Concat(server.Library.Data.CurseForgeRoots).ToArray();
        discovered.Clear();discovered.AddRange(CurseForgeProfiles.Discover(extras));FillInstalled(selected);
    }
    void FillInstalled(string? selected)
    {
        installedPacks.Items.Clear();foreach(var cf in discovered)installedPacks.Items.Add(cf);
        int index=discovered.FindIndex(c=>c.Path==selected);if(index<0&&discovered.Count==1)index=0;installedPacks.SelectedIndex=index;RefreshLibraryActivity();
    }
    void RefreshLibrary()
    {
        if(readyPacks.Parent==null)return;
        var selected=(readyPacks.SelectedItem as ServerProfile)?.Id??server.Profile.Id;
        refreshingLibrary=true;readyPacks.Items.Clear();foreach(var pack in server.Library.Data.Profiles)readyPacks.Items.Add(pack);
        readyPacks.SelectedItem=server.Library.Data.Profiles.FirstOrDefault(p=>p.Id==selected)??server.Profile;refreshingLibrary=false;RefreshWorldChoices();RefreshLibraryActivity();
    }
    void RefreshWorldChoices()
    {
        var previous=(worldChoices.SelectedItem as WorldEntry)?.Folder;worldChoices.Items.Clear();
        if(readyPacks.SelectedItem is not ServerProfile pack)return;
        foreach(var world in pack.Worlds)worldChoices.Items.Add(world);
        string selected=server.SelectedWorldFolder(pack);
        if(worldChoicesPackId==pack.Id&&previous!=null&&pack.Worlds.Any(w=>w.Folder==previous))selected=previous;
        worldChoicesPackId=pack.Id;
        worldChoices.SelectedItem=pack.Worlds.FirstOrDefault(w=>w.Folder==selected)??pack.Worlds.FirstOrDefault();
        libraryHint.Text="Active: "+server.Profile+" / "+server.Config.WorldName+"\nSwitching saves and backs up the active world. Press Start server after switching.";
    }
    void RefreshLibraryActivity()
    {
        if(setUpPack.Parent==null)return;
        bool ready=!server.Busy;var cf=installedPacks.SelectedItem as CurseForgeProfile;
        bool exists=cf!=null&&server.Library.Data.Profiles.Any(p=>p.CurseForgePath.Equals(cf.Path,StringComparison.OrdinalIgnoreCase)&&p.PackVersion==cf.PackVersion);
        bool supported=cf?.Loader is "neoforge" or "forge" or "fabric";
        setUpPack.Enabled=ready&&!server.HasProcess&&cf!=null&&!exists&&supported&&(cf.ServerFileId>0||selectedServerZip!=null);
        chooseZip.Enabled=ready&&!server.HasProcess&&cf!=null&&!exists&&supported;
        installedHint.Text=cf==null?"Scanning reads profile details only. Nothing is copied or downloaded.":exists?"Already set up in Harbor. Select it under Ready servers.":!supported?"This profile's loader is not supported yet. NeoForge, Forge and Fabric are supported.":selectedServerZip!=null?"Selected: "+Path.GetFileName(selectedServerZip)+". Click Set up server to use it.":cf.ServerFileId>0?"No files are copied until you click Set up server. Uses this release's matching server download.":"No server download is linked. Choose the matching server ZIP, then click Set up server.";
        useSelection.Enabled=ready&&readyPacks.SelectedItem!=null&&worldChoices.SelectedItem!=null;
        importWorld.Enabled=newWorld.Enabled=ready&&!server.HasProcess;
        readyPacks.Enabled=worldChoices.Enabled=installedPacks.Enabled=ready;
        libraryActivity.Text=server.Busy?server.Activity:server.HasProcess?"Your server is online. Stop & save before setting up another pack or importing a world.":"Server files are created only when you click Set up server. CurseForge downloads stay untouched.";
    }
    Form Dialog(string title,int width,int height)=>new(){Text=title,ClientSize=new Size(width,height),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=Bg,ForeColor=Ink,Font=new Font("Segoe UI",10)};
    static void ScaleDialog(Form dialog){dialog.AutoScaleDimensions=new SizeF(96,96);dialog.AutoScaleMode=AutoScaleMode.Dpi;}
    string? AskWorldName()
    {
        using var d=Dialog("Create a world for "+server.Profile.Name,570,230);LabelAt(d,"Give your new world a name",22,20,525,35,18,Ink,true);
        var name=new TextBox{PlaceholderText="World name"};TextStyle(name);name.SetBounds(24,77,520,37);d.Controls.Add(name);
        LabelAt(d,"The world is generated when you select it and start the server.",24,122,520,31,10,Muted);
        var add=MakeButton("Create world",175,true);add.SetBounds(369,171,175,42);add.DialogResult=DialogResult.OK;d.Controls.Add(add);d.AcceptButton=add;ScaleDialog(d);
        return d.ShowDialog(this)==DialogResult.OK?name.Text:null;
    }
    sealed record SaveChoice(string Path,string Name){public override string ToString()=>Name;}
    (string Path,string Name)? ChooseSavedWorld()
    {
        using var d=Dialog("Import a world into "+server.Profile.Name,730,405);
        LabelAt(d,"Bring a saved world to Harbor",22,20,680,40,22,Ink,true);
        LabelAt(d,"Harbor copies the save, including player progress. Your original stays in CurseForge.",24,76,680,44,10,Muted);
        var choices=new HarborDropdown();ComboStyle(choices,24,135,495,d);var saves=Path.Combine(server.Profile.CurseForgePath,"saves");
        if(Directory.Exists(saves))foreach(var dir in Directory.GetDirectories(saves))if(File.Exists(Path.Combine(dir,"level.dat"))){try{choices.Items.Add(new SaveChoice(dir,WorldImport.Read(dir).Name));}catch(IOException){}catch(InvalidDataException){}}
        var browse=MakeButton("Browse…",165);browse.SetBounds(535,130,170,43);d.Controls.Add(browse);
        var path=new Label{Left=24,Top=182,Width=680,Height=50,ForeColor=Muted,Font=new Font("Segoe UI",9)};d.Controls.Add(path);
        LabelAt(d,"NAME IN HARBOR",24,240,650,22,9,Mint,true);var name=new TextBox();TextStyle(name);name.SetBounds(24,271,680,35);d.Controls.Add(name);
        choices.SelectedIndexChanged+=(_,_)=>{if(choices.SelectedItem is SaveChoice save){path.Text=save.Path;name.Text=save.Name;}};
        browse.Click+=(_,_)=>{
            using var picker=new FolderBrowserDialog{Description="Choose the saved world folder containing level.dat",UseDescriptionForTitle=true,ShowNewFolderButton=false,SelectedPath=Directory.Exists(saves)?saves:""};
            if(picker.ShowDialog(d)!=DialogResult.OK)return;
            try{var info=WorldImport.Read(picker.SelectedPath);var item=new SaveChoice(picker.SelectedPath,info.Name);choices.Items.Add(item);choices.SelectedItem=item;}catch(Exception ex){MessageBox.Show(d,ex.Message,"Choose a saved world");}
        };
        if(choices.Items.Count==1)choices.SelectedIndex=0;
        var import=MakeButton("Import a copy",185,true);import.SetBounds(519,337,185,43);import.DialogResult=DialogResult.OK;d.Controls.Add(import);ScaleDialog(d);
        if(d.ShowDialog(this)!=DialogResult.OK||choices.SelectedItem is not SaveChoice chosen)return null;
        return(chosen.Path,name.Text);
    }
}
