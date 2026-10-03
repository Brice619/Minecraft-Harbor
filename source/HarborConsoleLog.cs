using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Drawing.Drawing2D;
using System.Text;
using System.Globalization;

namespace MinecraftHarbor;

internal static class ConsoleSyntax
{
    internal static readonly Color Text=Color.FromArgb(201,222,246),Info=Color.FromArgb(45,205,241),Warning=Color.FromArgb(255,204,73),Error=Color.FromArgb(255,113,123),Success=Color.FromArgb(95,245,155);
    static readonly Regex Prefix=new(@"^(?<time>\d{2}:\d{2}:\d{2})  (?<body>.*)$",RegexOptions.Compiled);
    internal static string Display(string line)
    {
        var match=Prefix.Match(line);if(!match.Success)return line;
        string body=match.Groups["body"].Value;
        if(Regex.IsMatch(body,@"^\[\d{2}:\d{2}:\d{2}\]"))return body;
        return "["+match.Groups["time"].Value+"] "+(body.StartsWith('>')?"":Regex.IsMatch(body,@"\b(ERROR|FATAL|WARN|INFO|DEBUG|TRACE)\b")?"":"[INFO] ")+body;
    }
    internal static Color BaseColor(string line)=>Regex.IsMatch(line,@"\b(ERROR|FATAL)\b|Exception:|^\s+at ")?Error:Regex.IsMatch(line,@"\bWARN(?:ING)?\b")?Warning:Text;
    internal static IEnumerable<(int Start,int Length,Color Color)> Highlights(string line)
    {
        var timestamp=Regex.Match(line,@"^\[\d{2}:\d{2}:\d{2}\]");if(timestamp.Success)yield return(timestamp.Index,timestamp.Length,Text);
        foreach(Match m in Regex.Matches(line,@"\[[^\]\r\n]*(?:INFO|DEBUG|TRACE)[^\]\r\n]*\]"))yield return(m.Index,m.Length,Info);
        foreach(Match m in Regex.Matches(line,@"\b\w+(?= (?:joined the game|left the game|has made the advancement|has completed the challenge|has reached the goal))|\bDone (?=\()|\bSaved the game\b|(?<=type )\""help\"""))yield return(m.Index,m.Length,Success);
        var achievement=Regex.Match(line,@"(?:has made the advancement|has completed the challenge|has reached the goal) (?<name>\[[^\]\r\n]+\])");if(achievement.Success){var name=achievement.Groups["name"];yield return(name.Index,name.Length,Success);}
        int prompt=line.IndexOf("] > ",StringComparison.Ordinal);if(prompt>=0)yield return(prompt+2,line.Length-prompt-2,Success);
    }
    internal static string Rtf(IReadOnlyList<string> lines,float points)
    {
        Color[] colors={Text,Info,Warning,Error,Success};var output=new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Consolas;}}{\colortbl ;");
        foreach(var color in colors)output.Append("\\red").Append(color.R).Append("\\green").Append(color.G).Append("\\blue").Append(color.B).Append(';');
        output.Append("}\\f0\\fs").Append(((int)Math.Round(points*2)).ToString(CultureInfo.InvariantCulture)).Append(' ');
        foreach(var raw in lines){string line=Display(raw);int baseIndex=Array.IndexOf(colors,BaseColor(line))+1;int[] ink=Enumerable.Repeat(baseIndex,line.Length).ToArray();
            foreach(var span in Highlights(line)){int index=Array.IndexOf(colors,span.Color)+1;Array.Fill(ink,index,span.Start,span.Length);}
            int previous=0;for(int i=0;i<line.Length;i++){if(ink[i]!=previous){previous=ink[i];output.Append("\\cf").Append(previous).Append(' ');}char c=line[i];if(c is '\\' or '{' or '}')output.Append('\\').Append(c);else if(c=='\n')output.Append(@"\line ");else if(c=='\r'){}else if(c=='\t')output.Append(@"\tab ");else if(c>127)output.Append("\\u").Append((short)c).Append('?');else output.Append(c);}
            output.Append(@"\cf1\par ");}
        return output.Append('}').ToString();
    }
}

// Native text selection and copying, with a painted frame and scrollbar that match Harbor.
internal sealed class HarborConsoleLog:PaintedPanelBase
{
    internal readonly ConsoleTextBox TextBox=new();
    readonly ConsoleScrollRail scroll;
    internal HarborConsoleLog()
    {
        TextBox.AccessibleName="Server log";TextBox.AccessibleDescription="Live server messages. Select text to copy. Scroll up to read history.";
        scroll=new(TextBox);Controls.Add(TextBox);Controls.Add(scroll);
        SizeChanged+=(_,_)=>Arrange();TextBox.ViewChanged+=(_,_)=>scroll.Invalidate();
    }
    void Arrange(){int u(int x)=>LogicalToDeviceUnits(x);TextBox.SetBounds(u(16),u(12),Math.Max(1,Width-u(43)),Math.Max(1,Height-u(24)));scroll.SetBounds(Width-u(24),u(10),u(15),Math.Max(1,Height-u(20)));}
    internal void AppendLines(IReadOnlyList<string> lines)=>TextBox.AppendLines(lines);
    public void Clear()=>TextBox.Clear();
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);float s=DeviceDpi/96f;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using var path=HarborTheme.Round(new RectangleF(s,s,Width-2*s,Height-2*s),8*s);using var brush=new SolidBrush(ConsoleTextBox.Surface);e.Graphics.FillPath(brush,path);
        using var edge=new Pen(Color.FromArgb(44,119,153),s);e.Graphics.DrawPath(edge,path);
    }
}
internal sealed class ConsoleTextBox:RichTextBox
{
    internal static readonly Color Surface=Color.FromArgb(7,23,36);
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
    internal event EventHandler? ViewChanged;
    internal bool FollowMessages=true;
    internal int HistoryCharacters=300000;
    Font? ownedFont;
    internal void Configure(float points,bool follow,int history)
    {
        FollowMessages=follow;HistoryCharacters=history;if(Math.Abs(Font.SizeInPoints-points)<.01f)return;
        int first=FirstLine,start=SelectionStart,length=SelectionLength;var next=new Font("Consolas",points);var old=ownedFont;Font=next;ownedFont=next;SelectAll();SelectionFont=next;Select(Math.Min(start,TextLength),Math.Min(length,Math.Max(0,TextLength-start)));ScrollToLine(first);old?.Dispose();
    }
    internal ConsoleTextBox(){ReadOnly=true;DetectUrls=false;BorderStyle=BorderStyle.None;ScrollBars=RichTextBoxScrollBars.None;WordWrap=true;BackColor=Surface;ForeColor=ConsoleSyntax.Text;Font=ownedFont=new Font("Consolas",10.5f);HideSelection=false;TabStop=true;ShortcutsEnabled=true;}
    protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing){ownedFont?.Dispose();ownedFont=null;}}
    internal int FirstLine=>IsHandleCreated?(int)SendMessage(Handle,0x00CE,IntPtr.Zero,IntPtr.Zero):0;
    internal int LineCount=>IsHandleCreated?(int)SendMessage(Handle,0x00BA,IntPtr.Zero,IntPtr.Zero):1;
    internal int VisibleLines=>Math.Max(1,(ClientSize.Height-4)/Font.Height);
    internal int LastFirst=>Math.Max(0,LineCount-VisibleLines);
    internal void ScrollToLine(int line){if(!IsHandleCreated)return;SendMessage(Handle,0x00B6,IntPtr.Zero,(IntPtr)(Math.Clamp(line,0,LastFirst)-FirstLine));ViewChanged?.Invoke(this,EventArgs.Empty);}
    protected override void OnMouseWheel(MouseEventArgs e){int lines=Math.Max(1,SystemInformation.MouseWheelScrollLines);ScrollToLine(FirstLine-e.Delta/120*lines);}
    protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg is 0x0115 or 0x0101 or 0x0005)ViewChanged?.Invoke(this,EventArgs.Empty);}
    internal void AppendLines(IReadOnlyList<string> lines)
    {
        if(lines.Count==0)return;
        int first=FirstLine,selection=SelectionStart,length=SelectionLength;bool follow=FollowMessages&&first>=LastFirst-1&&length==0;
        bool handle=IsHandleCreated;if(handle)SendMessage(Handle,0x000B,IntPtr.Zero,IntPtr.Zero);
        try{
            Select(TextLength,0);SelectedRtf=ConsoleSyntax.Rtf(lines,Font.SizeInPoints);
            int removed=0;
            if(TextLength>HistoryCharacters){removed=Text.IndexOf('\n',TextLength-HistoryCharacters*2/3);if(removed>=0){removed++;first=Math.Max(0,first-GetLineFromCharIndex(removed));Select(0,removed);ReadOnly=false;try{SelectedText="";}finally{ReadOnly=true;}}else removed=0;}
            Select(Math.Clamp(selection-removed,0,TextLength),Math.Min(length,Math.Max(0,TextLength-Math.Max(0,selection-removed))));
            ScrollToLine(follow?LastFirst:first);
        }finally{if(handle){SendMessage(Handle,0x000B,(IntPtr)1,IntPtr.Zero);Invalidate();}ViewChanged?.Invoke(this,EventArgs.Empty);}
    }
}
internal sealed class ConsoleScrollRail:Control
{
    readonly ConsoleTextBox log;bool dragging,hover;float dragOffset;
    internal ConsoleScrollRail(ConsoleTextBox log){this.log=log;BackColor=ConsoleTextBox.Surface;Cursor=Cursors.Hand;TabStop=false;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);AccessibleName="Scroll server log";}
    RectangleF Thumb(){float pad=2*DeviceDpi/96f,track=Math.Max(1,Height-2*pad),h=Math.Clamp(track*log.VisibleLines/Math.Max(log.VisibleLines,log.LineCount),Math.Min(track,32*DeviceDpi/96f),track);return new(pad,pad+(track-h)*log.FirstLine/Math.Max(1,log.LastFirst),Math.Max(1,Width-2*pad),h);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using var track=HarborTheme.Round(new(2,2,Math.Max(1,Width-4),Math.Max(1,Height-4)),5);using var bg=new SolidBrush(Color.FromArgb(14,38,57));e.Graphics.FillPath(bg,track);if(log.LastFirst>0){using var path=HarborTheme.Round(Thumb(),5);using var fill=new SolidBrush(hover||dragging?Color.FromArgb(57,147,185):Color.FromArgb(39,86,119));e.Graphics.FillPath(fill,path);}}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;var thumb=Thumb();if(thumb.Contains(e.Location)){dragging=true;dragOffset=e.Y-thumb.Y;Capture=true;}else log.ScrollToLine(log.FirstLine+(e.Y<thumb.Y?-log.VisibleLines:log.VisibleLines));Invalidate();}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging){var thumb=Thumb();float travel=Height-4*DeviceDpi/96f-thumb.Height;log.ScrollToLine((int)Math.Round((e.Y-dragOffset-2*DeviceDpi/96f)/Math.Max(1,travel)*log.LastFirst));}}
    protected override void OnMouseUp(MouseEventArgs e){dragging=false;Capture=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnMouseWheel(MouseEventArgs e){log.ScrollToLine(log.FirstLine-e.Delta/120*Math.Max(1,SystemInformation.MouseWheelScrollLines));}
}

internal sealed class ConsoleCommandFrame:PaintedPanelBase
{
    readonly TextBox input;
    internal ConsoleCommandFrame(TextBox input){this.input=input;input.BorderStyle=BorderStyle.None;input.BackColor=ConsoleTextBox.Surface;input.ForeColor=HarborTheme.Ink;input.Font=new Font("Segoe UI",11.5f);Controls.Add(input);SizeChanged+=(_,_)=>Arrange();input.GotFocus+=(_,_)=>Invalidate();input.LostFocus+=(_,_)=>Invalidate();}
    void Arrange(){int pad=LogicalToDeviceUnits(54);input.SetBounds(pad,Math.Max(0,(Height-input.PreferredSize.Height)/2),Math.Max(1,Width-pad-LogicalToDeviceUnits(14)),input.PreferredSize.Height);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);float s=DeviceDpi/96f;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;using var path=HarborTheme.Round(new(s,s,Width-2*s,Height-2*s),7*s);using var fill=new SolidBrush(ConsoleTextBox.Surface);g.FillPath(fill,path);using var pen=new Pen(input.Focused?ConsoleSyntax.Info:Color.FromArgb(44,119,153),s);g.DrawPath(pen,path);g.DrawLine(pen,43*s,s,43*s,Height-s);using var chevron=new Pen(HarborTheme.Muted,2*s){StartCap=LineCap.Round,EndCap=LineCap.Round};g.DrawLines(chevron,new[]{new PointF(18*s,Height/2-7*s),new PointF(25*s,Height/2),new PointF(18*s,Height/2+7*s)});}
}
