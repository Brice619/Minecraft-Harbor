using System.Drawing.Drawing2D;
namespace MinecraftHarbor;
internal sealed class CreationTile:FixedBackdropPanel
{
    readonly string title,description,icon;readonly Image? art;bool hover;
    internal CreationTile(string title,string description,string path,string icon="cube"){this.title=title;this.description=description;this.icon=icon;AccessibleName=title;AccessibleRole=AccessibleRole.PushButton;TabStop=true;Cursor=Cursors.Hand;DoubleBuffered=true;ResizeRedraw=true;if(File.Exists(path)){using var source=Image.FromFile(path);art=new Bitmap(source);}else{var assembly=typeof(CreationTile).Assembly;var name=assembly.GetManifestResourceNames().FirstOrDefault(n=>n.EndsWith("create-"+Path.GetFileName(path)));if(name!=null){using var stream=assembly.GetManifestResourceStream(name)!;using var source=Image.FromStream(stream);art=new Bitmap(source);}}}
    protected override void Dispose(bool disposing){if(disposing)art?.Dispose();base.Dispose(disposing);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode is Keys.Enter or Keys.Space){OnClick(EventArgs.Empty);e.Handled=true;}base.OnKeyDown(e);}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;float s=DeviceDpi/96f;g.SmoothingMode=SmoothingMode.AntiAlias;using var border=HarborTheme.Round(new RectangleF(s/2,s/2,Width-s,Height-s),10*s);using var pen=new Pen(hover?HarborTheme.Green:Color.FromArgb(55,97,120),s);
        using var fill=new SolidBrush(Color.FromArgb(17,30,41));g.FillPath(fill,border);
        int bottom=(int)(132*s),photoHeight=Height-bottom;var dest=new Rectangle(0,0,Width,photoHeight);
        if(art!=null){var state=g.Save();g.SetClip(border,CombineMode.Intersect);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;float ratio=Math.Max(dest.Width/(float)art.Width,dest.Height/(float)art.Height);float sw=dest.Width/ratio,sh=dest.Height/ratio;g.DrawImage(art,dest,new RectangleF((art.Width-sw)/2,(art.Height-sh)/2,sw,sh),GraphicsUnit.Pixel);g.Restore(state);}g.DrawPath(pen,border);
        HarborTheme.Icon(g,icon,25*s,Height-100*s,48*s,HarborTheme.Ink);
        using var heading=new Font("Segoe UI",25,FontStyle.Bold);using var body=new Font("Segoe UI",11);
        TextRenderer.DrawText(g,title,heading,new Rectangle((int)(94*s),Height-(int)(119*s),Width-(int)(115*s),(int)(44*s)),HarborTheme.Ink,TextFormatFlags.NoPadding|TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g,description,body,new Rectangle((int)(94*s),Height-(int)(70*s),Width-(int)(125*s),(int)(64*s)),HarborTheme.Muted,TextFormatFlags.NoPadding|TextFormatFlags.WordBreak);
        using var arrow=new Pen(HarborTheme.Ink,2*s);float ax=Width-24*s,ay=Height-52*s;g.DrawLine(arrow,ax-18*s,ay,ax,ay);g.DrawLine(arrow,ax-7*s,ay-7*s,ax,ay);g.DrawLine(arrow,ax-7*s,ay+7*s,ax,ay);
        if(Focused)ControlPaint.DrawFocusRectangle(g,new Rectangle(4,4,Width-8,Height-8));
    }
}
internal sealed class VanillaVersionList:Control
{
    readonly List<VanillaVersion> versions;readonly VScrollBar bar=new(){Dock=DockStyle.Right};readonly System.Windows.Forms.Timer timer=new(){Interval=15};readonly System.Diagnostics.Stopwatch watch=new();
    float offset,from,target;int selected,hover=-1;bool syncing;
    public event Action<VanillaVersion>? Chosen;
    int RowHeight=>LogicalToDeviceUnits(66);
    internal VanillaVersionList(List<VanillaVersion> versions){this.versions=versions;DoubleBuffered=true;ResizeRedraw=true;TabStop=true;BackColor=Color.FromArgb(17,29,40);Controls.Add(bar);bar.ValueChanged+=(_,_)=>{if(!syncing){timer.Stop();offset=target=bar.Value;Invalidate();}};timer.Tick+=(_,_)=>{double t=Math.Min(1,watch.Elapsed.TotalMilliseconds/160);offset=from+(target-from)*(float)(1-Math.Pow(1-t,3));SyncBar();Invalidate();if(t>=1)timer.Stop();};}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    protected override void OnResize(EventArgs e){base.OnResize(e);if(bar==null)return;bar.LargeChange=Math.Max(1,Height);bar.SmallChange=RowHeight;bar.Maximum=Math.Max(0,versions.Count*RowHeight-1);offset=target=Math.Clamp(offset,0,Math.Max(0,versions.Count*RowHeight-Height));SyncBar();}
    void SyncBar(){syncing=true;bar.Value=Math.Clamp((int)offset,0,Math.Max(0,bar.Maximum-bar.LargeChange+1));syncing=false;}
    protected override void OnMouseWheel(MouseEventArgs e){from=offset;target=Math.Clamp(target-e.Delta/120f*RowHeight*2,0,Math.Max(0,versions.Count*RowHeight-Height));watch.Restart();timer.Start();if(e is HandledMouseEventArgs handled)handled.Handled=true;}
    protected override void OnMouseMove(MouseEventArgs e){int row=(int)((e.Y+offset)/RowHeight);if(hover!=row){hover=row;Invalidate();}base.OnMouseMove(e);}
    protected override void OnMouseLeave(EventArgs e){hover=-1;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseClick(MouseEventArgs e){int row=(int)((e.Y+offset)/RowHeight);if(e.X<Width-bar.Width&&row>=0&&row<versions.Count){selected=row;Chosen?.Invoke(versions[row]);}base.OnMouseClick(e);}
    protected override bool IsInputKey(Keys keyData)=>keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End||base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Enter){Chosen?.Invoke(versions[selected]);e.Handled=true;}else if(e.KeyCode is Keys.Up or Keys.Down or Keys.Home or Keys.End){selected=e.KeyCode switch{Keys.Home=>0,Keys.End=>versions.Count-1,Keys.Up=>Math.Max(0,selected-1),_=>Math.Min(versions.Count-1,selected+1)};timer.Stop();if(selected*RowHeight<offset)offset=selected*RowHeight;if((selected+1)*RowHeight>offset+Height)offset=(selected+1)*RowHeight-Height;target=offset;SyncBar();Invalidate();e.Handled=true;}base.OnKeyDown(e);}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float s=DeviceDpi/96f;using var big=new Font("Segoe UI",16,FontStyle.Bold);using var title=new Font("Segoe UI",11,FontStyle.Bold);using var body=new Font("Segoe UI",10);
        for(int i=Math.Max(0,(int)(offset/RowHeight));i<versions.Count&&i*RowHeight-offset<Height;i++){int y=(int)(i*RowHeight-offset);var r=new RectangleF(1,y+2,Width-bar.Width-7,RowHeight-5);using var path=HarborTheme.Round(r,7*s);using var fill=new SolidBrush(i==hover||i==selected?Color.FromArgb(29,62,82):Color.FromArgb(25,41,55));g.FillPath(fill,path);using var pen=new Pen(i==hover||i==selected?Color.FromArgb(88,190,219):Color.FromArgb(39,60,76));g.DrawPath(pen,path);
            var v=versions[i];var story=v.Story;TextRenderer.DrawText(g,v.Id,big,new Rectangle((int)(17*s),y,(int)(120*s),RowHeight),HarborTheme.Ink,TextFormatFlags.NoPadding|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            int x=(int)(145*s),width=Width-bar.Width-x-(int)(36*s);TextRenderer.DrawText(g,story.Era,title,new Rectangle(x,y+(int)(9*s),width,(int)(23*s)),HarborTheme.Ink,TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g,story.Description,body,new Rectangle(x,y+(int)(35*s),width,(int)(24*s)),HarborTheme.Muted,TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g,"›",big,new Rectangle(Width-bar.Width-(int)(29*s),y,(int)(20*s),RowHeight),HarborTheme.Ink,TextFormatFlags.NoPadding|TextFormatFlags.VerticalCenter);
        }
    }
}
