using System.Diagnostics;
using System.Text.Json;

namespace HarborClientSetup;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try {
            if(args.Length==2&&args[0]=="--self-test"){Tests.Run(args[1]);return 0;}
            if(args.Length==2&&args[0]=="--probe"){File.WriteAllText(args[1],JsonSerializer.Serialize(Installer.Discover(),new JsonSerializerOptions{WriteIndented=true}));return 0;}
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(args.Length==2&&args[0]=="--preview"?args[1]:null));return 0;
        } catch(Exception ex){
            if(args.Length==2){File.WriteAllText(args[1]+".error.txt",ex.ToString());}
            else MessageBox.Show(ex.Message,"Minecraft Harbor setup",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
}

public sealed class SetupForm:Form
{
    static readonly Color Ink=Color.FromArgb(231,239,241),Muted=Color.FromArgb(159,177,183),Mint=Color.FromArgb(125,226,182),Bg=Color.FromArgb(16,23,29),Card=Color.FromArgb(25,35,43);
    readonly ComboBox profiles=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    readonly Label path=new(),status=new();
    readonly Button enable=new(),browse=new(),rescan=new(),open=new();
    bool working,complete;
    public SetupForm(string? preview)
    {
        SuspendLayout();
        Text="Minecraft Harbor · Client setup";ClientSize=new Size(760,655);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",11);ForeColor=Ink;BackColor=Bg;
        Icon=Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        LabelAt("MINECRAFT HARBOR / ONE-TIME SETUP",32,27,695,23,10,Mint,true);
        LabelAt("Keep your ATM10.",30,63,695,55,29,Ink,true);
        LabelAt("Add automatic custom-mod updates to the profile you already play.",32,125,695,32,11,Muted);
        LabelAt("YOUR EXISTING PROFILE",32,179,690,23,10,Mint,true);
        profiles.SetBounds(32,215,520,34);profiles.BackColor=Card;profiles.ForeColor=Ink;profiles.FlatStyle=FlatStyle.Flat;profiles.DrawMode=DrawMode.OwnerDrawFixed;profiles.ItemHeight=26;
        profiles.DrawItem+=(_,e)=>{using var brush=new SolidBrush(Card);e.Graphics.FillRectangle(brush,e.Bounds);var text=e.Index>=0?profiles.Items[e.Index]?.ToString():"Select your existing profile";TextRenderer.DrawText(e.Graphics,text,profiles.Font,e.Bounds,Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};Controls.Add(profiles);
        Style(browse,"Browse…",566,214,162,false);browse.Click+=(_,_)=>Browse();
        path.SetBounds(32,263,695,52);path.Font=new Font("Segoe UI",9);path.ForeColor=Muted;Controls.Add(path);
        Style(rescan,"Look again",32,320,145,false);rescan.Click+=(_,_)=>Scan();
        LabelAt("ATM10 8.1  ·  Minecraft 1.21.1  ·  NeoForge 21.1.249",195,330,530,25,10,Muted);
        LabelAt("Close Minecraft, then enable sync.",32,384,695,30,14,Ink,true);
        LabelAt("Adds the AutoModpack helper. Your saves, settings and other mods stay in place.\nAn older helper is backed up before replacement. No new profile is created.",32,424,695,52,10,Muted);
        status.SetBounds(32,491,695,78);status.Font=new Font("Segoe UI",10);status.ForeColor=Ink;Controls.Add(status);
        Style(enable,"Enable sync",32,585,210,true);enable.Click+=async(_,_)=>await Install();
        Style(open,"Open profile folder",258,585,218,false);open.Visible=false;open.Click+=(_,_)=>{if(profiles.SelectedItem is Profile p)Process.Start(new ProcessStartInfo(p.Path){UseShellExecute=true});};
        var close=new Button();Style(close,"Close",598,585,130,false);close.Click+=(_,_)=>Close();
        profiles.SelectedIndexChanged+=(_,_)=>Selected();
        FormClosing+=(_,e)=>{if(working)e.Cancel=true;};
        Shown+=(_,_)=>{
            Scan();
            if(preview!=null){using var bitmap=new Bitmap(Width,Height);DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(preview);Close();}
        };
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;ResumeLayout(true);
    }
    void LabelAt(string text,int x,int y,int w,int h,int size,Color color,bool bold=false)=>Controls.Add(new Label{Text=text,Left=x,Top=y,Width=w,Height=h,ForeColor=color,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular)});
    void Style(Button b,string text,int x,int y,int w,bool primary)
    {
        b.Text=text;b.SetBounds(x,y,w,43);b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=Color.FromArgb(45,61,70);b.BackColor=primary?Mint:Card;b.ForeColor=primary?Bg:Ink;b.Font=new Font("Segoe UI",11,FontStyle.Bold);b.Cursor=Cursors.Hand;Controls.Add(b);
    }
    void Scan()
    {
        profiles.Items.Clear();foreach(var p in Installer.Discover())profiles.Items.Add(p);
        if(profiles.Items.Count==1)profiles.SelectedIndex=0;
        else {enable.Enabled=false;path.Text=profiles.Items.Count==0?"No matching profile found in the usual folders. Use Browse to choose yours.":"Choose the profile you use to play. Check its folder below before enabling sync.";status.Text="In CurseForge: your ATM10 profile → ⋮ → Open Folder.";}
    }
    void Selected()
    {
        complete=false;open.Visible=false;enable.Text="Enable sync";enable.Enabled=profiles.SelectedItem is Profile;
        path.Text=(profiles.SelectedItem as Profile)?.Path??"";status.Text="Ready to add the helper to this profile.";status.ForeColor=Ink;
    }
    void Browse()
    {
        using var picker=new FolderBrowserDialog{Description="Choose your existing ATM10 profile folder (the folder containing mods).",UseDescriptionForTitle=true,ShowNewFolderButton=false};
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        try{var p=Installer.Validate(picker.SelectedPath);int index=profiles.Items.Cast<Profile>().ToList().FindIndex(x=>x.Path.Equals(p.Path,StringComparison.OrdinalIgnoreCase));if(index<0)index=profiles.Items.Add(p);profiles.SelectedIndex=index;}
        catch(Exception ex){status.Text=ex.Message;status.ForeColor=Color.FromArgb(255,188,137);}
    }
    async Task Install()
    {
        if(profiles.SelectedItem is not Profile p||working)return;
        working=true;enable.Enabled=browse.Enabled=rescan.Enabled=profiles.Enabled=false;status.ForeColor=Ink;status.Text="Checking the profile and installing the verified helper…";
        try{
            var result=await Task.Run(()=>Installer.Install(p.Path));complete=true;enable.Text="Sync is enabled";status.ForeColor=Mint;
            status.Text=(result.AlreadyInstalled?"The helper is already ready.":"The helper is installed.")+" Launch this same profile and join Harbor.\nApprove the custom Ultimine update, then restart Minecraft when prompted."+(result.BackupPath!=null?"\nPrevious helper saved in this profile’s harbor-sync-backups folder.":"");
            open.Visible=true;
        }catch(Exception ex){status.Text=ex.Message;status.ForeColor=Color.FromArgb(255,188,137);}
        finally{working=false;browse.Enabled=rescan.Enabled=profiles.Enabled=true;enable.Enabled=!complete;}
    }
}
