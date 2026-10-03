using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MinecraftHarbor;
public sealed partial class MainForm
{
    readonly HarborSurface rail=new(){Dock=DockStyle.Left,Width=208,Role="rail"};
    readonly HarborSurface header=new(){Dock=DockStyle.Top,Height=58,Role="header"};
    readonly HarborSurface statusBar=new(){Dock=DockStyle.Bottom,Height=30,Role="footer"};
    readonly PaintedPanel dashboard=new(){Dock=DockStyle.Fill};
    readonly PerformanceHistory performance=new();
    readonly PerformanceGraph graph=new();
    readonly HarborDropdown metricChoice=new();
    readonly HarborButton copyConnection=new(){Glyph="copy",AccessibleName="Copy server address",Cursor=Cursors.Hand};
    readonly ToolTip tips=new();
    readonly System.Windows.Forms.Timer homeEntrance=new(){Interval=15};
    readonly System.Diagnostics.Stopwatch homeEntranceTime=new();
    HomeFadeOverlay? homeFade;
    AppearanceSettings appearance=new();
    Bitmap nightScene=null!;
    Bitmap wordmark=null!;
    Rectangle wordmarkInk;
    Bitmap? packArt;
    CancellationTokenSource? artworkCancellation;
    string? artworkKey;
    readonly Label artworkStatus=new();
    string dashboardSignature="";
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wparam,IntPtr lparam);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);try{int round=2;DwmSetWindowAttribute(Handle,33,ref round,4);}catch(DllNotFoundException){}
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if(m.Msg!=0x84||WindowState!=FormWindowState.Normal)return;
        var point=PointToClient(new Point((short)((long)m.LParam&0xffff),(short)(((long)m.LParam>>16)&0xffff)));
        int edge=LogicalToDeviceUnits(6);bool left=point.X<edge,right=point.X>=Width-edge,top=point.Y<edge,bottom=point.Y>=Height-edge;
        if(top)m.Result=(IntPtr)(left?13:right?14:12);else if(bottom)m.Result=(IntPtr)(left?16:right?17:15);else if(left)m.Result=(IntPtr)10;else if(right)m.Result=(IntPtr)11;
    }
    void BuildShell()
    {
        appearance=AppearanceSettings.Load(server.Root);nightScene=PackArtwork.NightScene();wordmark=PackArtwork.Wordmark();wordmarkInk=PackArtwork.VisibleBounds(wordmark);content.Artwork=rail.Artwork=nightScene;
        Controls.Add(content);Controls.Add(rail);Controls.Add(statusBar);Controls.Add(header);
        header.Paint+=(_,e)=>{
            float s=DeviceDpi/96f;var g=e.Graphics;var saved=g.Save();g.ScaleTransform(s,s);g.SmoothingMode=SmoothingMode.AntiAlias;
            float h=header.Height/s;HarborTheme.GrassBlock(g,21,(h-36)/2,36);
            float scale=Math.Min(34f/wordmarkInk.Height,310f/wordmarkInk.Width),logoW=wordmarkInk.Width*scale,logoH=wordmarkInk.Height*scale;
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(wordmark,new RectangleF(68,(h-logoH)/2,logoW,logoH),wordmarkInk,GraphicsUnit.Pixel);
            g.Restore(saved);
        };
        header.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};
        header.DoubleClick+=(_,_)=>ToggleMaximize();
        var chrome=new List<HarborButton>();
        foreach(var glyph in new[]{"min","max","close"}){
            var b=new HarborButton{Glyph=glyph,Chrome=true,AccessibleName=glyph=="min"?"Minimize":glyph=="max"?"Maximize or restore":"Close Harbor",Cursor=Cursors.Hand};header.Controls.Add(b);chrome.Add(b);
            b.Click+=(_,_)=>{if(glyph=="min")WindowState=FormWindowState.Minimized;else if(glyph=="max")ToggleMaximize();else Close();};
        }
        void LayoutChrome(){int w=LogicalToDeviceUnits(38),h=LogicalToDeviceUnits(32);for(int i=0;i<chrome.Count;i++)chrome[i].SetBounds(header.Width-(3-i)*w-LogicalToDeviceUnits(4),(header.Height-h)/2,w,h);}
        header.SizeChanged+=(_,_)=>LayoutChrome();LayoutChrome();
        statusBar.Paint+=(_,e)=>{
            var g=e.Graphics;var saved=g.Save();float s=DeviceDpi/96f;g.ScaleTransform(s,s);float w=statusBar.Width/s;
            HarborTheme.Text(g,"© "+DateTime.Now.Year+" Rise Digital. All rights reserved.",10.5f,22,7,Color.FromArgb(141,155,175),false,w-44,true);
            g.Restore(saved);
        };
    }
    void ToggleMaximize(){MaximizedBounds=Screen.FromControl(this).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
    void BuildOverview()
    {
        var page=pages["Home"];page.AutoScroll=false;page.Controls.Add(dashboard);dashboard.Draw=DrawDashboard;
        homeEntrance.Tick+=(_,_)=>{
            if(!Visible||!page.Visible){FinishHomeEntrance();return;}
            double progress=Math.Min(1,homeEntranceTime.Elapsed.TotalMilliseconds/preferences.FadeMilliseconds);
            if(homeFade!=null){homeFade.Alpha=(float)(progress*progress*(3-2*progress));homeFade.Invalidate();}
            if(progress>=1)FinishHomeEntrance();
        };
        Disposed+=(_,_)=>{homeEntrance.Dispose();homeFade?.Dispose();};
        CopyButtonStyle(start,"Start Server",true);((HarborButton)start).Glyph="play";
        CopyButtonStyle(stop,"Stop Server",true);((HarborButton)stop).Glyph="stop";((HarborButton)stop).Danger=true;
        CopyButtonStyle(restart,"Restart");((HarborButton)restart).Glyph="restart";
        CopyButtonStyle(backup,"Backup");((HarborButton)backup).Glyph="backups";
        dashboard.Controls.AddRange(new Control[]{start,stop,restart,backup,copyConnection,graph,metricChoice});
        start.Click+=async(_,_)=>await Run(StartWithEula);stop.Click+=async(_,_)=>await ConfirmedServerAction(false);restart.Click+=async(_,_)=>await ConfirmedServerAction(true);backup.Click+=async(_,_)=>await BackupClick();
        copyConnection.Click+=(_,_)=>{Clipboard.SetText(ServerManager.LanAddress()+":25565");tips.Show("Server address copied",copyConnection,0,-28,1600);};tips.SetToolTip(copyConnection,"Copy server address");
        metricChoice.BackColor=Color.FromArgb(23,30,38);metricChoice.ForeColor=Muted;metricChoice.Font=new Font("Segoe UI",9);
        metricChoice.Items.AddRange(new object[]{"CPU Usage","Memory"});metricChoice.SelectedIndex=0;metricChoice.AccessibleName="Performance metric";
        metricChoice.SelectedIndexChanged+=(_,_)=>{graph.ShowMemory=metricChoice.SelectedIndex==1;graph.Invalidate();};
        graph.History=performance;tips.SetToolTip(graph,"Server CPU as a percentage of total PC capacity. Memory is the server process's actual RAM use. Samples every five seconds; last hour only.");
        dashboard.SizeChanged+=(_,_)=>{FinishHomeEntrance();LayoutDashboard();};LayoutDashboard();
    }
    void AnimateHomeEntrance()
    {
        FinishHomeEntrance();
        if(!IsHandleCreated||!Visible||!MotionEnabled)return;
        LayoutDashboard();
        var snapshot=new Bitmap(dashboard.Width,dashboard.Height);dashboard.DrawToBitmap(snapshot,dashboard.ClientRectangle);
        homeFade=new HomeFadeOverlay(snapshot){Bounds=dashboard.Bounds};pages["Home"].Controls.Add(homeFade);homeFade.BringToFront();
        homeEntranceTime.Restart();homeEntrance.Start();
    }
    void FinishHomeEntrance()
    {
        homeEntrance.Stop();homeEntranceTime.Reset();
        if(homeFade!=null){var overlay=homeFade;homeFade=null;overlay.Parent?.Controls.Remove(overlay);overlay.Dispose();}
    }
    (float W,float H,float Y,float CardHeight,float Left,float Gap) DashboardGeometry()
    {
        float s=dashboard.DeviceDpi/96f,w=dashboard.Width/s,h=dashboard.Height/s;float y=192,gap=14,left=(w-gap)*.445f;
        float cardHeight=Math.Max(234,h-y-150);return(w,h,y,cardHeight,left,gap);
    }
    void Place(Control c,float x,float y,float w,float h){float s=dashboard.DeviceDpi/96f;c.SetBounds((int)Math.Round(x*s),(int)Math.Round(y*s),(int)Math.Round(w*s),(int)Math.Round(h*s));}
    RectangleF ConnectionBox(){var d=DashboardGeometry();float x=d.Left>=445?190:135;return new RectangleF(x,d.Y+70,d.Left-x-74,39);}
    void LayoutDashboard()
    {
        if(dashboard.Width<100||graph.Parent==null)return;
        var d=DashboardGeometry();float button=Math.Min(223,(d.W-50)/3.8f);
        Place(start,5,115,button,54);Place(stop,5,115,button,54);Place(restart,button+18,115,196,54);Place(backup,button+227,115,196,54);
        float right=d.Left+d.Gap;var connection=ConnectionBox();float copySize=connection.Height-4;
        Place(copyConnection,connection.Right+13,connection.Y+(connection.Height-copySize)/2,copySize,copySize);
        Place(metricChoice,d.W-165,d.Y+18,143,34);Place(graph,right+15,d.Y+75,d.W-right-32,d.CardHeight-83);dashboard.Invalidate();
    }
    void DrawDashboard(Graphics g,float w,float h)
    {
        var d=DashboardGeometry();var muted=Muted;
        var section=g.Save();
        HarborTheme.Text(g,"Your Server",46,4,9,Ink,true,w-180);HarborTheme.Text(g,"Manage your Minecraft server with ease.",16,5,68,Muted,false,w-200);
        if(server.Busy||server.SavingNeedsResume)HarborTheme.Text(g,activity.Text,11,5,94,Muted,false,w-200);
        g.Restore(section);section=g.Save();
        string shortState=server.State switch{ServerState.Running=>"Online",ServerState.Starting=>"Starting",ServerState.Stopping=>"Saving",ServerState.Crashed=>"Error",_=>"Offline"};
        var left=new RectangleF(0,d.Y,d.Left,d.CardHeight);float rx=d.Left+d.Gap;var right=new RectangleF(rx,d.Y,w-rx,d.CardHeight);HarborTheme.Card(g,left);
        HarborTheme.Text(g,"Server Status",20,22,d.Y+16,Ink,true,d.Left-145);
        string status=server.Profile.Deleted?"No server":server.State==ServerState.Stopped?"Stopped":shortState;HarborTheme.Text(g,"●  "+status,12,d.Left-125,d.Y+24,server.State==ServerState.Running?Mint:Muted,false,102,true);
        float row=d.Y+78;var addrBox=ConnectionBox();float valueX=addrBox.X;
        HarborTheme.Icon(g,"link",24,row+3,23,Muted);HarborTheme.Text(g,d.Left>=445?"Direct Connection":"Address",12,67,row+3,Muted,false,valueX-75);
        using(var path=HarborTheme.Round(addrBox,5)){using var brush=new SolidBrush(Color.FromArgb(16,23,31));g.FillPath(brush,path);using var p=new Pen(Edge);g.DrawPath(p,path);}
        using(var font=new Font("Segoe UI",14,FontStyle.Regular,GraphicsUnit.Pixel)){float textWidth=g.MeasureString(address.Text,font).Width;float size=Math.Min(14,14*(addrBox.Width-20)/Math.Max(1,textWidth));HarborTheme.Text(g,address.Text,size,valueX+10,addrBox.Y+(addrBox.Height-size*1.65f)/2,Ink,false,addrBox.Width-20);}
        using var divider=new Pen(Edge);g.DrawLine(divider,23,row+47,d.Left-23,row+47);g.DrawLine(divider,23,row+101,d.Left-23,row+101);
        HarborTheme.Icon(g,"players",24,row+65,23,Muted);HarborTheme.Text(g,"Players",13,67,row+64,Muted,false,155);HarborTheme.Text(g,players.Text,17,valueX,row+60,Ink,false,d.Left-valueX-20);
        HarborTheme.Icon(g,"cpu",24,row+119,23,Muted);HarborTheme.Text(g,"Server Memory",13,67,row+117,Muted,false,155);HarborTheme.Text(g,memory.Text,17,valueX,row+114,Ink,false,d.Left-valueX-20);
        HarborTheme.Text(g,server.Profile.Deleted?"Select a server in Server Management":server.Config.WorldName+" · "+server.Profile,10.5f,25,d.Y+d.CardHeight-27,Color.FromArgb(142,160,181),false,d.Left-48);
        g.Restore(section);section=g.Save();HarborTheme.Card(g,right);
        HarborTheme.Icon(g,"chart",rx+23,d.Y+20,21,Ink);HarborTheme.Text(g,"Performance",20,rx+60,d.Y+16,Ink,true,146);HarborTheme.Text(g,"Last 1 hour",12,rx+60,d.Y+43,Muted,false,130);
        g.Restore(section);section=g.Save();
        float bottom=d.Y+d.CardHeight+14;var load=new RectangleF(0,bottom,w,Math.Max(118,h-bottom-3));HarborTheme.Card(g,load);HarborTheme.Icon(g,"cpu",24,bottom+21,24,Ink);HarborTheme.Text(g,"CPU Load",20,62,bottom+17,Ink,true,200);
        float bx=25,by=bottom+62,bw=w-116,bh=22;using(var path=HarborTheme.Round(new(bx,by,bw,bh),5))using(var bg=new SolidBrush(Color.FromArgb(43,54,70)))g.FillPath(bg,path);
        float percent=(float)(performance.CpuPercent??0),filled=bw*percent/100;
        if(filled>0){using var path=HarborTheme.Round(new(bx,by,bw,bh),5);var saved=g.Save();g.SetClip(path);using var fill=new LinearGradientBrush(new RectangleF(bx,by,bw,bh),Color.FromArgb(53,168,94),Color.FromArgb(98,246,151),LinearGradientMode.Horizontal);g.FillRectangle(fill,bx,by,filled,bh);g.Restore(saved);}
        using(var segment=new Pen(Color.FromArgb(23,39,44),.8f)){for(float x=bx+12;x<bx+bw;x+=12)g.DrawLine(segment,x,by,x,by+bh);}
        HarborTheme.Text(g,performance.CpuPercent.HasValue?performance.CpuPercent.Value.ToString("0")+"%":"—",25,w-79,by-7,Ink,true,59,true);
        for(int i=0;i<=4;i++){float x=bx+bw*i/4;g.DrawLine(divider,x,by+28,x,by+35);HarborTheme.Text(g,(i*25)+"%",12,x-(i==0?0:i==4?29:12),by+38,Muted,false,45);}
        g.Restore(section);
        dashboard.AccessibleName="Your Server — "+server.Config.WorldName;dashboard.AccessibleDescription=$"{shortState}. Direct connection {address.Text}. Players {players.Text}. Memory {memory.Text}. Last hour of server performance.";
    }
    void RefreshDashboard()
    {
        if(graph.Parent==null)return;
        bool sampled=performance.Sample(DateTime.UtcNow,server.ReadPerformance());graph.MemoryLimit=server.Config.MemoryGB;
        start.Visible=!server.HasProcess;stop.Visible=server.HasProcess;
        var signature=$"{server.State}|{server.Activity}|{players.Text}|{memory.Text}|{address.Text}|{performance.CpuPercent}";
        if(sampled||signature!=dashboardSignature){dashboardSignature=signature;dashboard.Invalidate();graph.Invalidate();}
    }
    void RefreshArtwork()
    {
        string key=appearance.Background+"|"+server.Profile.Id;if(key==artworkKey)return;artworkKey=key;artworkCancellation?.Cancel();artworkCancellation?.Dispose();artworkCancellation=new();
        content.PackBackdrop=rail.PackBackdrop=false;content.ArtworkTitle=rail.ArtworkTitle=null;content.Artwork=rail.Artwork=nightScene;content.Invalidate(true);rail.Invalidate(true);
        artworkStatus.Text=appearance.Background=="modpack"?"Loading artwork from this installed CurseForge profile…":"Harbor's night scene. Change anytime; the server keeps running.";
        if(appearance.Background=="modpack")_ = LoadArtwork(key,server.Profile,artworkCancellation.Token);
    }
    async Task LoadArtwork(string key,ServerProfile profile,CancellationToken token)
    {
        Bitmap? loaded=null;
        try{
            loaded=await PackArtwork.Load(server.Root,profile,token);
            RectangleF? title=null;
            if(loaded!=null){try{title=await Task.Run(()=>ArtworkFocus.Detect(loaded,profile.Name),token);}catch(Exception ex)when(ex is Tesseract.TesseractException or DllNotFoundException or TypeInitializationException or IOException or BadImageFormatException){server.Log("Title detection unavailable; fitting the full pack artwork.");}}
            if(token.IsCancellationRequested||IsDisposed||artworkKey!=key)return;
            packArt?.Dispose();packArt=loaded;content.PackBackdrop=rail.PackBackdrop=packArt!=null;content.ArtworkTitle=rail.ArtworkTitle=title;content.Artwork=rail.Artwork=packArt??nightScene;content.Invalidate(true);rail.Invalidate(true);
            artworkStatus.Text=loaded==null?"No pack artwork available. Showing the Harbor night scene.":"Official CurseForge artwork · "+profile.Name+" · Cached for offline use.";
            loaded=null;
        }catch(OperationCanceledException){}catch(Exception ex)when(ex is IOException or HttpRequestException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException){if(!IsDisposed&&artworkKey==key)artworkStatus.Text="Pack artwork unavailable. Showing the Harbor night scene.";}
        finally{loaded?.Dispose();}
    }
}


