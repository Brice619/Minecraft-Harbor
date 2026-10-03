using System.ComponentModel;

namespace MinecraftHarbor;
internal sealed class BackupViewport:PaintedPanelBase
{
    internal static readonly Color Surface=Color.FromArgb(10,30,44);
    readonly BackupScrollRail rail;
    readonly System.Windows.Forms.Timer animation=new(){Interval=15};
    readonly System.Diagnostics.Stopwatch watch=new();
    int offset,extent,start,target;
    internal int Limit=>Math.Max(0,extent-Height);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Point AutoScrollPosition{get=>new(0,-offset);set{animation.Stop();MoveTo(value.Y);}}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Size AutoScrollMinSize{get=>new(0,extent);set{extent=value.Height;MoveTo(Math.Min(offset,Limit));rail.Invalidate();}}
    internal int Offset=>offset;
    public BackupViewport(){BackColor=Surface;rail=new(this);Controls.Add(rail);SizeChanged+=(_,_)=>{int u(int n)=>LogicalToDeviceUnits(n);rail.SetBounds(Width-u(15),u(3),u(12),Math.Max(1,Height-u(6)));MoveTo(Math.Min(offset,Limit));};animation.Tick+=(_,_)=>{float t=Math.Min(1,watch.ElapsedMilliseconds/160f);MoveTo((int)(start+(target-start)*(1-Math.Pow(1-t,3))));if(t>=1)animation.Stop();};AccessibleName="Backup list";}
    protected override void OnControlAdded(ControlEventArgs e){base.OnControlAdded(e);if(e.Control!=null&&e.Control is not BackupScrollRail){e.Control.MouseWheel+=Wheel;e.Control.ControlAdded+=(_,added)=>{if(added.Control!=null)added.Control.MouseWheel+=Wheel;};foreach(Control child in e.Control.Controls)child.MouseWheel+=Wheel;}}
    void Wheel(object? sender,MouseEventArgs e){if(e is HandledMouseEventArgs handled&&handled.Handled)return;ScrollSmooth(-e.Delta/120*LogicalToDeviceUnits(72));if(e is HandledMouseEventArgs done)done.Handled=true;}
    protected override void OnMouseWheel(MouseEventArgs e){Wheel(this,e);}
    internal void ScrollSmooth(int delta){start=offset;target=Math.Clamp((animation.Enabled?target:offset)+delta,0,Limit);watch.Restart();animation.Start();}
    internal void MoveTo(int value){value=Math.Clamp(value,0,Limit);int delta=offset-value;offset=value;SuspendLayout();foreach(Control child in Controls)if(child!=rail)child.Top+=delta;ResumeLayout(false);rail.BringToFront();rail.Invalidate();Invalidate();}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Surface);}
    protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
}
internal sealed class BackupScrollRail:Control
{
    readonly BackupViewport viewport;bool dragging,hover;float grab;
    public BackupScrollRail(BackupViewport viewport){this.viewport=viewport;BackColor=BackupViewport.Surface;Cursor=Cursors.Hand;TabStop=true;AccessibleName="Scroll backups";SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
    RectangleF Thumb(){float pad=2*DeviceDpi/96f,track=Math.Max(1,Height-2*pad),height=Math.Clamp(track*viewport.Height/Math.Max(1,viewport.AutoScrollMinSize.Height),Math.Min(track,32*DeviceDpi/96f),track);return new(pad,pad+(track-height)*viewport.Offset/Math.Max(1,viewport.Limit),Math.Max(1,Width-2*pad),height);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);if(viewport.Limit<=0)return;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using var track=HarborTheme.Round(new(1,1,Math.Max(1,Width-2),Math.Max(1,Height-2)),5);using var background=new SolidBrush(Color.FromArgb(14,38,57));e.Graphics.FillPath(background,track);using var path=HarborTheme.Round(Thumb(),5);using var fill=new SolidBrush(hover||dragging?Color.FromArgb(57,147,185):Color.FromArgb(39,86,119));e.Graphics.FillPath(fill,path);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();var thumb=Thumb();if(thumb.Contains(e.Location)){dragging=true;grab=e.Y-thumb.Top;Capture=true;}else viewport.ScrollSmooth(e.Y<thumb.Y?-viewport.Height:viewport.Height);Invalidate();}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!dragging)return;var thumb=Thumb();float pad=2*DeviceDpi/96f;viewport.MoveTo((int)((e.Y-grab-pad)/Math.Max(1,Height-2*pad-thumb.Height)*viewport.Limit));}
    protected override void OnMouseUp(MouseEventArgs e){dragging=false;Capture=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseWheel(MouseEventArgs e){viewport.ScrollSmooth(-e.Delta/120*LogicalToDeviceUnits(72));}
    protected override bool IsInputKey(Keys keyData)=>keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End||base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e){int amount=e.KeyCode switch{Keys.Up=>-LogicalToDeviceUnits(64),Keys.Down=>LogicalToDeviceUnits(64),Keys.PageUp=>-viewport.Height,Keys.PageDown=>viewport.Height,Keys.Home=>-viewport.Limit,Keys.End=>viewport.Limit,_=>0};if(amount!=0){viewport.ScrollSmooth(amount);e.Handled=true;}base.OnKeyDown(e);}
}
