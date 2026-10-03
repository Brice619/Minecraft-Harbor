using System.Drawing.Drawing2D;
namespace MinecraftHarbor;
internal sealed class SetupProgressPanel:FixedBackdropPanel
{
    SetupProgress progress=new(0,0,"Preparing server files…");Bitmap? frame;internal int FrameBuildCount;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal SetupProgress Progress{get=>progress;set{if(progress==value)return;progress=value;ResetFrame();}}
    readonly System.Windows.Forms.Timer animation=new(){Interval=40};float angle;
    internal SetupProgressPanel(){DoubleBuffered=true;ResizeRedraw=true;animation.Tick+=(_,_)=>{if(!Visible||Progress.Complete)return;angle=(angle+12)%360;float scale=DeviceDpi/96f,row=(Height/scale-108)/5,y=100+Progress.Step*row+(row-23)/2;Invalidate(Rectangle.Ceiling(new RectangleF(23*scale,(y-4)*scale,31*scale,31*scale)));};animation.Start();}
    void ResetFrame(){frame?.Dispose();frame=null;Invalidate();}
    protected override void OnSizeChanged(EventArgs e){ResetFrame();base.OnSizeChanged(e);}
    protected override void OnDpiChangedAfterParent(EventArgs e){ResetFrame();base.OnDpiChangedAfterParent(e);}
    protected override void Dispose(bool disposing){if(disposing){animation.Dispose();frame?.Dispose();}base.Dispose(disposing);}
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Width<1||Height<1)return;
        if(frame==null){frame=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppRgb);frame.SetResolution(DeviceDpi,DeviceDpi);using var graphics=Graphics.FromImage(frame);RenderFrame(graphics);FrameBuildCount++;}
        var g=e.Graphics;float s=DeviceDpi/96f;var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;using var clip=HarborTheme.Round(new RectangleF(s,s,Width-2*s,Height-2*s),10*s);g.SetClip(clip,CombineMode.Intersect);g.DrawImageUnscaled(frame,0,0);g.Restore(state);
        if(!Progress.Complete){float row=(Height/s-108)/5,y=100+Progress.Step*row;var circle=new RectangleF(27*s,(y+(row-23)/2)*s,23*s,23*s);g.SmoothingMode=SmoothingMode.AntiAlias;using var cyan=new Pen(Color.FromArgb(73,217,251),3*s);g.DrawArc(cyan,circle,angle,245);}
    }
    void RenderFrame(Graphics g)
    {
        float s=DeviceDpi/96f;g.ScaleTransform(s,s);g.SmoothingMode=SmoothingMode.AntiAlias;float w=Width/s,h=Height/s;
        using var outline=HarborTheme.Round(new RectangleF(1,1,w-2,h-2),10);using var fill=new LinearGradientBrush(new RectangleF(0,0,w,h),Color.FromArgb(18,39,55),Color.FromArgb(13,28,40),LinearGradientMode.ForwardDiagonal);g.FillPath(fill,outline);using var border=new Pen(Color.FromArgb(43,86,113));g.DrawPath(border,outline);
        using var heading=new Font("Segoe UI",15,FontStyle.Bold);using var body=new Font("Segoe UI",10);using var title=new Font("Segoe UI",11,FontStyle.Bold);using var percent=new Font("Segoe UI",19,FontStyle.Bold);
        void Text(string value,Font font,float x,float y,float width,float height,Color color,StringAlignment align=StringAlignment.Near){var state=g.Save();g.ResetTransform();var rect=Rectangle.Round(new RectangleF(x*s,y*s,width*s,height*s));var flags=TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.PreserveGraphicsClipping;if(align==StringAlignment.Far)flags|=TextFormatFlags.Right;TextRenderer.DrawText(g,value,font,rect,color,flags);g.Restore(state);}
        Text(Progress.Complete?"Your server is ready":"Setting up your server…",heading,25,13,w-50,29,HarborTheme.Ink);Text(Progress.Detail,body,25,44,w-50,25,HarborTheme.Muted);
        var bar=new RectangleF(25,78,w-142,15);using var track=HarborTheme.Round(bar,7.5f);using var trackBrush=new SolidBrush(Color.FromArgb(31,58,78));g.FillPath(trackBrush,track);
        float amount=bar.Width*Progress.Percent/100f;if(amount>0){using var green=new SolidBrush(Color.FromArgb(0,220,128));using var active=HarborTheme.Round(new RectangleF(bar.X,bar.Y,Math.Max(1,amount),bar.Height),Math.Min(7.5f,amount/2));g.FillPath(green,active);}
        Text(Progress.Percent+"%",percent,w-108,67,85,35,HarborTheme.Ink,StringAlignment.Far);
        string[] names={"Creating server files",Progress.WorldDeferred?"Preparing world":"Generating world","Applying server settings","Preparing startup configuration","Finalizing setup"};
        float row=(h-108)/5;
        for(int i=0;i<5;i++){
            float y=100+i*row;bool done=Progress.Complete||i<Progress.Step,active=i==Progress.Step&&!Progress.Complete;using var line=new Pen(Color.FromArgb(33,63,82));g.DrawLine(line,25,y,w-25,y);
            var circle=new RectangleF(27,y+(row-23)/2,23,23);
            if(done){using var green=new SolidBrush(Color.FromArgb(0,222,128));g.FillEllipse(green,circle);using var check=new Pen(Color.FromArgb(6,35,36),2){StartCap=LineCap.Round,EndCap=LineCap.Round};g.DrawLines(check,new[]{new PointF(circle.X+6,circle.Y+12),new PointF(circle.X+10,circle.Y+16),new PointF(circle.X+18,circle.Y+8)});}
            else{using var ring=new Pen(Color.FromArgb(74,104,130),3);g.DrawEllipse(ring,circle);}
            Text(names[i],title,69,y,w-226,row,HarborTheme.Ink);
            Text(done?(i==1&&Progress.WorldDeferred?"On first start":"Completed"):active?"In progress…":"Pending",body,w-160,y,135,row,HarborTheme.Muted,StringAlignment.Far);
        }
    }
}
