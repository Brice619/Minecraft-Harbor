using System.Diagnostics;
using System.Text.Json;

namespace HarborAgent;
internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        try{if(args.Length==2&&args[0]=="--shutdown-test"){UpdateTests.Shutdown(args[1]);return 0;}if(args.Length==2&&args[0]=="--shutdown-fixture"){Application.EnableVisualStyles();UpdateTests.Fixture(args[1]);return 0;}if(args.Length==2&&args[0]=="--loader-install-test"){LoaderUpdateTests.Install(args[1]).GetAwaiter().GetResult();return 0;}if(args.Length==2&&args[0]=="--self-test"){PackAgentTests.Run(args[1]).GetAwaiter().GetResult();return 0;}Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length==2&&args[0]=="--preview"){Application.Run(new ClientForm(args[1]));return 0;}
            if(Path.GetFileName(Environment.ProcessPath)!= "Minecraft Harbor Client.exe"){Application.Run(new InstallForm());return 0;}
            using var mutex=new Mutex(true,"Local\\MinecraftHarborClient",out bool first);if(!first){MessageBox.Show("Harbor Client is already open. Use its window to select and launch a pack.","Minecraft Harbor Client");return 0;}
            Application.Run(new ClientForm());return 0;
        }catch(Exception ex){if(args.Length==2)File.WriteAllText(args[1]+".error.txt",ex.ToString());else MessageBox.Show(ex.Message,"Minecraft Harbor Client");return 1;}
    }
}
internal sealed class InstallForm:Form
{
    public InstallForm()
    {
        bool updating=ClientInstallation.Exists(ClientConfig.Root),busy=false,finished=false;
        SuspendLayout();ClientUi.Style(this,"Minecraft Harbor Client 1.2 Setup",620,310);
        ClientUi.Label(this,updating?"Update Harbor Client":"Minecraft Harbor Client",25,25,570,42,22,true);
        var message=ClientUi.Label(this,updating?"Update to version 1.2.":"Install Minecraft Harbor Client 1.2.",27,84,566,95,11);
        var install=ClientUi.Button(this,updating?"Update Client":"Install Client",25,225,280,true);var cancel=ClientUi.Button(this,"Cancel",321,225,272);cancel.Click+=(_,_)=>Close();
        install.Click+=async(_,_)=>
        {
            string exe=Path.Combine(ClientConfig.Root,"Minecraft Harbor Client.exe");
            if(finished){Process.Start(new ProcessStartInfo(exe){UseShellExecute=true});Close();return;}
            busy=true;message.Text=updating?"Closing Harbor Client and updating…":"Installing…";install.Enabled=cancel.Enabled=false;
            try{await Task.Run(()=>ClientInstallation.Install(Environment.ProcessPath!,ClientConfig.Root));CreateShortcut(exe);finished=true;message.Text=updating?"Update complete.":"Installation complete.";install.Text="Open Client";cancel.Text="Close";}
            catch(Exception ex){ClientDiagnostics.Record(ex);message.Text=ex.Message;}
            finally{busy=false;install.Enabled=cancel.Enabled=true;}
        };
        FormClosing+=(_,e)=>e.Cancel=busy;ClientUi.Finish(this);
    }
    static void CreateShortcut(string exe){Type type=Type.GetTypeFromProgID("WScript.Shell")??throw new IOException("Windows shortcut service unavailable");dynamic shell=Activator.CreateInstance(type)!;try{dynamic link=shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Minecraft Harbor Client.lnk"));try{link.TargetPath=exe;link.WorkingDirectory=ClientConfig.Root;link.Description="Update your existing Minecraft profile, then launch";link.IconLocation=exe+",0";link.Save();}finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);}}finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}}
}
internal static class ClientUi
{
    internal static readonly Color Bg=Color.FromArgb(13,23,33),Ink=Color.FromArgb(239,246,252),Muted=Color.FromArgb(175,193,215),Card=Color.FromArgb(24,43,59),Green=Color.FromArgb(37,207,119);
    public static void Style(Form form,string title,int w,int h){form.Text=title;form.ClientSize=new(w,h);form.StartPosition=FormStartPosition.CenterScreen;form.BackColor=Bg;form.ForeColor=Ink;form.Font=new("Segoe UI",11);form.FormBorderStyle=FormBorderStyle.FixedSingle;form.MaximizeBox=false;form.Icon=Icon.ExtractAssociatedIcon(Environment.ProcessPath!);}
    public static void Finish(Form form){form.AutoScaleDimensions=new(96,96);form.AutoScaleMode=AutoScaleMode.Dpi;form.ResumeLayout(true);}
    public static Label Label(Control owner,string text,int x,int y,int w,int h,float size=11,bool bold=false){var l=new Label{Text=text,Bounds=new(x,y,w,h),ForeColor=bold?Ink:Muted,Font=new("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),TextAlign=ContentAlignment.MiddleLeft};owner.Controls.Add(l);return l;}
    public static Button Button(Control owner,string text,int x,int y,int w,bool primary=false){var b=new Button{Text=text,Bounds=new(x,y,w,44),BackColor=primary?Green:Card,ForeColor=Ink,FlatStyle=FlatStyle.Flat,Font=new("Segoe UI",11,FontStyle.Bold),TextAlign=ContentAlignment.MiddleCenter,UseMnemonic=false,Cursor=Cursors.Hand};b.FlatAppearance.BorderColor=Color.FromArgb(55,94,121);owner.Controls.Add(b);return b;}
    public static TextBox Input(Control owner,int x,int y,int w,string text=""){var t=new TextBox{Bounds=new(x,y,w,33),BackColor=Card,ForeColor=Ink,BorderStyle=BorderStyle.FixedSingle,Text=text};owner.Controls.Add(t);return t;}
}
internal sealed class ClientForm:Form
{
    readonly ClientConfig config=ClientConfig.Load();readonly ClientCore core;
    readonly TextBox host,code,folder;readonly Label status;readonly Button launch,pair,other;
    readonly System.Windows.Forms.Timer heartbeat=new(){Interval=15000};bool working,reporting;
    public ClientForm(string? preview=null)
    {
        SuspendLayout();core=new(config);ClientUi.Style(this,"Minecraft Harbor Client",780,540);ClientUi.Label(this,"Minecraft Harbor Client",26,22,728,43,24,true);ClientUi.Label(this,"Update your existing pack, then play.",28,70,725,30,12);
        ClientUi.Label(this,"Harbor Connection Address",28,119,520,26,11,true);host=ClientUi.Input(this,28,151,500,config.Host);ClientUi.Label(this,"Pairing Code",552,119,200,26,11,true);code=ClientUi.Input(this,552,151,200);code.MaxLength=6;code.PlaceholderText="From LAN PCs";
        ClientUi.Label(this,"Your Selected Modpack Folder",28,203,700,27,11,true);folder=ClientUi.Input(this,28,237,575,config.Profile);folder.ReadOnly=true;var browse=ClientUi.Button(this,"Browse",622,234,130);browse.Click+=(_,_)=>SelectFolder();
        status=ClientUi.Label(this,"Select a folder and pair with your server once.",28,297,724,74,12);core.Progress=text=>status.Text=text;pair=ClientUi.Button(this,"Save & Pair",28,392,200);pair.Click+=async(_,_)=>await Pair();other=ClientUi.Button(this,"Launch Another Copy",244,392,235);other.Click+=async(_,_)=>await Other();launch=ClientUi.Button(this,"Update & Launch",495,392,257,true);launch.Click+=async(_,_)=>await Launch(config.Profile);
        ClientUi.Label(this,"Updates mods and matching NeoForge before launch. Close Minecraft first.\nCurseForge closes automatically when the loader needs updating.",28,458,724,57,10);
        heartbeat.Tick+=async(_,_)=>{if(working||reporting||config.Token.Length==0||config.Host.Length==0||config.Profile.Length==0)return;reporting=true;try{var info=await core.Info();await core.Report(config.Profile,info,"Connected");}catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException or IOException or System.Management.ManagementException){}finally{reporting=false;}};
        Shown+=async(_,_)=>{if(preview!=null){using var image=new Bitmap(Width,Height);DrawToBitmap(image,new(Point.Empty,Size));image.Save(preview);Close();return;}heartbeat.Start();if(config.Token.Length>0&&config.Profile.Length>0){status.Text="Checking your selected pack…";await Launch(config.Profile);}};
        FormClosing+=(_,e)=>{if(working)e.Cancel=true;};FormClosed+=(_,_)=>{heartbeat.Dispose();core.Dispose();};ClientUi.Finish(this);
    }
    string? BrowseFolder(){using var dialog=new FolderBrowserDialog{Description="Select your existing CurseForge modpack folder",UseDescriptionForTitle=true,ShowNewFolderButton=false};return dialog.ShowDialog(this)==DialogResult.OK?dialog.SelectedPath:null;}
    void SelectFolder(){string? selected=BrowseFolder();if(selected==null)return;try{ClientProfile.Read(selected);if(config.Profile.Length>0&&Path.GetFullPath(selected)!=Path.GetFullPath(config.Profile)&&MessageBox.Show(this,"Replace your normally selected modpack folder? Use Launch Another Copy for a one-time launch instead.","Change Selected Folder",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;config.Profile=selected;folder.Text=selected;config.Save();status.Text="Selected folder saved.";}catch(Exception ex){status.Text=ex.Message;}}
    async Task Pair(){if(working)return;working=true;SetButtons(false);string oldHost=config.Host,oldToken=config.Token;bool paired=false;try{ClientProfile.Read(config.Profile);config.Host=ClientCore.NormalizeHost(host.Text.Trim());config.Token="";var info=await core.Info();if(!info.FullPack)throw new InvalidOperationException("Choose a client pack folder in the Harbor LAN PCs page first.");await core.Pair(code.Text);paired=true;code.Clear();status.Text="Paired. Your pack will be checked each time you open this client.";await core.Report(config.Profile,info,"Connected");}catch(Exception ex){if(!paired){config.Host=oldHost;config.Token=oldToken;}status.Text=ex.Message;}finally{working=false;SetButtons(true);}}
    async Task CheckOnOpen(){if(working)return;working=true;SetButtons(false);try{var info=await core.Info();if(ClientCore.IsRunning(config.Profile)){status.Text="Minecraft is running. Updates will wait until it is closed.";return;}status.Text=await core.Sync(config.Profile,info);await core.Report(config.Profile,info,status.Text);}catch(HttpRequestException){status.Text="Harbor is unreachable. You can choose to launch without updates.";}catch(TaskCanceledException){status.Text="Harbor did not respond. You can choose to launch without updates.";}catch(Exception ex){ClientDiagnostics.Record(ex);status.Text=ex.Message;}finally{working=false;SetButtons(true);}}
    async Task Other(){string? selected=BrowseFolder();if(selected==null)return;try{var original=ClientProfile.Read(config.Profile);var alternate=ClientProfile.Read(selected);if(Path.GetFullPath(selected)==Path.GetFullPath(config.Profile)){await Launch(selected);return;}if(!original.SamePack(alternate)){MessageBox.Show(this,"Choose another copy of "+original.Name+" from the same pack project.","Different Modpack");return;}var answer=MessageBox.Show(this,"Sync this copy for this launch only?\n\n"+selected+"\n\nYour selected folder will stay unchanged.","Another Copy of "+original.Name,MessageBoxButtons.YesNoCancel);if(answer==DialogResult.Cancel)return;await Launch(selected,answer==DialogResult.Yes);}catch(Exception ex){status.Text=ex.Message;}}
    async Task Launch(string target,bool sync=true)
    {
        if(working)return;working=true;SetButtons(false);
        try{var profile=ClientProfile.Read(target);if(ClientCore.IsRunning(target)){MessageBox.Show(this,"This copy is already running. Close Minecraft before checking updates or launching it again.","Minecraft Is Running");return;}
            if(sync){try{var info=await core.Info();status.Text=await core.Sync(target,info);await core.Report(target,info,status.Text);}catch(Exception ex)when(ex is HttpRequestException{StatusCode:null} or TaskCanceledException){if(MessageBox.Show(this,"Harbor is unreachable. Launch this pack without checking for updates?", "Server Unavailable",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;}}
            Process.Start(new ProcessStartInfo(profile.LaunchUri){UseShellExecute=true});status.Text="Launch sent to CurseForge for "+profile.Name+".";
        }catch(Exception ex){ClientDiagnostics.Record(ex);status.Text=ex.Message;}finally{working=false;SetButtons(true);}
    }
    void SetButtons(bool enabled){pair.Enabled=other.Enabled=launch.Enabled=enabled;}
}








