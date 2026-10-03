using System.Drawing.Drawing2D;
using System.ComponentModel;
namespace MinecraftHarbor;
public sealed class HarborButton : Button
{
    [DefaultValue("")] public string Glyph {get;set;}="";
    [DefaultValue(false)] public bool Primary {get;set;}
    [DefaultValue(false)] public bool Danger {get;set;}
    [DefaultValue(false)] public bool Navigation {get;set;}
    [DefaultValue(false)] public bool Selected {get;set;}
    [DefaultValue(false)] public bool Chrome {get;set;}
    [DefaultValue(false)] public bool QuietDanger {get;set;}
    [DefaultValue(false)] public bool ConsoleAccent {get;set;}
    [DefaultValue(false)] public bool OutlineAccent {get;set;}
    [DefaultValue(false)] public bool MenuArrow {get;set;}
    bool hover,pressed;
    public HarborButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        PaintBase(e.Graphics);
    }
    void PaintBase(Graphics g)
    {
        if(Parent is HarborSurface surface){surface.PaintBackdrop(g,Location,ClientSize);return;}
        if(Parent is null){g.Clear(BackColor);return;}
        // Rounded corners reveal the actual parent artwork or card, never a solid backing rectangle.
        var state=g.Save();
        g.TranslateTransform(-Left,-Top);
        using var background=new PaintEventArgs(g,Bounds);
        InvokePaintBackground(Parent,background);
        InvokePaint(Parent,background);
        g.Restore(state);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;PaintBase(g);g.SmoothingMode=SmoothingMode.AntiAlias;float s=DeviceDpi/96f;var r=new RectangleF(s,s,Width-2*s,Height-2*s);
        var top=Primary?Color.FromArgb(64,204,112):Color.FromArgb(43,53,67);var bottom=Primary?Color.FromArgb(40,156,80):Color.FromArgb(27,34,43);
        if(hover)top=Primary?Color.FromArgb(78,218,127):Color.FromArgb(57,69,86);
        if(ConsoleAccent){top=hover?Color.FromArgb(58,218,244):Color.FromArgb(10,201,235);bottom=Color.FromArgb(3,163,199);}
        if(OutlineAccent){top=hover?Color.FromArgb(20,64,58):Color.FromArgb(11,39,42);bottom=Color.FromArgb(9,28,36);}
        if(Danger){top=hover?Color.FromArgb(239,89,94):Color.FromArgb(219,68,75);bottom=Color.FromArgb(162,43,52);}
        if(Danger&&QuietDanger){top=hover?Color.FromArgb(93,43,52):Color.FromArgb(61,30,39);bottom=Color.FromArgb(40,25,32);}
        if(pressed)top=bottom;if(!Enabled){top=Color.FromArgb(32,41,52);bottom=Color.FromArgb(25,32,41);}
        if(Navigation&&!Selected&&!hover){using var path=HarborTheme.Round(r,8*s);using var fill=new SolidBrush(Color.FromArgb(23,30,40));g.FillPath(fill,path);}
        if(!Navigation&&!Chrome||Selected||hover){using var path=HarborTheme.Round(r,(Chrome?4:8)*s);using var fill=new LinearGradientBrush(r,top,bottom,LinearGradientMode.Vertical);g.FillPath(fill,path);if(!Navigation&&!Chrome){using var edge=new Pen(Danger&&Enabled?Color.FromArgb(255,124,128):OutlineAccent&&Enabled?Color.FromArgb(0,236,168):ConsoleAccent&&Enabled?Color.FromArgb(72,222,249):Primary&&Enabled?Color.FromArgb(108,239,152):Color.FromArgb(74,85,101),s);g.DrawPath(edge,path);}}
        if(Navigation&&Selected){using var outline=HarborTheme.Round(new RectangleF(s,s,Width-2*s,Height-2*s),8*s);using var border=new Pen(Color.FromArgb(69,223,125),1.2f*s);g.DrawPath(border,outline);}
        var color=Enabled?(ConsoleAccent?Color.FromArgb(6,27,39):Navigation&&!Selected?HarborTheme.Muted:HarborTheme.Ink):Color.FromArgb(112,126,145);
        if(OutlineAccent&&Enabled)color=Color.FromArgb(0,236,168);
        var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding;
        float icon=(Navigation?20:21)*s,gap=Text.Length>0?12*s:0;
        float arrowSpace=MenuArrow?23*s:0;
        int textWidth=Math.Min(TextRenderer.MeasureText(g,Text,Font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding).Width+2,Math.Max(0,Width-(int)(28*s+icon+gap+arrowSpace)));
        float groupWidth=Glyph.Length>0?icon+gap+textWidth+arrowSpace:0;
        float ix=Navigation?17*s:(Width-groupWidth)/2;
        if(Glyph.Length>0)HarborTheme.Icon(g,Glyph,Text.Length==0?(Width-icon)/2:ix,(Height-icon)/2,icon,color);
        var bounds=Navigation?new Rectangle((int)(50*s),0,Width-(int)(58*s),Height):Glyph.Length>0?new Rectangle((int)(ix+icon+gap),0,textWidth,Height):new Rectangle(0,0,Width,Height);
        flags|=Navigation||Glyph.Length>0?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter;
        TextRenderer.DrawText(g,Text,Font,bounds,color,flags);
        if(MenuArrow){float x=ix+icon+gap+textWidth+12*s;using var pen=new Pen(color,1.5f*s);g.DrawLines(pen,new[]{new PointF(x-4*s,Height/2f-2*s),new PointF(x,Height/2f+2*s),new PointF(x+4*s,Height/2f-2*s)});}
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle((int)(5*s),(int)(5*s),Width-(int)(10*s),Height-(int)(10*s)),color,BackColor);
    }
}


