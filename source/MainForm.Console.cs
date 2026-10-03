using System.Drawing.Drawing2D;

namespace MinecraftHarbor;
public sealed partial class MainForm
{
    readonly PaintedPanel consoleBanner=new();
    HarborButton consoleEnter=null!;
    ConsoleCommandFrame consoleCommandFrame=null!;
    Bitmap? consoleArtwork;
    string consoleArtworkKey="",consoleSignature="";
    CancellationTokenSource? consoleArtworkCancellation;
    HomeFadeOverlay? consoleFade;
    int consoleFadeGeneration;

    void BuildConsole()
    {
        var page=pages["Console"];page.AutoScroll=false;
        var title=LabelAt(page,"Server Console",0,0,900,55,31,Ink,true);
        var subtitle=LabelAt(page,"View logs and run console commands for your selected server.",0,0,900,33,13,ConsoleSyntax.Info);
        page.Controls.Add(consoleBanner);page.Controls.Add(console);
        consoleBanner.Draw=DrawConsoleBanner;
        command.PlaceholderText="Type a console command…";command.AccessibleName="Console command";command.MaxLength=32767;
        consoleCommandFrame=new(command);page.Controls.Add(consoleCommandFrame);
        consoleEnter=(HarborButton)MakeButton("Enter",140);consoleEnter.ConsoleAccent=true;consoleEnter.AccessibleName="Send console command";page.Controls.Add(consoleEnter);
        consoleEnter.Click+=async(_,_)=>await SendConsoleDraft();
        command.PreviewKeyDown+=(_,e)=>{if(e.KeyCode==Keys.Tab)e.IsInputKey=true;};
        command.KeyDown+=async(_,e)=>{
            if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;await SendConsoleDraft();}
            else if(e.KeyCode==Keys.Tab&&commandSuggestions.Visible&&commandSuggestions.Selected!=null){e.SuppressKeyPress=true;AcceptCommandSuggestion();}
            else if(e.KeyCode is Keys.Up or Keys.Down){e.SuppressKeyPress=true;if(e.Control&&commandSuggestions.Visible)commandSuggestions.MoveSelection(e.KeyCode==Keys.Up?-1:1);else MoveCommandHistory(e.KeyCode==Keys.Up?-1:1);}
            else if(e.KeyCode==Keys.Escape){e.SuppressKeyPress=true;commandPopupDismissed=true;commandSuggestions.Visible=false;}
            else if(e.Control&&e.KeyCode==Keys.Space){e.SuppressKeyPress=true;commandPopupDismissed=false;UpdateCommandSuggestions();}
            else if(e.KeyCode is Keys.Left or Keys.Right or Keys.Home or Keys.End){BeginInvoke(()=>UpdateCommandSuggestions());}
        };
        command.TextChanged+=(_,_)=>RefreshConsoleStatus();
        void Arrange(){int u(int x)=>page.LogicalToDeviceUnits(x);int w=page.ClientSize.Width-u(3),bottom=page.ClientSize.Height-u(46);title.SetBounds(0,u(12),w,u(58));subtitle.SetBounds(0,u(71),w,u(33));consoleBanner.SetBounds(0,u(123),w,u(74));console.SetBounds(0,u(210),w,Math.Max(u(140),bottom-u(222)));consoleCommandFrame.SetBounds(0,bottom,w-u(154),u(44));consoleEnter.SetBounds(w-u(140),bottom,u(140),u(44));}
        page.SizeChanged+=(_,_)=>{FinishConsoleEntrance();Arrange();};page.VisibleChanged+=(_,_)=>{if(page.Visible){Arrange();RefreshConsoleStatus();RefreshConsoleArtwork();}else FinishConsoleEntrance();};Arrange();BuildCommandAssistance();
        Disposed+=(_,_)=>{var cancellation=consoleArtworkCancellation;consoleArtworkCancellation=null;cancellation?.Cancel();cancellation?.Dispose();consoleArtwork?.Dispose();consoleArtwork=null;FinishConsoleEntrance();};
    }
    void DrawConsoleBanner(Graphics g,float w,float h)
    {
        var bounds=new RectangleF(1,1,w-2,h-2);using var outline=HarborTheme.Round(bounds,9);using var fill=new LinearGradientBrush(bounds,Color.FromArgb(18,41,58),Color.FromArgb(10,29,43),LinearGradientMode.Horizontal);g.FillPath(fill,outline);using var edge=new Pen(Color.FromArgb(44,119,153));g.DrawPath(edge,outline);
        var thumbnail=new RectangleF(16,10,88,h-20);
        if(consoleArtwork!=null){float scale=Math.Min(thumbnail.Width/consoleArtwork.Width,thumbnail.Height/consoleArtwork.Height);float iw=consoleArtwork.Width*scale,ih=consoleArtwork.Height*scale;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(consoleArtwork,new RectangleF(thumbnail.X+(thumbnail.Width-iw)/2,thumbnail.Y+(thumbnail.Height-ih)/2,iw,ih));}
        else HarborTheme.Icon(g,"cube",38,(h-38)/2,38,Muted);
        HarborTheme.Text(g,server.Profile.Deleted?"No server selected":server.Profile.DisplayName,19,120,12,Ink,true,Math.Max(1,w-322));
        HarborTheme.Text(g,server.Profile.Deleted?"Choose a server in Server Management":server.Profile+"  •  "+server.Config.WorldName,13,120,41,Muted,false,Math.Max(1,w-322));
        var (label,color)=ConsoleState(server.State);float x=w-183;var badge=new RectangleF(x,10,169,h-20);using var badgePath=HarborTheme.Round(badge,8);using var badgeFill=new SolidBrush(Color.FromArgb(10,28,37));g.FillPath(badgeFill,badgePath);using var badgeEdge=new Pen(Color.FromArgb(140,color));g.DrawPath(badgeEdge,badgePath);
        float cy=h/2;using var indicator=new SolidBrush(color);if(server.State==ServerState.Running){g.FillEllipse(indicator,x+13,cy-12,24,24);using var check=new Pen(Color.FromArgb(8,38,31),2.4f){StartCap=LineCap.Round,EndCap=LineCap.Round};g.DrawLines(check,new[]{new PointF(x+20,cy),new PointF(x+24,cy+4),new PointF(x+31,cy-4)});}else{using var dotPen=new Pen(color,2);g.DrawEllipse(dotPen,x+15,cy-10,20,20);g.FillEllipse(indicator,x+22,cy-3,6,6);}
        string session=ConsoleSessionText();
        if(session.Length==0){using var font=new Font("Segoe UI",14,FontStyle.Bold,GraphicsUnit.Pixel);using var brush=new SolidBrush(color);using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap};g.DrawString(label,font,brush,new RectangleF(x+47,10,112,h-20),format);}
        else{HarborTheme.Text(g,label,14,x+47,15,color,true,112);HarborTheme.Text(g,session,10.5f,x+47,36,Muted,false,113);}
        consoleBanner.AccessibleName=server.Profile.DisplayName+" · "+label+(session.Length>0?" · "+session:"");
    }
    static (string Label,Color Color) ConsoleState(ServerState state)=>state switch{ServerState.Running=>("Running",ConsoleSyntax.Success),ServerState.Starting=>("Starting",ConsoleSyntax.Info),ServerState.Stopping=>("Stopping",ConsoleSyntax.Warning),ServerState.Crashed=>("Needs attention",ConsoleSyntax.Error),_=>("Stopped",Muted)};
    string ConsoleSessionText(){if(!server.StartedAt.HasValue)return "";var span=DateTime.Now-server.StartedAt.Value;return "Uptime: "+(span.TotalHours>=1?(int)span.TotalHours+"h "+span.Minutes+"m":span.TotalMinutes>=1?(int)span.TotalMinutes+"m "+span.Seconds+"s":span.Seconds+"s");}
    void RefreshConsoleStatus()
    {
        if(consoleEnter==null)return;
        command.Enabled=server.State==ServerState.Running&&!commandSending;consoleEnter.Enabled=command.Enabled&&!string.IsNullOrWhiteSpace(command.Text);
        if(pages["Console"].Visible)RefreshCommandAssistance();
        string signature=server.Profile.Id+"|"+server.Profile.DisplayName+"|"+server.Config.WorldName+"|"+server.State+"|"+ConsoleSessionText();
        if(signature!=consoleSignature){consoleSignature=signature;consoleBanner.Invalidate();}
        if(pages["Console"].Visible)RefreshConsoleArtwork();
    }
    void RefreshConsoleArtwork()
    {
        string key=server.Profile.Id;if(key==consoleArtworkKey)return;consoleArtworkKey=key;consoleArtworkCancellation?.Cancel();consoleArtworkCancellation?.Dispose();consoleArtworkCancellation=new();
        consoleArtwork?.Dispose();consoleArtwork=null;consoleBanner.Invalidate();var profile=server.Profile;var token=consoleArtworkCancellation.Token;
        async Task Load(){Bitmap? image=null;try{image=await PackArtwork.Load(server.Root,profile,token);if(token.IsCancellationRequested||IsDisposed||consoleArtworkKey!=key)return;consoleArtwork=image;image=null;consoleBanner.Invalidate();}catch(OperationCanceledException){}catch(Exception ex)when(ex is IOException or HttpRequestException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException){server.Log("Console artwork unavailable: "+ex.Message);}finally{image?.Dispose();}}
        _=Load();
    }
    async void AnimateConsoleEntrance()
    {
        FinishConsoleEntrance();if(!IsHandleCreated||!Visible||!MotionEnabled)return;
        int generation=consoleFadeGeneration;var page=pages["Console"];using var snapshot=new Bitmap(page.Width,page.Height);page.DrawToBitmap(snapshot,page.ClientRectangle);
        var overlay=new HomeFadeOverlay(new Bitmap(snapshot)){Bounds=page.Bounds,Alpha=0};consoleFade=overlay;content.Controls.Add(overlay);overlay.BringToFront();
        var watch=System.Diagnostics.Stopwatch.StartNew();while(watch.ElapsedMilliseconds<preferences.FadeMilliseconds&&generation==consoleFadeGeneration&&!IsDisposed){float t=Math.Min(1,watch.ElapsedMilliseconds/(float)preferences.FadeMilliseconds);overlay.Alpha=t*t*(3-2*t);overlay.Invalidate();await Task.Delay(15);}if(generation==consoleFadeGeneration)FinishConsoleEntrance();
    }
    void FinishConsoleEntrance(){consoleFadeGeneration++;if(consoleFade!=null){var old=consoleFade;consoleFade=null;old.Parent?.Controls.Remove(old);old.Dispose();}}
}

