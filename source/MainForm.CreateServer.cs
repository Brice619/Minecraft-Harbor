namespace MinecraftHarbor;
public sealed partial class MainForm
{
    List<VanillaVersion>? vanillaVersions;
    VanillaVersion? createVersion;
    Settings createSettings=new();
    string createName="My Vanilla Server";
    Dictionary<string,string> createProperties=new(),createRules=new();
    readonly Dictionary<string,Control> createPropertyInputs=new(),createRuleInputs=new();
    bool creatingServer;
    void CreationHeading(string title,string subtitle,string back,Action action)
    {
        ClearManagement();var b=MButton(management,"Back to "+back,0,0,330,action,glyph:"back");b.Chrome=true;
        using var g=b.CreateGraphics();int text=TextRenderer.MeasureText(g,b.Text,b.Font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.SingleLine|TextFormatFlags.NoPadding).Width;b.Width=text+management.LogicalToDeviceUnits(63);
        MLabel(management,title,0,50,MWidth,62,34,true);MLabel(management,subtitle,0,111,MWidth,35,13).ForeColor=Color.FromArgb(112,218,242);
    }
    void BeginCreateServer()
    {
        createPack=null;createAllowCheats=false;createVersion=null;createName="My Vanilla Server";createSettings=new(){MemoryGB=8,MaxPlayers=20,ViewDistance=10,SimulationDistance=8,WorldName="world",WorldFolder="world"};
        TransitionManagement(RenderCreateChoice);
    }
    void RenderCreateChoice()
    {
        CreationHeading("Create New Server","What server are we running today?","Server Management",()=>TransitionManagement(RenderServerList));
        int width=(MWidth-20)/2,height=Math.Max(355,Math.Min(530,management.ClientSize.Height*96/management.DeviceDpi-185));
        void Tile(string name,string description,string file,int x,Action click){
            var tile=new CreationTile(name,description,Path.Combine(server.Root,"artwork","create",file));management.Controls.Add(tile);MBounds(tile,x,163,width,height);tile.Click+=(_,_)=>click();
        }
        Tile("Modded","Play with mods, modpacks, and custom content.\nAdd new worlds, items, machines, and more.","modded.png",0,()=>TransitionManagement(RenderCreateModded));
        Tile("Vanilla","A clean, classic Minecraft experience.\nJust you, your friends, and the world.","vanilla.png",width+20,OpenVanillaVersions);
        management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(173+height));
    }
    async void OpenVanillaVersions()
    {
        if(vanillaVersions!=null){TransitionManagement(RenderVanillaVersions);return;}
        TransitionManagement(()=>{
            CreationHeading("Choose Vanilla Version","Loading official Minecraft versions…","Create New Server",()=>TransitionManagement(RenderCreateChoice));
            var status=MLabel(management,"Connecting to Mojang…",20,200,MWidth-40,40,17);
            _=LoadVersionsForPage(status);
        });
        await Task.CompletedTask;
    }
    async Task LoadVersionsForPage(Label status)
    {
        try{vanillaVersions=await VanillaCatalog.Load(server.Root);if(status.IsDisposed)return;while(managementTransition&&!IsDisposed)await Task.Delay(30);if(!status.IsDisposed)TransitionManagement(RenderVanillaVersions);}
        catch(Exception ex){if(!status.IsDisposed){status.Text="Could not load versions. "+ex.Message;MButton(management,"Try Again",20,260,160,OpenVanillaVersions,true);}}
    }
    void RenderVanillaVersions()
    {
        CreationHeading("Choose Vanilla Version","Pick the version of vanilla Minecraft you want to run.","Create New Server",()=>TransitionManagement(RenderCreateChoice));
        int height=Math.Max(290,management.ClientSize.Height*96/management.DeviceDpi-247);
        var card=MCard(155,height);MLabel(card,"Vanilla Server",60,10,MWidth-80,30,15,true);MLabel(card,"Official Minecraft releases · newest to oldest",60,41,MWidth-80,25,10);
        card.Draw=(g,w,h)=>{HarborTheme.Card(g,new RectangleF(0,0,w,h));HarborTheme.Icon(g,"cube",19,20,28,Ink);};
        var list=new VanillaVersionList(vanillaVersions!){AccessibleName="Minecraft versions"};card.Controls.Add(list);MBounds(list,8,77,MWidth-16,height-85);
        list.Chosen+=version=>{if(managementTransition)return;createVersion=version;createProperties=VanillaCatalog.Properties(version);createRules=VanillaCatalog.Rules(version);TransitionManagement(RenderCreateSettings);};
        MLabel(management,"Official server downloads go back to 1.2.5.",3,height+164,MWidth-300,35,10);
        MButton(management,"Cancel",MWidth-140,height+165,140,()=>TransitionManagement(RenderServerList));
        management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(height+218));
    }
    Label CreationIdentity(int y)
    {
        var card=MCard(y,65);IdentityArtwork(card,createPack==null?null:PackLogo,createPack?.Project);int x=createPack==null?70:85;
        var title=MLabel(card,createName,x,8,MWidth-x-25,30,16,true);MLabel(card,createPack==null?"Vanilla · Version: "+createVersion!.Id:createPack.Source.Name+" · "+createPack.Source.PackVersion+" · Minecraft "+createPack.Source.MinecraftVersion,x,37,MWidth-x-25,24,11);return title;
    }
    void RenderCreateSettings()
    {
        CreationHeading("Server Settings","Set up the main settings for your new "+(createPack==null?"vanilla":"modded")+" server.",createPack==null?"Choose Vanilla Version":customizePack?"Customize Modpack":"Modpack Choice",()=>TransitionManagement(createPack==null?RenderVanillaVersions:customizePack?RenderCustomizeModpack:RenderModpackChoice));
        var caption=CreationIdentity(155);int step=Math.Clamp((management.ClientSize.Height*96/management.DeviceDpi-335)/6,48,62);int cardHeight=step*6+18;var card=MCard(234,cardHeight);int w=MWidth,x=w/2;
        var name=new TextBox{Text=createName,MaxLength=90};var world=new TextBox{Text=createSettings.WorldName,MaxLength=90};
        name.TextChanged+=(_,_)=>{createName=name.Text;caption.Text=createName;};world.TextChanged+=(_,_)=>createSettings.WorldName=world.Text;
        void Row(string label,string description,Control control,int y){MLabel(card,label,25,y,x-45,24,12,true);MLabel(card,description,25,y+24,x-45,22,10).ForeColor=Muted;card.Controls.Add(control);MBounds(control,x,y+6,w-x-25,38);}
        Row("Server Name","A name to identify your server.",new HarborInputFrame(name),14);Row("World Name",createPack?.World==null?"Name a new world or choose an existing save.":"Selected save: "+createPack.World.Name,createPack==null?new HarborInputFrame(world):WorldNameField(world,w-x-25,()=>OpenWorldPicker(true)),14+step);
        HarborDropdown Number(int value,int max,int step,string unit,Action<int> change){var d=new HarborDropdown();foreach(int n in SystemMemory.Choices(max,step))d.Items.Add(new NumberOption(n,unit,false));d.SelectedItem=d.Items.Cast<NumberOption>().First(o=>o.Value==value);d.SelectedIndexChanged+=(_,_)=>change(((NumberOption)d.SelectedItem!).Value);return d;}
        Row("RAM Amount",$"{SystemMemory.InstalledGB} GB installed on this PC.",Number(createSettings.MemoryGB,Settings.MaxMemoryGB,8," GB",n=>createSettings.MemoryGB=n),14+step*2);
        Row("Max Player Count","The maximum number of players that can join.",Number(createSettings.MaxPlayers,50,5,"",n=>createSettings.MaxPlayers=n),14+step*3);
        Row("View Distance","How many chunks players can see.",Number(createSettings.ViewDistance,30,5," chunks",n=>createSettings.ViewDistance=n),14+step*4);
        var simulation=Number(createSettings.SimulationDistance,32,4," chunks",n=>createSettings.SimulationDistance=n);
        simulation.Enabled=createVersion!.Number>=new Version(1,18);
        Row("Simulation Distance",simulation.Enabled?"How many chunks are actively simulated.":"Introduced in Minecraft 1.18.",simulation,14+step*5);
        MButton(management,"Cancel",w-295,247+cardHeight,130,()=>TransitionManagement(RenderServerList));MButton(management,"Continue",w-153,247+cardHeight,153,()=>{if(string.IsNullOrWhiteSpace(createName)||string.IsNullOrWhiteSpace(createSettings.WorldName)){MessageBox.Show(this,"Enter a server name and world name.","Minecraft Harbor");return;}TransitionManagement(RenderCreateGame);},true);
        management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(306+cardHeight));
    }
    void CaptureCreationGame()
    {
        createAllowCheats=createCheatsToggle?.Checked??false;foreach(var p in createPropertyInputs)createProperties[p.Key]=p.Value is HarborDropdown d&&d.SelectedItem is CreationOption option?option.Value:GameValue(p.Value);foreach(var p in createRuleInputs)createRules[p.Key]=GameValue(p.Value);
    }
    bool createAllowCheats;
    HarborToggle? createCheatsToggle;
    sealed record CreationOption(string Value){public override string ToString()=>char.ToUpperInvariant(Value[0])+Value[1..];}
    void RenderCreateGame()
    {
        CreationHeading("Game Settings","Set up the gameplay settings for your new "+(createPack==null?"vanilla":"modded")+" server.","Server Settings",()=>{CaptureCreationGame();TransitionManagement(RenderCreateSettings);});CreationIdentity(155);
        createPropertyInputs.Clear();createRuleInputs.Clear();int step=Math.Clamp((management.ClientSize.Height*96/management.DeviceDpi-335)/6,48,62);int height=step*6+18;int half=MWidth/2;
        var card=MCard(234,height);
        card.Draw=(g,w,h)=>{HarborTheme.Card(g,new RectangleF(0,0,w,h));using var pen=new Pen(Color.FromArgb(44,66,83));g.DrawLine(pen,w/2,16,w/2,h-16);for(int row=1;row<6;row++){g.DrawLine(pen,24,12+row*step,w/2-24,12+row*step);g.DrawLine(pen,w/2+24,12+row*step,w-24,12+row*step);}};
        void Row(string key,string label,string description,bool rule,int col,int row){
            int x=col*half,y=14+row*step;var values=rule?createRules:createProperties;
            bool supported=key=="cheats"||values.ContainsKey(key);string value=key=="cheats"?(createAllowCheats?"true":"false"):supported?values[key]:"false";
            MLabel(card,label,x+24,y,half/2-27,24,12,true);MLabel(card,supported?description:"Not available in this version.",x+24,y+24,bool.TryParse(value,out _)?half-115:half/2-35,22,10).ForeColor=Muted;
            Control control;
            if(bool.TryParse(value,out bool enabled)){control=new HarborToggle{Checked=enabled,Text=label,ShowStateLabel=false};MBounds(control,x+half-77,y+4,52,34);}
            else if(key is "difficulty" or "gamemode"){
                var menu=new HarborDropdown();var options=key=="difficulty"?new[]{"peaceful","easy","normal","hard"}:new[]{"survival","creative","adventure","spectator"};
                foreach(var option in options){if(key=="gamemode"&&(option=="spectator"&&createVersion!.Number<new Version(1,8)||option=="adventure"&&createVersion!.Number<new Version(1,3)))continue;menu.Items.Add(new CreationOption(option));}
                menu.SelectedItem=menu.Items.Cast<CreationOption>().First(o=>o.Value==value);control=menu;MBounds(control,x+half/2,y+3,half/2-25,36);
            }else{control=new HarborNumber(int.Parse(value),0);MBounds(control,x+half/2+25,y+3,half/2-50,36);}
            control.Enabled=supported;control.AccessibleName=label;card.Controls.Add(control);
            if(key=="cheats")createCheatsToggle=(HarborToggle)control;else if(supported)(rule?createRuleInputs:createPropertyInputs)[key]=control;
        }
        Row("difficulty","Difficulty","The world's difficulty setting.",false,0,0);
        Row("gamemode","Game Mode","The default game mode.",false,0,1);
        Row("cheats","Allow Cheats","Enable commands for your existing operators.",false,0,2);
        Row("pvp","PvP","Allow players to fight each other.",false,0,3);
        Row("keepInventory","Keep Inventory","Keep inventory on death.",true,0,4);
        Row("doMobSpawning","Mob Spawning","Allow mobs to naturally spawn.",true,0,5);
        Row("doFireTick","Fire Spread","Allow fire to spread naturally.",true,1,0);
        Row("doDaylightCycle","Daylight Cycle","Enable the day/night cycle.",true,1,1);
        Row("doWeatherCycle","Weather Cycle","Enable rain and thunderstorms.",true,1,2);
        Row("enable-command-block","Command Blocks","Allow the use of command blocks.",false,1,3);
        Row("spawn-protection","Spawn Protection","Radius around spawn (in blocks).",false,1,4);
        Row("randomTickSpeed","Random Tick Speed","Controls random block updates.",true,1,5);
        int bottom=247+height;
        MButton(management,"Reset Defaults",0,bottom,174,()=>{createAllowCheats=false;SetCreationDefaults();TransitionManagement(RenderCreateGame);});
        if(createPack!=null)MButton(management,"Modpack Settings ("+createPack.Config.Count+")",185,bottom,220,()=>{CaptureCreationGame();TransitionManagement(RenderPackConfiguration);},glyph:"settings");
        MButton(management,"Back",MWidth-320,bottom,125,()=>{CaptureCreationGame();TransitionManagement(RenderCreateSettings);});
        MButton(management,"Create Server",MWidth-183,bottom,183,FinishCreateVanilla,true);
        management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(bottom+59));
    }
    SetupProgress setupState=new(0,0,"Preparing server files…");
    CancellationTokenSource? setupCancellation;
    void ReportSetup(SetupProgress value)=>Volatile.Write(ref setupState,value);
    SetupProgressPanel RenderSetupLoading(string name,string version,string kind,Action cancel)
    {
        CreationHeading("Creating Your Server","Please wait while we set up your new "+kind+" server.","Server Settings",cancel);
        var identity=MCard(155,65);IdentityArtwork(identity,createPack==null?null:PackLogo,createPack?.Project);
        int identityX=createPack==null?70:85;MLabel(identity,name,identityX,8,MWidth-identityX-25,30,16,true);MLabel(identity,"Version: "+version,identityX,37,MWidth-identityX-25,24,11);
        int height=Math.Clamp(management.ClientSize.Height*96/management.DeviceDpi-307,310,410);
        var panel=new SetupProgressPanel{Progress=Volatile.Read(ref setupState),AccessibleName="Server setup progress"};management.Controls.Add(panel);MBounds(panel,0,234,MWidth,height);
        int bottom=247+height;var cancelButton=MButton(management,"Cancel",MWidth-320,bottom,125,cancel);var create=MButton(management,"Create Server",MWidth-183,bottom,183,()=>{},true);create.Enabled=false;
        foreach(var label in management.Controls.OfType<Label>().Concat(identity.Controls.OfType<Label>()))label.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
        void LayoutLoading(object? sender,EventArgs e){
            if(panel.IsDisposed)return;int available=management.ClientSize.Height*96/management.DeviceDpi;bool compact=available<610;int top=compact?180:234,h=Math.Clamp(available-top-72,288,410);var offset=management.AutoScrollPosition;management.AutoScrollPosition=Point.Empty;
            var headings=management.Controls.OfType<Label>().ToArray();MBounds(headings[0],0,compact?34:50,MWidth,compact?50:62);MBounds(headings[1],0,compact?80:111,MWidth,compact?30:35);MBounds(identity,0,compact?112:155,MWidth,compact?55:65);
            var captions=identity.Controls.OfType<Label>().ToArray();MBounds(captions[0],identityX,compact?5:8,MWidth-identityX-25,compact?27:30);MBounds(captions[1],identityX,compact?31:37,MWidth-identityX-25,compact?22:24);
            foreach(var art in identity.Controls.OfType<PictureBox>())MBounds(art,14,compact?5:8,60,compact?45:49);
            MBounds(panel,0,top,MWidth,h);MBounds(cancelButton,MWidth-320,top+h+13,125,44);MBounds(create,MWidth-183,top+h+13,183,44);management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(top+h+72));if(offset.Y<0)management.AutoScrollPosition=new Point(0,-offset.Y);
        }
        management.SizeChanged+=LayoutLoading;panel.Disposed+=(_,_)=>management.SizeChanged-=LayoutLoading;
        LayoutLoading(null,EventArgs.Empty);return panel;
    }
    async void FinishCreateVanilla()
    {
        if(creatingServer)return;CaptureCreationGame();
        if(createPack!=null){await RunSetup(createName,createPack.Source.PackVersion+" · Minecraft "+createPack.Source.MinecraftVersion,"modded",token=>server.CreateModdedAsync(createPack,createSettings,createName,createProperties,createRules,createAllowCheats,Catalog,token,ReportSetup),RenderCreateGame);return;}
        await RunSetup(createName,createVersion!.Id,"vanilla",token=>server.CreateVanillaAsync(createVersion!,createSettings,createName,createProperties,createRules,token,createAllowCheats,ReportSetup),RenderCreateGame);
    }
    async void FinishCreateModded(CurseForgeProfile pack)
    {
        if(creatingServer)return;
        await RunSetup(pack.Name,pack.PackVersion+" · Minecraft "+pack.MinecraftVersion,"modded",token=>server.SetUpPackAsync(pack,token,progress:ReportSetup),RenderCreateModded);
    }
    async Task RunSetup(string name,string version,string kind,Func<CancellationToken,Task> action,Action previous)
    {
        creatingServer=true;using var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(45));setupCancellation=cancel;ReportSetup(new(0,0,"Preparing server files…",WorldDeferred:kind=="modded"));
        SetupProgressPanel? panel=null;TransitionManagement(()=>panel=RenderSetupLoading(name,version,kind,()=>cancel.Cancel()));
        using var timer=new System.Windows.Forms.Timer{Interval=100};timer.Tick+=(_,_)=>{if(panel is {IsDisposed:false}&&!ReferenceEquals(panel.Progress,Volatile.Read(ref setupState))){panel.Progress=Volatile.Read(ref setupState);panel.AccessibleName="Server setup "+panel.Progress.Percent+" percent. "+panel.Progress.Detail;panel.Invalidate();}};timer.Start();
        void OnClosing(object? _,FormClosingEventArgs e)=>cancel.Cancel();FormClosing+=OnClosing;
        try{while(managementTransition&&!IsDisposed)await Task.Delay(30,cancel.Token);if(IsDisposed)return;if(panel is null)throw new InvalidOperationException("The setup screen could not be displayed.");management.Refresh();await Task.Yield();await action(cancel.Token);while(managementTransition&&!IsDisposed)await Task.Delay(30);if(!IsDisposed){if(panel is {IsDisposed:false}){panel.Progress=Volatile.Read(ref setupState);panel.Refresh();}await Task.Delay(650);TransitionManagement(RenderServerList);}}
        catch(Exception ex){
            server.Log("Server creation "+(ex is OperationCanceledException?"cancelled": "failed: "+ex));
            if(ex is not OperationCanceledException)ServerManager.WriteJson(Path.Combine(server.Root,"logs","creation-last-error.json"),new{time=DateTimeOffset.Now,name,version,kind,step=Volatile.Read(ref setupState).Step,error=ex.ToString()});
            while(managementTransition&&!IsDisposed)await Task.Delay(30);if(!IsDisposed){MessageBox.Show(this,ex is OperationCanceledException?"Server creation cancelled.":ex.Message,"Minecraft Harbor");TransitionManagement(previous);}
        }finally{FormClosing-=OnClosing;setupCancellation=null;creatingServer=false;}
    }
}
