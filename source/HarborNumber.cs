namespace MinecraftHarbor;
internal sealed class HarborNumber:PaintedPanelBase
{
    readonly TextBox input=new(){BorderStyle=BorderStyle.None,BackColor=Color.FromArgb(20,34,46),ForeColor=HarborTheme.Ink,Font=new Font("Segoe UI",11)};
    readonly HarborButton up=new(){Chrome=true,Glyph="up",AccessibleName="Increase",TabStop=false},down=new(){Chrome=true,Glyph="down",AccessibleName="Decrease",TabStop=false};
    readonly int minimum;
    internal HarborNumber(int value,int min=0)
    {
        minimum=min;input.Text=value.ToString();Controls.AddRange(new Control[]{input,up,down});up.Click+=(_,_)=>Step(1);down.Click+=(_,_)=>Step(-1);
        input.KeyDown+=(_,e)=>{if(e.KeyCode is Keys.Up or Keys.Down){Step(e.KeyCode==Keys.Up?1:-1);e.SuppressKeyPress=true;}};
        SizeChanged+=(_,_)=>{int pad=LogicalToDeviceUnits(11),width=LogicalToDeviceUnits(28);input.SetBounds(pad,Math.Max(0,(Height-input.PreferredSize.Height)/2),Math.Max(1,Width-width-pad*2),input.PreferredSize.Height);up.SetBounds(Width-width-2,1,width,(Height-2)/2);down.SetBounds(Width-width-2,Height/2,width,(Height-2)/2);};
    }
    internal string ReadValue(){if(!int.TryParse(input.Text,out int value)||value<minimum)throw new ArgumentException((AccessibleName??"Value")+" must be a whole number of at least "+minimum+".");return value.ToString(System.Globalization.CultureInfo.InvariantCulture);}
    internal void SetValue(string value)=>input.Text=value;
    void Step(int direction){if(int.TryParse(input.Text,out int value)){long next=(long)value+direction;input.Text=Math.Clamp(next,minimum,int.MaxValue).ToString();}}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);input.AccessibleName=AccessibleName;float s=DeviceDpi/96f;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using var path=HarborTheme.Round(new RectangleF(s,s,Width-2*s,Height-2*s),6*s);using var fill=new SolidBrush(Color.FromArgb(20,34,46));e.Graphics.FillPath(fill,path);using var pen=new Pen(Color.FromArgb(57,82,105),s);e.Graphics.DrawPath(pen,path);}
}
