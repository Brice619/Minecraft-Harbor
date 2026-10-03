using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.ComponentModel;
namespace MinecraftHarbor;
internal sealed class PerformanceGraph:Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public PerformanceHistory History {get;set;}=new();
    [DefaultValue(false)] public bool ShowMemory {get;set;}
    [DefaultValue(80d)] public double MemoryLimit {get;set;}=80;
    public PerformanceGraph(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Color.FromArgb(24,31,39);AccessibleName="Server performance over the last hour";}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;var original=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;float scale=DeviceDpi/96f;g.ScaleTransform(scale,scale);
        float w=Width/scale,h=Height/scale;var plot=new RectangleF(44,8,w-55,h-54);if(plot.Width<50||plot.Height<20){g.Restore(original);return;}
        using var grid=new Pen(Color.FromArgb(43,52,64));using var axis=new Pen(Color.FromArgb(97,112,130));
        double max=ShowMemory?Math.Max(MemoryLimit,Math.Ceiling(History.Samples.Select(s=>s.MemoryGB).DefaultIfEmpty().Max())):100;
        for(int i=0;i<=4;i++){float y=plot.Bottom-plot.Height*i/4;g.DrawLine(grid,plot.Left,y,plot.Right,y);HarborTheme.Text(g,(max*i/4).ToString("0")+(ShowMemory?"G":"%"),11,0,y-8,HarborTheme.Muted,false,36,true);}
        for(int i=0;i<=6;i++){float x=plot.Left+plot.Width*i/6;g.DrawLine(grid,x,plot.Top,x,plot.Bottom);string text=i==0?"1h ago":i==6?"Now":(60-i*10)+"m ago";HarborTheme.Text(g,text,10.5f,x-25,plot.Bottom+8,HarborTheme.Muted,false,54);}
        g.DrawLine(axis,plot.Left,plot.Top,plot.Left,plot.Bottom);g.DrawLine(axis,plot.Left,plot.Bottom,plot.Right,plot.Bottom);
        var now=DateTime.UtcNow;var cutoff=now.AddHours(-1);var segments=new List<List<PointF>>();List<PointF>? segment=null;DateTime? last=null;
        foreach(var sample in History.Samples.Where(s=>s.Utc>=cutoff&&s.Utc<=now)){
            double? value=ShowMemory?sample.MemoryGB:sample.CpuPercent;if(value==null){segment=null;last=null;continue;}
            if(segment==null||last.HasValue&&(sample.Utc-last.Value).TotalSeconds>15){segment=new();segments.Add(segment);}
            segment.Add(new(plot.Left+(float)((sample.Utc-cutoff).TotalSeconds/3600)*plot.Width,plot.Bottom-(float)(value/max)*plot.Height));last=sample.Utc;
        }
        var saved=g.Save();g.SetClip(plot);using var line=new Pen(HarborTheme.Green,1.8f);using var fill=new LinearGradientBrush(plot,Color.FromArgb(90,61,204,136),Color.FromArgb(8,61,204,136),LinearGradientMode.Vertical);
        foreach(var points in segments){if(points.Count<2)continue;using var area=new GraphicsPath();area.AddLines(points.ToArray());area.AddLine(points[^1],new PointF(points[^1].X,plot.Bottom));area.AddLine(new PointF(points[^1].X,plot.Bottom),new PointF(points[0].X,plot.Bottom));area.CloseFigure();g.FillPath(fill,area);g.DrawLines(line,points.ToArray());}
        g.Restore(saved);
        AccessibleDescription=(ShowMemory?"Memory usage":"CPU usage")+". Time axis runs from one hour ago to now. Missing history is left empty.";
        g.Restore(original);
    }
}
