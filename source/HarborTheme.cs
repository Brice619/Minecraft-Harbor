using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.ComponentModel;

namespace MinecraftHarbor;
internal static class HarborTheme
{
    public static readonly Color Ink=Color.FromArgb(244,247,251),Muted=Color.FromArgb(175,193,215),Green=Color.FromArgb(106,255,183),Edge=Color.FromArgb(49,60,73);
    public static GraphicsPath Round(RectangleF r,float radius=9)
    {
        var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    public static void Text(Graphics g,string text,float size,float x,float y,Color? color=null,bool bold=false,float width=1000,bool right=false)
    {
        using var font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);using var brush=new SolidBrush(color??Ink);
        using var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap,Alignment=right?StringAlignment.Far:StringAlignment.Near};
        g.DrawString(text,font,brush,new RectangleF(x,y,width,size*1.65f),format);
    }
    public static void Card(Graphics g,RectangleF rect)
    {
        // Keep the complete outline inside the panel's drawable bounds.
        rect.Inflate(-1,-1);
        using var path=Round(rect,10);using var fill=new LinearGradientBrush(rect,Color.FromArgb(248,25,32,41),Color.FromArgb(249,23,29,37),LinearGradientMode.ForwardDiagonal);g.FillPath(fill,path);
        using var edge=new Pen(Edge,1);g.DrawPath(edge,path);
    }
    public static void Icon(Graphics g,string glyph,float x,float y,float size,Color color)
    {
        var state=g.Save();g.TranslateTransform(x,y);g.ScaleTransform(size/24,size/24);using var p=new Pen(color,1.8f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};using var b=new SolidBrush(color);
        switch(glyph){
            case "play":g.FillPolygon(b,new[]{new PointF(6,3),new PointF(21,12),new PointF(6,21)});break;
            case "stop":g.FillRectangle(b,5,5,14,14);break;
            case "restart":g.DrawArc(p,4,4,16,16,35,300);g.DrawLines(p,new[]{new PointF(20,2),new PointF(20,9),new PointF(13,9)});break;
            case "backups":g.DrawEllipse(p,4,3,16,6);g.DrawArc(p,4,8,16,6,0,180);g.DrawArc(p,4,13,16,6,0,180);g.DrawArc(p,4,17,16,5,0,180);g.DrawLine(p,4,6,4,19);g.DrawLine(p,20,6,20,19);break;
            case "up":g.DrawLines(p,new[]{new PointF(8,14),new PointF(12,10),new PointF(16,14)});break;
            case "down":g.DrawLines(p,new[]{new PointF(8,10),new PointF(12,14),new PointF(16,10)});break;
            case "back":g.DrawLine(p,3,12,22,12);g.DrawLines(p,new[]{new PointF(10,4),new PointF(2,12),new PointF(10,20)});break;
            case "plus":g.DrawLine(p,12,2,12,22);g.DrawLine(p,2,12,22,12);break;
            case "download":g.DrawLine(p,12,2,12,16);g.DrawLines(p,new[]{new PointF(6,10),new PointF(12,16),new PointF(18,10)});g.DrawLines(p,new[]{new PointF(3,16),new PointF(3,22),new PointF(21,22),new PointF(21,16)});break;
            case "cube":g.DrawPolygon(p,new[]{new PointF(12,1),new PointF(22,7),new PointF(22,18),new PointF(12,24),new PointF(2,18),new PointF(2,7)});g.DrawLines(p,new[]{new PointF(2,7),new PointF(12,13),new PointF(22,7)});g.DrawLine(p,12,13,12,24);break;
            case "edit":g.DrawPolygon(p,new[]{new PointF(3,17),new PointF(17,3),new PointF(21,7),new PointF(7,21),new PointF(2,22)});break;
            case "trash":g.DrawLine(p,3,5,21,5);g.DrawRectangle(p,6,5,12,17);g.DrawLine(p,9,2,15,2);g.DrawLine(p,10,9,10,18);g.DrawLine(p,14,9,14,18);break;
            case "cpu":g.DrawRectangle(p,5,5,14,14);g.DrawRectangle(p,8,8,8,8);for(int a=7;a<19;a+=4){g.DrawLine(p,a,1,a,4);g.DrawLine(p,a,20,a,23);g.DrawLine(p,1,a,4,a);g.DrawLine(p,20,a,23,a);}break;
            case "players":g.FillEllipse(b,5,3,7,7);g.FillEllipse(b,14,5,5,5);g.FillPie(b,1,12,15,15,180,180);g.FillPie(b,13,13,10,12,180,180);break;
            case "player":g.FillEllipse(b,8,2,8,8);g.FillPie(b,3,12,18,20,180,180);break;
            case "shield":g.FillPolygon(b,new[]{new PointF(12,1),new PointF(22,5),new PointF(20,15),new PointF(12,23),new PointF(4,15),new PointF(2,5)});using(var cut=new Pen(Color.FromArgb(16,41,61),2)){g.DrawLine(cut,12,7,12,14);g.DrawEllipse(cut,11,17,2,2);}break;
            case "next":g.DrawLines(p,new[]{new PointF(8,4),new PointF(16,12),new PointF(8,20)});break;
            case "teleport":g.DrawPolygon(p,new[]{new PointF(2,9),new PointF(22,2),new PointF(15,22),new PointF(11,13),new PointF(2,9)});g.DrawLine(p,11,13,22,2);break;
            case "kick":g.DrawLines(p,new[]{new PointF(5,3),new PointF(5,15),new PointF(13,18),new PointF(21,18),new PointF(21,22),new PointF(11,22),new PointF(3,18),new PointF(3,12)});g.DrawLine(p,13,3,13,13);break;
            case "link":g.DrawArc(p,2,10,10,10,30,290);g.DrawArc(p,12,1,10,10,210,290);g.DrawLine(p,8,16,16,8);break;
            case "copy":g.DrawRectangle(p,8,7,12,14);g.DrawLines(p,new[]{new PointF(15,4),new PointF(15,2),new PointF(3,2),new PointF(3,16),new PointF(5,16)});break;
            case "home":g.FillPolygon(b,new[]{new PointF(2,10),new PointF(12,1),new PointF(22,10),new PointF(19,10),new PointF(19,22),new PointF(14,22),new PointF(14,14),new PointF(10,14),new PointF(10,22),new PointF(5,22),new PointF(5,10)});break;
            case "folder":g.DrawLines(p,new[]{new PointF(2,20),new PointF(2,4),new PointF(9,4),new PointF(12,8),new PointF(22,8),new PointF(22,20),new PointF(2,20)});break;
            case "console":g.DrawRectangle(p,2,4,20,16);g.DrawLines(p,new[]{new PointF(6,8),new PointF(10,12),new PointF(6,16)});g.DrawLine(p,13,16,18,16);break;
            case "settings":g.DrawEllipse(p,6,6,12,12);g.DrawEllipse(p,10,10,4,4);for(int a=0;a<8;a++){double t=a*Math.PI/4;g.DrawLine(p,12+(float)Math.Cos(t)*7,12+(float)Math.Sin(t)*7,12+(float)Math.Cos(t)*11,12+(float)Math.Sin(t)*11);}break;
            case "chart":g.DrawLine(p,2,22,23,22);g.FillRectangle(b,4,15,4,6);g.FillRectangle(b,11,8,4,13);g.FillRectangle(b,18,2,4,19);break;
            case "pc":g.DrawRectangle(p,2,3,20,14);g.DrawLine(p,12,18,12,22);g.DrawLine(p,7,22,17,22);break;
            case "help":g.DrawEllipse(p,2,2,20,20);Text(g,"?",18,7,0,color,true,16);break;
            case "info":g.DrawEllipse(p,2,2,20,20);g.FillEllipse(b,11,6,2,2);g.DrawLine(p,12,11,12,18);break;
            case "close":g.DrawLine(p,6,6,18,18);g.DrawLine(p,18,6,6,18);break;
            case "max":g.DrawRectangle(p,6,6,12,12);break;
            case "min":g.DrawLine(p,6,12,18,12);break;
        }g.Restore(state);
    }
    public static void GrassBlock(Graphics g,float x,float y,float s)
    {
        var saved=g.Save();g.TranslateTransform(x,y);g.ScaleTransform(s/36,s/36);
        using var grass=new SolidBrush(Color.FromArgb(108,208,100));g.FillPolygon(grass,new[]{new PointF(18,0),new PointF(35,8),new PointF(18,16),new PointF(1,8)});
        using var dirt=new SolidBrush(Color.FromArgb(130,93,61));g.FillPolygon(dirt,new[]{new PointF(1,8),new PointF(18,16),new PointF(18,36),new PointF(1,27)});
        using var shade=new SolidBrush(Color.FromArgb(89,65,43));g.FillPolygon(shade,new[]{new PointF(18,16),new PointF(35,8),new PointF(35,27),new PointF(18,36)});
        using var green=new SolidBrush(Color.FromArgb(62,139,60));g.FillPolygon(green,new[]{new PointF(1,8),new PointF(18,16),new PointF(18,23),new PointF(13,21),new PointF(13,24),new PointF(8,20),new PointF(8,17),new PointF(1,14)});
        using var dark=new SolidBrush(Color.FromArgb(43,107,51));g.FillPolygon(dark,new[]{new PointF(18,16),new PointF(35,8),new PointF(35,15),new PointF(31,17),new PointF(31,20),new PointF(26,22),new PointF(26,19),new PointF(18,23)});
        using var fleck=new SolidBrush(Color.FromArgb(142,224,121));g.FillPolygon(fleck,new[]{new PointF(9,7),new PointF(16,4),new PointF(23,7),new PointF(16,10)});g.Restore(saved);
    }
}
internal sealed class HarborSurface:Panel
{
    Image? artwork;
    string role="canvas";
    bool packBackdrop;
    RectangleF? artworkTitle;
    Bitmap? backdrop;
    internal int BackdropRenderCount {get;private set;}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Image? Artwork {get=>artwork;set{if(ReferenceEquals(artwork,value))return;artwork=value;ResetBackdrop();}}
    [DefaultValue("canvas")] public string Role {get=>role;set{if(role==value)return;role=value;ResetBackdrop();}}
    [DefaultValue(false)] public bool PackBackdrop {get=>packBackdrop;set{if(packBackdrop==value)return;packBackdrop=value;ResetBackdrop();}}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public RectangleF? ArtworkTitle {get=>artworkTitle;set{if(artworkTitle==value)return;artworkTitle=value;ResetBackdrop();}}
    public HarborSurface(){DoubleBuffered=true;ResizeRedraw=true;SetStyle(ControlStyles.SupportsTransparentBackColor,true);}
    void ResetBackdrop(){backdrop?.Dispose();backdrop=null;Invalidate(true);}
    protected override void OnSizeChanged(EventArgs e){ResetBackdrop();base.OnSizeChanged(e);}
    protected override void OnDpiChangedAfterParent(EventArgs e){ResetBackdrop();base.OnDpiChangedAfterParent(e);}
    protected override void Dispose(bool disposing){if(disposing)backdrop?.Dispose();base.Dispose(disposing);}
    void EnsureBackdrop()
    {
        if(backdrop!=null||Width<1||Height<1)return;
        backdrop=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g=Graphics.FromImage(backdrop);RenderBackdrop(g);BackdropRenderCount++;
    }
    internal void PaintBackdrop(Graphics g,Point origin,Size size)
    {
        EnsureBackdrop();if(backdrop==null)return;
        g.DrawImage(backdrop,new Rectangle(Point.Empty,size),new Rectangle(origin,size),GraphicsUnit.Pixel);
    }
    static void Fit(Graphics g,Image image,RectangleF target,RectangleF? focus=null)
    {
        var source=focus??new RectangleF(0,0,image.Width,image.Height);
        float scale=Math.Min(target.Width/source.Width,target.Height/source.Height);float w=source.Width*scale,h=source.Height*scale;
        g.DrawImage(image,new RectangleF(target.X+(target.Width-w)/2,target.Y+(target.Height-h)/2,w,h),source,GraphicsUnit.Pixel);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        PaintBackdrop(e.Graphics,Point.Empty,ClientSize);
    }
    void RenderBackdrop(Graphics g)
    {
        g.Clear(Color.FromArgb(13,19,26));var r=ClientRectangle;if(r.Width<1||r.Height<1)return;
        if(Artwork!=null&&(Role=="canvas"||Role=="rail")){
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            if(Role=="rail"){
                if(!PackBackdrop){int h=Math.Min(Height,(int)(350*DeviceDpi/96f));g.DrawImage(Artwork,new Rectangle(0,Height-h,Width,h),new Rectangle(0,(int)(Artwork.Height*.25),(int)(Artwork.Width*.23),(int)(Artwork.Height*.75)),GraphicsUnit.Pixel);}
            }else{
                float scale=Math.Max((float)Width/Artwork.Width,(float)Height/Artwork.Height);
                float w=Artwork.Width*scale,h=Artwork.Height*scale;
                g.DrawImage(Artwork,new RectangleF(Width-w,0,w,h));
            }
        }
        if(Role=="canvas"){
            if(PackBackdrop){using var dim=new SolidBrush(Color.FromArgb(135,10,16,24));g.FillRectangle(dim,r);}
            using var shade=new LinearGradientBrush(r,Color.FromArgb(140,9,14,20),Color.FromArgb(15,9,14,20),LinearGradientMode.Horizontal);g.FillRectangle(shade,r);
            using var lower=new LinearGradientBrush(r,Color.FromArgb(0,10,16,22),Color.FromArgb(220,10,16,22),LinearGradientMode.Vertical);g.FillRectangle(lower,r);
        }else{
            using var shade=new LinearGradientBrush(r,Color.FromArgb(Role=="rail"?255:245,23,30,40),Color.FromArgb(Role=="rail"?(PackBackdrop?210:30):245,30,40,55),LinearGradientMode.ForwardDiagonal);g.FillRectangle(shade,r);
        }
        using var edge=new Pen(HarborTheme.Edge);if(Role=="rail")g.DrawLine(edge,Width-1,0,Width-1,Height);if(Role=="header")g.DrawLine(edge,0,Height-1,Width,Height-1);if(Role=="footer")g.DrawLine(edge,0,0,Width,0);
        // Fit the sidebar artwork within its available space.
        if(PackBackdrop&&Artwork!=null&&Role=="rail"){
            float s=DeviceDpi/96f;
            float available=Math.Min(180*s,Height-442*s);
            if(available>20*s)Fit(g,Artwork,new RectangleF(17*s,Height-24*s-available,Width-34*s,available));
        }
    }
}
internal class FixedBackdropPanel:Panel
{
    readonly VScrollBar vertical=new(){Visible=false,TabStop=false,AccessibleName="Scroll page vertically"};
    readonly HScrollBar horizontal=new(){Visible=false,TabStop=false,AccessibleName="Scroll page horizontally"};
    bool arranging,moving,refreshPending;
    int offsetX,offsetY,maxX,maxY;
    [DefaultValue(false)] public bool SmoothScrolling {get;set;}
    protected override CreateParams CreateParams {get{var cp=base.CreateParams;if(SmoothScrolling)cp.ExStyle|=0x02000000;return cp;}}
    readonly System.Windows.Forms.Timer scrollAnimation=new(){Interval=15};
    readonly System.Diagnostics.Stopwatch scrollTime=new();
    int scrollFromX,scrollFromY,scrollTargetX,scrollTargetY;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Point AutoScrollPosition {get=>new(-offsetX,-offsetY);set{scrollAnimation.Stop();MoveContent(value.X,value.Y);}}
    public FixedBackdropPanel()
    {
        DoubleBuffered=true;ResizeRedraw=true;BackColor=Color.Transparent;
        Controls.Add(vertical);Controls.Add(horizontal);
        vertical.ValueChanged+=(_,_)=>{if(!arranging&&!moving){scrollAnimation.Stop();MoveContent(offsetX,vertical.Value);}};
        horizontal.ValueChanged+=(_,_)=>{if(!arranging&&!moving){scrollAnimation.Stop();MoveContent(horizontal.Value,offsetY);}};
        scrollAnimation.Tick+=(_,_)=>{
            double t=Math.Min(1,scrollTime.Elapsed.TotalMilliseconds/180),eased=1-Math.Pow(1-t,3);
            if(t>=1)scrollAnimation.Stop();
            MoveContent((int)Math.Round(scrollFromX+(scrollTargetX-scrollFromX)*eased),(int)Math.Round(scrollFromY+(scrollTargetY-scrollFromY)*eased));
            if(t>=1){Invalidate(true);RefreshTree(this);}
        };
        VisibleChanged+=(_,_)=>{if(!Visible)scrollAnimation.Stop();};
    }
    protected override void Dispose(bool disposing){if(disposing)scrollAnimation.Dispose();base.Dispose(disposing);}
    void SmoothMove(int x,int y)
    {
        scrollFromX=offsetX;scrollFromY=offsetY;scrollTargetX=Math.Clamp(x,0,maxX);scrollTargetY=Math.Clamp(y,0,maxY);
        scrollTime.Restart();scrollAnimation.Start();
    }
    protected override void OnControlAdded(ControlEventArgs e){base.OnControlAdded(e);if(e.Control!=null)e.Control.Enter+=RevealFocusedControl;}
    protected override void OnControlRemoved(ControlEventArgs e){if(e.Control!=null)e.Control.Enter-=RevealFocusedControl;base.OnControlRemoved(e);}
    void RevealFocusedControl(object? sender,EventArgs e)
    {
        if(!AutoScroll||sender is not Control child||child==vertical||child==horizontal)return;
        int w=ClientSize.Width-(vertical.Visible?vertical.Width:0),h=ClientSize.Height-(horizontal.Visible?horizontal.Height:0);
        int x=offsetX,y=offsetY;
        if(child.Left<0)x+=child.Left;else if(child.Right>w)x+=child.Right-w;
        if(child.Top<0)y+=child.Top;else if(child.Bottom>h)y+=child.Bottom-h;
        MoveContent(x,y);
    }
    protected override void AdjustFormScrollbars(bool displayScrollbars)
    {
        // Keep the native display rectangle stationary. ScrollWindowEx copies old
        // background pixels along with controls, which tears a fixed photograph.
        base.AdjustFormScrollbars(false);
        if(arranging||moving||vertical==null||horizontal==null)return;
        arranging=true;
        try{
            int extentW=AutoScrollMinSize.Width,extentH=AutoScrollMinSize.Height;
            foreach(Control child in Controls){if(child==vertical||child==horizontal)continue;extentW=Math.Max(extentW,child.Right+offsetX);extentH=Math.Max(extentH,child.Bottom+offsetY);}
            int barW=SystemInformation.VerticalScrollBarWidth,barH=SystemInformation.HorizontalScrollBarHeight;
            bool needV=AutoScroll&&extentH>ClientSize.Height,needH=AutoScroll&&extentW>ClientSize.Width;
            if(needV&&extentW>ClientSize.Width-barW)needH=true;
            if(needH&&extentH>ClientSize.Height-barH)needV=true;
            int viewW=Math.Max(1,ClientSize.Width-(needV?barW:0)),viewH=Math.Max(1,ClientSize.Height-(needH?barH:0));
            maxX=needH?Math.Max(0,extentW-viewW):0;maxY=needV?Math.Max(0,extentH-viewH):0;
            vertical.Visible=needV;horizontal.Visible=needH;
            vertical.SetBounds(ClientSize.Width-barW,0,barW,viewH);
            horizontal.SetBounds(0,ClientSize.Height-barH,viewW,barH);
            vertical.LargeChange=viewH;vertical.SmallChange=LogicalToDeviceUnits(24);vertical.Maximum=Math.Max(0,extentH-1);
            horizontal.LargeChange=viewW;horizontal.SmallChange=LogicalToDeviceUnits(24);horizontal.Maximum=Math.Max(0,extentW-1);
            vertical.BringToFront();horizontal.BringToFront();
            MoveContent(Math.Clamp(offsetX,0,maxX),Math.Clamp(offsetY,0,maxY));
        }finally{arranging=false;}
    }
    void MoveContent(int x,int y)
    {
        x=Math.Clamp(x,0,maxX);y=Math.Clamp(y,0,maxY);
        int dx=offsetX-x,dy=offsetY-y;if(dx==0&&dy==0)return;
        moving=true;SuspendLayout();
        try{
            offsetX=x;offsetY=y;
            foreach(Control child in Controls){if(child!=vertical&&child!=horizontal)child.Location=new Point(child.Left+dx,child.Top+dy);}
            vertical.Value=y;horizontal.Value=x;
        }finally{ResumeLayout(false);moving=false;}
        Invalidate(true);
        // Native edit fields can receive a late paint after their containing window
        // moves. Refresh those child windows after all position messages settle.
        if(!scrollAnimation.Enabled&&!refreshPending&&IsHandleCreated){refreshPending=true;BeginInvoke(()=>{refreshPending=false;if(!IsDisposed)RefreshTree(this);});}
    }
    static void RefreshTree(Control parent)
    {
        RefreshVisibleTree(parent,parent.RectangleToScreen(parent.ClientRectangle));
    }
    static void RefreshVisibleTree(Control parent,Rectangle viewport)
    {
        foreach(Control child in parent.Controls){
            if(!child.Visible)continue;
            var visible=Rectangle.Intersect(viewport,child.RectangleToScreen(child.ClientRectangle));
            if(visible.IsEmpty)continue;
            child.Invalidate(child.RectangleToClient(visible));
            RefreshVisibleTree(child,visible);
        }
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if(AutoScroll&&(maxY>0||maxX>0)){
            if(SmoothScrolling){
                int lines=SystemInformation.MouseWheelScrollLines;
                int distance=lines<0?ClientSize.Height:LogicalToDeviceUnits(Math.Max(1,lines)*24);
                int delta=(int)Math.Round(e.Delta/120d*distance);
                int x=scrollAnimation.Enabled?scrollTargetX:offsetX,y=scrollAnimation.Enabled?scrollTargetY:offsetY;
                SmoothMove(maxY>0?x:x-delta,maxY>0?y-delta:y);
            }else if(maxY>0)MoveContent(offsetX,offsetY-e.Delta);else MoveContent(offsetX-e.Delta,offsetY);
            if(e is HandledMouseEventArgs handled)handled.Handled=true;
        }
        base.OnMouseWheel(e);
    }
    protected override void WndProc(ref Message m)
    {
        if(m.LParam==IntPtr.Zero&&m.Msg is 0x0114 or 0x0115){
            bool v=m.Msg==0x0115;int position=v?offsetY:offsetX,limit=v?maxY:maxX,page=v?vertical.LargeChange:horizontal.LargeChange;
            int command=(int)((long)m.WParam&0xffff),step=LogicalToDeviceUnits(24);
            int next=command switch{0=>position-step,1=>position+step,2=>position-page,3=>position+page,4 or 5=>(int)(((long)m.WParam>>16)&0xffff),6=>0,7=>limit,_=>position};
            if(SmoothScrolling&&command is 0 or 1 or 2 or 3)SmoothMove(v?offsetX:next,v?next:offsetY);
            else{scrollAnimation.Stop();MoveContent(v?offsetX:next,v?next:offsetY);}return;
        }
        base.WndProc(ref m);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Control? ancestor=Parent;while(ancestor!=null&&ancestor is not HarborSurface)ancestor=ancestor.Parent;
        if(ancestor is HarborSurface surface){var origin=surface.PointToClient(PointToScreen(Point.Empty));surface.PaintBackdrop(e.Graphics,origin,ClientSize);}
        else base.OnPaintBackground(e);
    }
}
internal sealed class PaintedPanel:FixedBackdropPanel
{
    public Action<Graphics,float,float>? Draw;
    public PaintedPanel(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Color.Transparent;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;var saved=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;float scale=DeviceDpi/96f;g.ScaleTransform(scale,scale);Draw?.Invoke(g,Width/scale,Height/scale);g.Restore(saved);}
}



