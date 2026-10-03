namespace MinecraftHarbor;
internal sealed class HarborInputFrame:PaintedPanelBase
{
    public HarborInputFrame(Control input)
    {
        Input=input;Controls.Add(input);input.BackColor=Color.FromArgb(20,34,46);input.ForeColor=HarborTheme.Ink;
        if(input is TextBox box){box.BorderStyle=BorderStyle.None;box.Font=new Font("Segoe UI",12);}
        SizeChanged+=(_,_)=>{int pad=LogicalToDeviceUnits(12);input.SetBounds(pad,Math.Max(0,(Height-input.PreferredSize.Height)/2),Math.Max(1,Width-pad*2),input.PreferredSize.Height);};
    }
    public Control Input {get;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);float s=DeviceDpi/96f;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using var path=HarborTheme.Round(new RectangleF(s,s,Width-2*s,Height-2*s),7*s);using var fill=new SolidBrush(Color.FromArgb(20,34,46));e.Graphics.FillPath(fill,path);using var pen=new Pen(Color.FromArgb(57,82,105),s);e.Graphics.DrawPath(pen,path);}
}
internal class PaintedPanelBase:Panel
{
    public PaintedPanelBase(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Color.Transparent;}
}
