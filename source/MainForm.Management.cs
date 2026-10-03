using System.Diagnostics;
using System.Text.RegularExpressions;
namespace MinecraftHarbor;
public sealed partial class MainForm
{
    readonly FixedBackdropPanel management=new(){Dock=DockStyle.Fill,AutoScroll=true,SmoothScrolling=true};
    ServerProfile? editingServer;
    Settings? editingConfig;
    TextBox editServerName=new(),editWorldName=new();
    HarborDropdown editRam=new(),editSlots=new(),editView=new(),editSimulation=new();
    bool managementTransition;
    int managementGeneration;
    readonly Dictionary<string,Control> gamePropertyInputs=new(),gameRuleInputs=new();
    readonly Dictionary<string,string> propertyOriginal=new(),ruleOriginal=new();
    void BuildManagement()
    {
        pages["Server Management"].Controls.Add(management);RenderServerList();
    }
    void ClearManagement()
    {
        management.AutoScrollPosition=Point.Empty;
        foreach(var control in management.Controls.Cast<Control>().Where(c=>c is not ScrollBar).ToArray())control.Dispose();
        management.AutoScrollMinSize=Size.Empty;
    }
    void MBounds(Control c,int x,int y,int w,int h)=>c.SetBounds(management.LogicalToDeviceUnits(x),management.LogicalToDeviceUnits(y),management.LogicalToDeviceUnits(w),management.LogicalToDeviceUnits(h));
    Label MLabel(Control parent,string text,int x,int y,int w,int h,float size=11,bool bold=false)
    {var l=LabelAt(parent,text,0,0,0,0,size,Ink,bold);MBounds(l,x,y,w,h);return l;}
    HarborButton MButton(Control parent,string text,int x,int y,int w,Action click,bool primary=false,string glyph="",bool danger=false)
    {var b=(HarborButton)MakeButton(text,w,primary);b.Glyph=glyph;b.Danger=danger;b.QuietDanger=danger;parent.Controls.Add(b);MBounds(b,x,y,w,44);b.Click+=(_,_)=>{try{click();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Minecraft Harbor");}};return b;}
    int MWidth=>Math.Max(870,management.ClientSize.Width*96/management.DeviceDpi-18);
    PaintedPanel MCard(int y,int height)
    {var card=new PaintedPanel();card.Draw=(g,w,h)=>HarborTheme.Card(g,new RectangleF(0,0,w,h));management.Controls.Add(card);MBounds(card,0,y,MWidth,height);card.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;return card;}
    void MHeading(string title,string subtitle,bool back=false,Action? backAction=null)
    {
        if(back){var backButton=MButton(management,"Back to "+(title.StartsWith("Game Settings")?"Server Settings":"Server Management"),0,0,290,backAction!,glyph:"back");backButton.Chrome=true;int textWidth=TextRenderer.MeasureText(backButton.Text,backButton.Font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.SingleLine|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix).Width;backButton.Width=textWidth+management.LogicalToDeviceUnits(63);}
        MLabel(management,title,0,back?53:12,back?MWidth:MWidth-270,65,34,true);MLabel(management,subtitle,0,back?114:80,850,35,13);
    }
    void RenderServerList()
    {
        ClearManagement();editingServer=null;editingConfig=null;
        MHeading("Server Management","View, edit, and manage all of your saved servers.");
        var create=MButton(management,"Create New Server",MWidth-252,12,252,BeginCreateServer,true,"plus");create.Anchor=AnchorStyles.Top|AnchorStyles.Right;
        int y=163;
        foreach(var profile in server.Library.Data.Profiles.Where(p=>!p.Deleted)){
            var card=MCard(y,137);var cfg=server.ReadProfileSettings(profile);
            card.Draw=(g,w,h)=>{HarborTheme.Card(g,new RectangleF(0,0,w,h));HarborTheme.Icon(g,"cube",26,43,42,Ink);};
            int actions=MWidth-467;
            MLabel(card,profile.DisplayName,94,20,Math.Max(220,actions-110),33,19,true);
            MLabel(card,"Modpack",94,62,91,25);MLabel(card,profile.ToString(),194,62,Math.Max(130,actions-205),25);
            MLabel(card,"World Name",94,95,98,25);MLabel(card,cfg.WorldName,194,95,Math.Max(130,actions-205),25);
            var edit=MButton(card,"Edit",actions,47,120,()=>OpenServerEditor(profile),glyph:"edit");edit.Anchor=AnchorStyles.Top|AnchorStyles.Right;
            bool active=profile.Id==server.Profile.Id;
            var activate=MButton(card,active?"Already Active":"Make Active",actions+130,47,188,async()=>await Run(async()=>{
                if(server.HasProcess||server.Busy)throw new InvalidOperationException("Stop the current server before making another server active.");
                await server.SelectAsync(profile.Id,server.SelectedWorldFolder(profile));RenderServerList();
            }),!active,active?"":"play");activate.Enabled=!active;activate.Anchor=AnchorStyles.Top|AnchorStyles.Right;
            var delete=MButton(card,"Delete",actions+330,47,123,()=>DeleteManagedServer(profile),glyph:"trash",danger:true);delete.Anchor=AnchorStyles.Top|AnchorStyles.Right;
            y+=151;
        }
        if(y==163)MLabel(management,"No saved servers",24,180,650,40,20,true);
        management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(y+15));
    }
    async void DeleteManagedServer(ServerProfile profile)
    {
        if(MessageBox.Show(this,"Delete “"+profile.DisplayName+"” and its server worlds?\n\nThe server files go to the Recycle Bin. Existing backups are kept.","Delete Server",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        await Run(()=>{server.DeleteProfile(profile);RenderServerList();return Task.CompletedTask;});
    }
    void OpenServerEditor(ServerProfile profile)
    {
        draftServerName="";editingServer=profile;editingConfig=server.ReadProfileSettings(profile);TransitionManagement(RenderServerEditor);
    }
    void CaptureServerDraft()
    {
        if(editingConfig==null)return;editingConfig.WorldName=editWorldName.Text;editingConfig.MemoryGB=((NumberOption)editRam.SelectedItem!).Value;editingConfig.MaxPlayers=((NumberOption)editSlots.SelectedItem!).Value;editingConfig.ViewDistance=((NumberOption)editView.SelectedItem!).Value;editingConfig.SimulationDistance=((NumberOption)editSimulation.SelectedItem!).Value;
    }
    static string ServerCaption(ServerProfile profile,string name=""){string display=string.IsNullOrWhiteSpace(name)?profile.DisplayName:name;return display==profile.Name?profile.ToString():display+" · "+profile;}
    sealed record NumberOption(int Value,string Unit,bool Current){public override string ToString()=>Value+Unit+(Current?" (current)":"");}
    string draftServerName="";
    void RenderServerEditor()
    {
        var profile=editingServer!;var cfg=editingConfig!;ClearManagement();
        MHeading("Server Settings","Edit the details and configuration for this server.",true,()=>TransitionManagement(RenderServerList));
        var card=MCard(150,450);int w=MWidth;card.Draw=(g,cw,ch)=>{HarborTheme.Card(g,new RectangleF(0,0,cw,ch));HarborTheme.Icon(g,"cube",28,30,44,Ink);};
        MLabel(card,ServerCaption(profile,draftServerName),105,22,w-130,40,22,true);
        MLabel(card,"Configure your server settings below.",105,67,w-130,28,12);
        editServerName=new TextBox{Text=draftServerName.Length>0?draftServerName:profile.DisplayName,MaxLength=90};editWorldName=new TextBox{Text=cfg.WorldName,MaxLength=90};
        void TextRow(string label,TextBox box,int y){MLabel(card,label,105,y+5,180,29);var frame=new HarborInputFrame(box);card.Controls.Add(frame);MBounds(frame,285,y,w-313,36);frame.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;}
        TextRow("Server Name",editServerName,100);TextRow("World Name",editWorldName,144);
        HarborDropdown Number(string label,int value,int max,int step,int y,string unit=""){
            var n=new HarborDropdown{Font=new Font("Segoe UI",12),AccessibleName=label};
            foreach(int amount in SystemMemory.Choices(max,step))n.Items.Add(new NumberOption(amount,unit,false));
            if(!n.Items.Cast<NumberOption>().Any(o=>o.Value==value))n.Items.Insert(0,new NumberOption(value,unit,true));
            n.SelectedItem=n.Items.Cast<NumberOption>().Single(o=>o.Value==value);
            MLabel(card,label,105,y+5,180,28);card.Controls.Add(n);MBounds(n,285,y,w-313,38);n.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;return n;
        }
        editRam=Number("RAM Amount",cfg.MemoryGB,Settings.MaxMemoryGB,8,188," GB");editSlots=Number("Player Count",cfg.MaxPlayers,50,5,232);editView=Number("View Distance",cfg.ViewDistance,30,5,276," chunks");editSimulation=Number("Simulation Distance",cfg.SimulationDistance,32,4,320," chunks");
        tips.SetToolTip(editRam,$"{SystemMemory.InstalledGB} GB installed on this PC");
        MButton(card,"Delete Server",24,388,176,()=>DeleteManagedServer(profile),glyph:"trash",danger:true);
        MButton(card,"Edit Game Settings",225,388,230,()=>{CaptureServerDraft();draftServerName=editServerName.Text;gameDraftProperties=null;gameDraftRules=null;showMoreGameSettings=false;TransitionManagement(RenderGameSettings);},glyph:"settings");
        MButton(card,"Cancel",w-290,388,115,()=>{draftServerName="";TransitionManagement(RenderServerList);});
        MButton(card,"Save Changes",w-163,388,140,async()=>await Run(()=>{CaptureServerDraft();server.SaveProfile(profile,cfg,editServerName.Text);draftServerName="";TransitionManagement(RenderServerList);return Task.CompletedTask;}),true);
        foreach(var button in card.Controls.OfType<HarborButton>().Where(b=>b.Text is "Cancel" or "Save Changes"))button.Anchor=AnchorStyles.Top|AnchorStyles.Right;
        management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(615));
    }
    static string FriendlySetting(string key)=>key switch{"minecraft:keep_inventory"=>"Keep Inventory","minecraft:spawn_mobs"=>"Mob Spawning","minecraft:advance_time"=>"Daylight Cycle","minecraft:advance_weather"=>"Weather Cycle","minecraft:random_tick_speed"=>"Random Tick Speed","minecraft:fire_spread_radius_around_player"=>"Fire Spread Radius","pvp"=>"PVP","gamemode"=>"Game Mode","online-mode"=>"Online Mode","spawn-protection"=>"Spawn Protection","entity-broadcast-range-percentage"=>"Entity Distance (%)","force-gamemode"=>"Force Game Mode","allow-flight"=>"Allow Flight","enable-command-block"=>"Command Blocks","doMobSpawning"=>"Mob Spawning","doFireTick"=>"Fire Spread","doDaylightCycle"=>"Daylight Cycle","doWeatherCycle"=>"Weather Cycle",_=>Regex.Replace(Regex.Replace(key,"([a-z])([A-Z])","$1 $2").Replace('-',' '),"^.",m=>m.Value.ToUpperInvariant())};
    static readonly HashSet<string> ManagedProperties=new("level-name,max-players,view-distance,simulation-distance,motd,server-ip,server-port,enable-rcon,enable-query,rcon.password,rcon.port,query.port".Split(','));
    void RenderGameSettings()
    {
        ClearManagement();gamePropertyInputs.Clear();gameRuleInputs.Clear();propertyOriginal.Clear();ruleOriginal.Clear();
        var profile=editingServer!;MHeading("Game Settings - "+profile.Name,"Configure gameplay options for this server. Changes apply on the next start.",true,()=>TransitionManagement(RenderServerEditor));
        var props=gameDraftProperties??server.ReadGameProperties(profile);var rules=gameDraftRules??server.ReadGameRules(profile);
        foreach(var pair in props)propertyOriginal[pair.Key]=pair.Value;foreach(var pair in rules)ruleOriginal[pair.Key]=pair.Value;

        int column=(MWidth-18)/2;
        var left=new PaintedPanel();var right=new PaintedPanel();management.Controls.AddRange(new Control[]{left,right});
        left.Draw=right.Draw=(g,w,h)=>HarborTheme.Card(g,new RectangleF(0,0,w,h));
        MLabel(left,"General",18,13,column-36,29,16,true);MLabel(right,"World & Environment",18,13,column-36,29,16,true);
        int ly=52,ry=52;
        string[] mainProperties={"difficulty","gamemode","pvp","online-mode","spawn-protection","entity-broadcast-range-percentage","allow-flight","force-gamemode"};
        string[] mainRules={"randomTickSpeed","doMobSpawning","doFireTick","keepInventory","doDaylightCycle","doWeatherCycle"};
        foreach(var key in mainProperties)if(props.TryGetValue(key,out var value))AddGameRow(left,key,value,gamePropertyInputs,ref ly,column);
        foreach(var key in mainRules){string actual=rules.ContainsKey(key)?key:profile.Loader=="vanilla"?VanillaCatalog.RuleCommand(profile.MinecraftVersion,key,"true").Key:key;if(rules.TryGetValue(actual,out var value))AddGameRow(right,actual,value,gameRuleInputs,ref ry,column);}
        foreach(var key in new[]{"enable-command-block","hardcore"})if(props.TryGetValue(key,out var value))AddGameRow(right,key,value,gamePropertyInputs,ref ry,column);
        if(showMoreGameSettings){
            MLabel(left,"More Server Options",18,ly+8,column-36,32,16,true);ly+=47;
            foreach(var pair in props.Where(p=>!ManagedProperties.Contains(p.Key)&&!gamePropertyInputs.ContainsKey(p.Key)).OrderBy(p=>p.Key))AddGameRow(left,pair.Key,pair.Value,gamePropertyInputs,ref ly,column);
            MLabel(right,"More Game Rules",18,ry+8,column-36,32,16,true);ry+=47;
            foreach(var pair in rules.Where(p=>!gameRuleInputs.ContainsKey(p.Key)).OrderBy(p=>p.Key))AddGameRow(right,pair.Key,pair.Value,gameRuleInputs,ref ry,column);
        }
        MBounds(left,0,163,column,ly+8);MBounds(right,column+18,163,column,Math.Max(ry+8,140));
        int bottom=163+Math.Max(ly,ry)+21;
        var reset=MButton(management,"Reset Defaults",0,bottom,174,ResetGameDefaults,glyph:"restart",danger:true);tips.SetToolTip(reset,"Restore standard Minecraft gameplay defaults. Custom mod rules and connection settings stay unchanged until you edit them.");
        MButton(management,showMoreGameSettings?"Fewer Settings":"More Settings",185,bottom,162,()=>{CaptureGameDraft();showMoreGameSettings=!showMoreGameSettings;RenderGameSettings();});
        MButton(management,"Cancel",MWidth-290,bottom,115,()=>TransitionManagement(RenderServerEditor));
        MButton(management,"Save Changes",MWidth-163,bottom,150,async()=>await Run(()=>{
            CaptureGameDraft();server.SaveGameSettings(profile,gameDraftProperties!.Where(p=>!ManagedProperties.Contains(p.Key)).ToDictionary(),gameDraftRules!);TransitionManagement(RenderServerEditor);return Task.CompletedTask;
        }),true);
        management.AutoScrollMinSize=new Size(0,management.LogicalToDeviceUnits(bottom+58));
    }
    bool showMoreGameSettings;
    Dictionary<string,string>? gameDraftProperties,gameDraftRules;
    void CaptureGameDraft()
    {
        gameDraftProperties=new(propertyOriginal);gameDraftRules=new(ruleOriginal);
        foreach(var input in gamePropertyInputs)gameDraftProperties[input.Key]=GameValue(input.Value);
        foreach(var input in gameRuleInputs)gameDraftRules[input.Key]=GameValue(input.Value);
    }
    void AddGameRow(Control parent,string key,string value,Dictionary<string,Control> inputs,ref int y,int width,int rowHeight=36)
    {
        var label=MLabel(parent,FriendlySetting(key),18,y+6,width/2-25,rowHeight,10);tips.SetToolTip(label,key);
        Control input;
        if(bool.TryParse(value,out bool enabled))input=new HarborToggle{Checked=enabled,Text=FriendlySetting(key)};
        else if(key is "gamemode" or "difficulty"){
            var list=new HarborDropdown();list.Items.AddRange(key=="gamemode"?new object[]{"survival","creative","adventure","spectator"}:new object[]{"peaceful","easy","normal","hard"});list.SelectedItem=value;input=list;
        }else if(int.TryParse(value,out int number))input=new HarborNumber(number,key is "max-tick-time" or "pause-when-empty-seconds" or "network-compression-threshold"? -1:0);








        else{var box=new TextBox{Text=value};input=new HarborInputFrame(box);}
        input.AccessibleName=FriendlySetting(key);parent.Controls.Add(input);MBounds(input,width/2,y,width/2-20,Math.Min(32,rowHeight));inputs[key]=input;y+=rowHeight;
    }
    static string GameValue(Control c)=>c switch{HarborNumber n=>n.ReadValue(),HarborInputFrame f=>f.Input.Text,HarborToggle t=>t.Checked?"true":"false",HarborDropdown d=>d.SelectedItem?.ToString()??"",_=>c.Text};
    void ResetGameDefaults()
    {
        CaptureGameDraft();
        foreach(var pair in GameDefaults.Properties)if(gameDraftProperties!.ContainsKey(pair.Key))gameDraftProperties[pair.Key]=pair.Value;
        foreach(var pair in GameDefaults.Rules)if(gameDraftRules!.ContainsKey(pair.Key))gameDraftRules[pair.Key]=pair.Value;
        RenderGameSettings();
    }
    async void TransitionManagement(Action render)
    {
        if(managementTransition)return;managementTransition=true;int generation=++managementGeneration;
        HomeFadeOverlay? overlay=null;
        try{
            bool animate=Visible&&MotionEnabled;
            Bitmap Capture(){var bitmap=new Bitmap(management.Width,management.Height);management.DrawToBitmap(bitmap,management.ClientRectangle);return bitmap;}
            async Task Fade(bool entering){int duration=preferences.FadeMilliseconds/2;var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<duration&&generation==managementGeneration&&!IsDisposed){float t=Math.Min(1,watch.ElapsedMilliseconds/(float)duration);overlay!.Alpha=entering?t*t*(3-2*t):1-t*t*(3-2*t);overlay.Invalidate();await Task.Delay(15);}if(overlay!=null){overlay.Alpha=entering?1:0;overlay.Refresh();}}
            if(animate){overlay=new HomeFadeOverlay(Capture()){Bounds=management.Bounds,Alpha=1};pages["Server Management"].Controls.Add(overlay);overlay.BringToFront();overlay.Refresh();await Fade(false);}
            if(generation!=managementGeneration||IsDisposed)return;
            // Keep the cover in place while replacing the page beneath it.
            management.SuspendLayout();try{render();}finally{management.ResumeLayout(true);}
            if(overlay!=null){overlay.BringToFront();overlay.ReplaceSnapshot(Capture());overlay.Alpha=0;overlay.Refresh();await Fade(true);}
            management.Refresh();
        }catch(Exception ex){server.Log("Page transition: "+ex.Message);if(!IsDisposed)MessageBox.Show(this,ex.Message,"Minecraft Harbor");}finally{overlay?.Dispose();managementTransition=false;}
    }
}
internal sealed class HarborToggle:CheckBox
{
    public bool ShowStateLabel=true;
    public HarborToggle(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;Cursor=Cursors.Hand;}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;if(Parent!=null){var saved=g.Save();g.TranslateTransform(-Left,-Top);using var background=new PaintEventArgs(g,Bounds);InvokePaintBackground(Parent,background);InvokePaint(Parent,background);g.Restore(saved);}else g.Clear(Color.FromArgb(25,32,41));float s=DeviceDpi/96f;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using var path=HarborTheme.Round(new RectangleF(1,5*s,46*s,24*s),12*s);using var fill=new SolidBrush(Checked?Color.FromArgb(0,200,91):Color.FromArgb(53,72,94));g.FillPath(fill,path);using var knob=new SolidBrush(HarborTheme.Ink);g.FillEllipse(knob,(Checked?25:4)*s,8*s,18*s,18*s);using var labelFont=new Font("Segoe UI",11,FontStyle.Regular);if(ShowStateLabel)TextRenderer.DrawText(g,Checked?"Enabled":"Disabled",labelFont,new Rectangle((int)(59*s),0,Math.Max(1,Width-(int)(59*s)),Height),Checked?Color.FromArgb(154,232,186):HarborTheme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,ClientRectangle);}
}


