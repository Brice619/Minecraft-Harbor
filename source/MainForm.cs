using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO.Pipes;

namespace MinecraftHarbor;

public sealed partial class MainForm : Form
{
    static readonly Color Bg=Color.FromArgb(13,19,26), Card=Color.FromArgb(25,32,41), Ink=HarborTheme.Ink, Muted=HarborTheme.Muted, Mint=HarborTheme.Green, Edge=HarborTheme.Edge;
    readonly ServerManager server;
    readonly ClientSetup setup;
    readonly Label setupAddress=new(), syncSummary=new(), setupStatus=new();
    readonly TextBox certificate=new();
    readonly Dictionary<string,FixedBackdropPanel> pages=new();
    readonly List<Button> navButtons=new();
    readonly HarborSurface content=new(){Dock=DockStyle.Fill,Padding=new Padding(28,22,22,18)};
    readonly Label state=new(), activity=new(), address=new(), memory=new(), players=new(), uptime=new(), backupStatus=new(), footer=new();
    readonly Label worldTitle=new(),packSubtitle=new(),railFooter=new(),settingsInfo=new(),setupHeading=new(),setupIntro=new(),setupSteps=new(),setupNote=new();
    readonly HarborConsoleLog console=new();
    readonly TextBox preview=new(), command=new();
    readonly Button start=new HarborButton(), stop=new HarborButton(), restart=new HarborButton(), backup=new HarborButton();
    
    
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer timer=new(){Interval=600};
    int tick;
    bool exiting;
    readonly string? previewPath;
    readonly bool autoStart;
    readonly CancellationTokenSource activationCancellation=new();
    readonly List<string> recent=new();
    bool closeInProgress,automaticCheckRunning;
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint state);
    public MainForm(ServerManager manager,string? screenshot=null,bool auto=false,string? visualTestReport=null,string? consoleTestReport=null,string? lifetimeTestReport=null,string? backupTestReport=null,string? playersTestReport=null)
    {
        SuspendLayout();
        server=manager;previewPath=screenshot;autoStart=auto;preferences=AppPreferences.Load(server.Root,server.Config.KeepAwake);setup=new ClientSetup(server.Root,()=>server);
        if(visualTestReport!=null||consoleTestReport!=null||lifetimeTestReport!=null||backupTestReport!=null||playersTestReport!=null){server.Profile.CurseForgePath="";server.Profile.ClientSyncPath="";}
        if(previewPath==null&&visualTestReport==null&&consoleTestReport==null&&lifetimeTestReport==null&&backupTestReport==null&&playersTestReport==null)setup.Start();
        Text="Minecraft Harbor · "+server.Config.WorldName;BackColor=Bg;ForeColor=Ink;Font=new Font("Segoe UI",10);
        FormBorderStyle=FormBorderStyle.None;ClientSize=new Size(1320,744);MinimumSize=new Size(1160,680);StartPosition=FormStartPosition.CenterScreen;
        Icon=PackArtwork.AppIcon();
        BuildShell();
        var navNames=new[]{"Home","Server Management","Console","Backups","Players","LAN PCs","Settings"};
        var navIcons=new[]{"home","folder","console","backups","players","pc","settings"};
        for(int i=0;i<navNames.Length;i++) {
            string name=navNames[i];var b=new HarborButton{Text=name,Navigation=true,Glyph=navIcons[i],Font=new Font("Segoe UI",9.5f),Cursor=Cursors.Hand};b.SetBounds(6,33+i*52,192,46);b.Click+=(_,_)=>ShowPage(name);rail.Controls.Add(b);navButtons.Add(b);
            var p=new FixedBackdropPanel{Size=new Size(1010,620),Dock=DockStyle.Fill,Visible=false,AutoScroll=true};content.Controls.Add(p);pages[name]=p;
        }
        BuildOverview();BuildManagement();BuildConsole();BuildBackups();BuildPlayers();BuildClientSetup();BuildSettings();ApplyAppPreferences();
        var menu=new ContextMenuStrip();menu.Items.Add("Open Harbor",null,(_,_)=>RevealWindow());
        menu.Items.Add("Stop server and exit",null,async(_,_)=>await ExitSafely());
        tray=new NotifyIcon{Text="Minecraft Harbor",Icon=Icon??SystemIcons.Application,ContextMenuStrip=menu,Visible=true};
        tray.DoubleClick+=(_,_)=>RevealWindow();
        timer.Tick+=(_,_)=>RefreshStatus();timer.Start();
        FormClosing+=(_,e)=>{
            if(exiting||previewPath!=null)return;
            e.Cancel=true;_ = ExitSafely();
        };
        FormClosed+=(_,_)=>{activationCancellation.Cancel();artworkCancellation?.Cancel();performance.Save();timer.Stop();tray.Dispose();setup.Dispose();nightScene.Dispose();wordmark.Dispose();packArt?.Dispose();SetThreadExecutionState(0x80000000);};
        Shown+=async(_,_)=>{
            LayoutDashboard();MaximizedBounds=Screen.FromControl(this).WorkingArea;
            if(visualTestReport=="__app_settings_demo__"){ShowPage("Settings");return;}
            if(visualTestReport?.StartsWith("__app_settings_test__:")==true){await VerifyAppSettings(visualTestReport[22..]);return;}
            if(playersTestReport=="__players_demo__"){ShowPage("Players");return;}
            if(playersTestReport!=null){await VerifyPlayersDesign(playersTestReport);return;}
            if(backupTestReport=="__backups_demo__"){ShowPage("Backups");return;}
            if(backupTestReport!=null){await VerifyBackupDesign(backupTestReport);return;}
            if(lifetimeTestReport!=null){await server.StartAsync();while(server.State==ServerState.Starting)await Task.Delay(20);if(server.State!=ServerState.Running)throw new Exception("Close fixture failed startup");Close();return;}
            if(consoleTestReport=="__command_demo__"){ShowPage("Console");await server.StartAsync();return;}
            if(consoleTestReport!=null){await VerifyConsoleDesign(consoleTestReport);return;}
            if(visualTestReport!=null){await VerifyDesign(visualTestReport);return;}
            if(previewPath!=null){
                await Task.Delay(350);RefreshStatus();
                foreach(var name in pages.Keys){ShowPage(name);await Task.Delay(70);using var bitmap=new Bitmap(Width,Height);DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(name=="Home"?previewPath:Path.Combine(Path.GetDirectoryName(previewPath)!,Path.GetFileNameWithoutExtension(previewPath)+"-"+name+".png"));}
                exiting=true;Close();return;
            }
            _=ListenForActivation();
            RevealWindow();
            applicationShown=true;ShowPage(preferences.RememberPage&&pages.ContainsKey(preferences.LastPage)?preferences.LastPage:"Home");
            if(autoStart)await Run(StartWithEula);
        };
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;ResumeLayout(true);
        ShowPage("Home");RefreshIdentity();RefreshStatus();
    }
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    void RevealWindow()
    {
        Show();if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;
        BringToFront();Activate();SetForegroundWindow(Handle);
    }
    async Task ListenForActivation()
    {
        try{
            while(!activationCancellation.IsCancellationRequested){
                using var pipe=new NamedPipeServerStream("MinecraftHarbor-Show",PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(activationCancellation.Token);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(activationCancellation.Token);timeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var reader=new StreamReader(pipe);
                try{if(await reader.ReadLineAsync(timeout.Token)=="show")RevealWindow();}
                catch(OperationCanceledException) when(!activationCancellation.IsCancellationRequested){}
                catch(IOException){}
            }
        }catch(OperationCanceledException){}catch(IOException ex){server.Log("Harbor activation listener: "+ex.Message);}
    }
    static Label LabelAt(Control parent,string text,int x,int y,int w,int h,float size,Color? color=null,bool bold=false)
    {
        var l=new AlignedLabel{Text=text,Left=x,Top=y,Width=w,Height=h,UseMnemonic=false,ForeColor=color??Ink,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),BackColor=Color.Transparent};parent.Controls.Add(l);return l;
    }
    static Button MakeButton(string text,int width=150,bool primary=false)
    {
        var b=new HarborButton{Text=text,Primary=primary,Width=width,Height=42,FlatStyle=FlatStyle.Flat,BackColor=Color.Transparent,ForeColor=Ink,Cursor=Cursors.Hand,Font=new Font("Segoe UI",10,FontStyle.Bold),UseVisualStyleBackColor=false};
        b.FlatAppearance.BorderColor=primary?Mint:Edge;b.FlatAppearance.BorderSize=1;return b;
    }
    static void CopyButtonStyle(Button target,string text,bool primary=false)
    {
        target.Text=text;target.Size=new Size(155,43);target.FlatStyle=FlatStyle.Flat;target.BackColor=Color.Transparent;target.ForeColor=Ink;target.FlatAppearance.BorderColor=primary?Mint:Edge;target.Font=new Font("Segoe UI",10,FontStyle.Bold);target.Cursor=Cursors.Hand;if(target is HarborButton styled)styled.Primary=primary;
    }
    static void TextStyle(TextBox box,bool mono=false){box.BackColor=Color.FromArgb(10,17,22);box.ForeColor=Ink;box.BorderStyle=BorderStyle.FixedSingle;box.Font=new Font(mono?"Consolas":"Segoe UI",mono?9:11);}
    void Heading(Panel p,string title,string sub)
    {
        LabelAt(p,title,0,0,850,43,27,Ink,true);LabelAt(p,sub,0,50,850,44,10,Muted);
    }
    void ShowPage(string name)
    {
        managementGeneration++;
        FinishHomeEntrance();
        FinishConsoleEntrance();
        FinishPageEntrance();
        // Hidden docked pages have not received their final layout yet. Settle
        // bounds and rows before placing the fade cover or exposing the page.
        content.PerformLayout();pages[name].Bounds=content.DisplayRectangle;pages[name].PerformLayout();
        if(name=="Backups")arrangeBackupPage();if(name=="Players")arrangePlayerPage();
        PreparePageEntrance(name);
        foreach(var pair in pages)pair.Value.Visible=pair.Key==name;
        foreach(var b in navButtons){if(b is HarborButton styled)styled.Selected=b.Text==name;b.Invalidate();}
        if(name=="Backups")RefreshBackups();if(name=="Players")RefreshRoster();
        if(name=="LAN PCs")RefreshSetup();
        if(name=="Server Management"&&!creatingServer){draftServerName="";RenderServerList();}
        if(name=="Home")AnimateHomeEntrance();
        if(name=="Console")AnimateConsoleEntrance();
        AnimatePageEntrance();
        if(applicationShown&&preferences.RememberPage&&preferences.LastPage!=name){preferences.LastPage=name;try{preferences.Save(server.Root);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){server.Log("App preferences could not save: "+ex.Message);}}
    }
    async Task StartWithEula()
    {
        if(!server.EulaAccepted) {
            using var dialog=new Form{Text="Minecraft server agreement",ClientSize=new Size(560,255),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,BackColor=Bg,ForeColor=Ink,Font=Font};
            LabelAt(dialog,"One step before your first start",22,20,510,35,19,Ink,true);
            LabelAt(dialog,"Minecraft requires the server owner to accept its EULA.\nRead it below, then accept if you agree. This is saved locally\nin the server's eula.txt file.",24,72,510,70,11,Muted);
            var link=new LinkLabel{Text="Read the Minecraft EULA",Left=24,Top=149,Width=470,Height=25,LinkColor=Mint};dialog.Controls.Add(link);link.LinkClicked+=(_,_)=>OpenPath("https://www.minecraft.net/eula");
            var accept=MakeButton("Accept & start",185,true);accept.SetBounds(345,199,185,40);accept.DialogResult=DialogResult.OK;dialog.Controls.Add(accept);
            var cancel=MakeButton("Not now",130);cancel.SetBounds(197,199,130,40);cancel.DialogResult=DialogResult.Cancel;dialog.Controls.Add(cancel);dialog.CancelButton=cancel;
            ScaleDialog(dialog);if(dialog.ShowDialog(this)!=DialogResult.OK)return;server.AcceptEula();
        }
        await server.StartAsync();
    }
    async Task EnableLan()
    {
        var startInfo=new ProcessStartInfo("powershell.exe"){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,Arguments="-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(server.Root,"Enable-LAN.ps1")+"\""};
        using var p=Process.Start(startInfo)??throw new IOException("Windows could not open the LAN setup.");await p.WaitForExitAsync();
        if(p.ExitCode!=0)throw new IOException("LAN setup did not complete. Check logs/lan-setup.log or retry and approve the Windows administrator prompt.");
        server.Log("LAN firewall rules enabled for Minecraft and desktop setup.");MessageBox.Show(this,"LAN access is enabled.\n\nOpen "+setup.Url+" on your desktop for setup.\nKeep both computers on the same home network.","Ready for your desktop");
    }
    static void OpenPath(string path)=>Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
    async Task Run(Func<Task> action)
    {
        try{await action();}catch(OperationCanceledException){server.Log("Operation cancelled. Your current server is unchanged.");}catch(Exception e){server.Log("Harbor: "+e.Message);MessageBox.Show(this,e.Message,"Minecraft Harbor",MessageBoxButtons.OK,MessageBoxIcon.Information);}RefreshIdentity();RefreshStatus();
    }
    async Task ExitSafely()
    {
        if(closeInProgress||exiting)return;closeInProgress=true;Enabled=false;packPreparation?.Cancel();
        try{while(server.Busy)await Task.Delay(200);if(server.HasProcess)await server.CloseAsync();exiting=true;Close();}
        catch(Exception e){Enabled=true;closeInProgress=false;MessageBox.Show(this,e.Message+"\n\nHarbor stayed open so your world is not force-closed.","Could not finish saving");}
    }
    void RefreshStatus()
    {
        var stateText=server.State switch{ServerState.Starting=>"●  Starting up",ServerState.Running=>"●  Online",ServerState.Stopping=>"●  Saving & stopping",ServerState.Crashed=>"●  Needs attention",_=>"●  Offline"};
        state.Text=stateText;state.ForeColor=server.State==ServerState.Running?Mint:server.State==ServerState.Crashed?Color.Salmon:Muted;
        activity.Text=server.SavingNeedsResume&&!server.Busy?"Saving needs attention — retrying save-on; see Console":server.Activity;address.Text=ServerManager.LanAddress()+":25565";players.Text=server.OnlinePlayers+" / "+server.Config.MaxPlayers;
        memory.Text=(server.MemoryBytes/1073741824d).ToString("0.0")+" / "+server.Config.MemoryGB+" GB";
        start.Enabled=!server.Profile.Deleted&&!server.Busy&&!server.HasProcess;stop.Enabled=!server.Busy&&server.HasProcess;
        restart.Enabled=!server.Busy&&server.State==ServerState.Running;backup.Enabled=!server.Profile.Deleted&&!server.Busy&&(!server.HasProcess||server.State==ServerState.Running);
        uptime.Text=server.StartedAt.HasValue?"Session time  "+(DateTime.Now-server.StartedAt.Value).ToString(@"hh\:mm\:ss"):"Original singleplayer save preserved";
        var latest=new DirectoryInfo(server.BackupDir).GetFiles("*.zip").OrderByDescending(f=>f.LastWriteTime).FirstOrDefault();backupStatus.Text=latest==null?"No backups yet":"Last backup  "+latest.LastWriteTime.ToString("MMM d, HH:mm");
        footer.Text=server.EulaAccepted?"Hourly backups · Five-hour history":"First start: review and accept the Minecraft EULA.";
        RefreshBackupStatus();RefreshAppNotifications(latest);
        var logBatch=new List<string>();int count=0;while(count++<600&&server.Lines.TryDequeue(out var line)){logBatch.Add(line);recent.Add(line);}console.AppendLines(logBatch);
        if(recent.Count>6)recent.RemoveRange(0,recent.Count-6);preview.Text=string.Join(Environment.NewLine,recent);preview.SelectionStart=preview.TextLength;preview.ScrollToCaret();
        if(previewPath==null)SetThreadExecutionState(preferences.KeepAwake&&server.HasProcess?0x80000001:0x80000000);
        if(++tick%50==0&&server.State==ServerState.Running&&!server.Busy){try{server.Send("list");}catch{}}
        RefreshLibraryActivity();
        RefreshDashboard();
        RefreshConsoleStatus();
        RefreshPlayerStatus();
        if(pages["LAN PCs"].Visible&&tick%10==0)RefreshSetup();
        if(previewPath==null&&!closeInProgress&&!automaticCheckRunning&&!server.Busy&&server.State==ServerState.Running&&(server.SavingNeedsResume||server.NextAutomaticBackupUtc<=DateTime.UtcNow))_ = CheckAutomaticBackup();
    }
    async Task CheckAutomaticBackup()
    {
        automaticCheckRunning=true;
        try{if(await server.TryAutomaticBackupAsync())RefreshBackups();}
        catch(Exception ex){server.Log("Hourly backup could not finish: "+ex.Message);if(preferences.NotifyErrors)NotifyInBackground("Backup needs attention",ex.Message,ToolTipIcon.Warning);}
        finally{automaticCheckRunning=false;}
    }
    void RefreshIdentity()
    {
        Text="Minecraft Harbor · "+server.Config.WorldName;worldTitle.Text=server.Config.WorldName;packSubtitle.Text=server.Profile+"  /  Minecraft "+server.Profile.MinecraftVersion+"  /  Home network";
        railFooter.Text=server.Profile.Name.ToUpperInvariant()+"\n"+server.Profile.PackVersion+" · Minecraft "+server.Profile.MinecraftVersion+"\n\nA world of your own.";
        settingsInfo.Text="Minecraft "+server.Profile.MinecraftVersion+"  ·  "+server.Profile.Loader+" "+server.Profile.LoaderVersion+"\nLAN port: 25565  ·  Account authentication enabled  ·  Remote console disabled\nClosing Harbor saves and stops the server.";
        
        RefreshSetup();RefreshBackups();RefreshLibrary();
        performance.Select(server.ProfileRoot,server.Config.WorldFolder,DateTime.UtcNow);RefreshArtwork();
    }
}




