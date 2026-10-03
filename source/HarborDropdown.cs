using System.ComponentModel;
using System.Drawing.Drawing2D;
namespace MinecraftHarbor;

internal sealed class HarborDropdown:Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public List<object> Items {get;}=new();
    int index=-1;
    bool hover,pressed,suppressClick;
    string search="";
    DateTime searchTime;
    ToolStripDropDown? menu;
    internal bool IsExpanded=>menu?.Visible==true;
    [DefaultValue(-1)] public int SelectedIndex {
        get=>index;
        set{
            if(value< -1||value>=Items.Count)throw new ArgumentOutOfRangeException(nameof(value));
            if(index==value){Invalidate();return;}index=value;Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1);SelectedIndexChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public object? SelectedItem {get=>index>=0&&index<Items.Count?Items[index]:null;set=>SelectedIndex=value==null?-1:Items.IndexOf(value);}
    public event EventHandler? SelectedIndexChanged;
    public HarborDropdown(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Cursor=Cursors.Hand;TabStop=true;BackColor=Color.FromArgb(20,27,35);ForeColor=HarborTheme.Muted;AccessibleRole=AccessibleRole.ComboBox;}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;float s=DeviceDpi/96f;var rect=new RectangleF(s,s,Width-2*s,Height-2*s);
        bool active=IsExpanded;var color=Enabled?ForeColor:Color.FromArgb(101,116,132);
        using var path=HarborTheme.Round(rect,6*s);
        using var fill=new LinearGradientBrush(rect,active||pressed?Color.FromArgb(40,62,57):hover?Color.FromArgb(43,56,72):Color.FromArgb(32,42,55),Color.FromArgb(20,28,38),LinearGradientMode.Vertical);g.FillPath(fill,path);
        using var border=new Pen(active||Focused?HarborTheme.Green:hover?Color.FromArgb(142,164,189):Color.FromArgb(70,85,104),(active||Focused?1.4f:1)*s);g.DrawPath(border,path);
        float split=Width-34*s;using(var divider=new Pen(Color.FromArgb(67,83,99),s))g.DrawLine(divider,split,9*s,split,Height-9*s);
        TextRenderer.DrawText(g,SelectedItem?.ToString()??"Choose…",Font,new Rectangle((int)(34*s),0,Math.Max(1,Width-(int)(68*s)),Height),color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);
        using var arrow=new Pen(active?HarborTheme.Green:color,1.7f*s){StartCap=LineCap.Round,EndCap=LineCap.Round};float x=Width-17*s,y=Height/2f,d=active?-1:1;
        g.DrawLines(arrow,new[]{new PointF(x-4*s,y-d*2*s),new PointF(x,y+d*2*s),new PointF(x+4*s,y-d*2*s)});
    }
    internal ToolStripDropDown OpenMenu()
    {
        if(menu==null){
            menu=new ToolStripDropDown{BackColor=Color.FromArgb(23,31,41),ForeColor=HarborTheme.Ink,AccessibleRole=AccessibleRole.MenuPopup,Renderer=new ChoiceRenderer(),LayoutStyle=ToolStripLayoutStyle.Flow,AutoSize=false,Padding=new Padding(1),DropShadowEnabled=true};
            if(menu.LayoutSettings is FlowLayoutSettings layout){layout.FlowDirection=FlowDirection.TopDown;layout.WrapContents=false;}
            menu.Opened+=(_,_)=>{LayoutMenu();Invalidate();AccessibilityNotifyClients(AccessibleEvents.StateChange,-1);};
            menu.Closed+=(_,e)=>{pressed=false;suppressClick=e.CloseReason==ToolStripDropDownCloseReason.AppClicked&&ClientRectangle.Contains(PointToClient(Cursor.Position));Invalidate();AccessibilityNotifyClients(AccessibleEvents.StateChange,-1);};
        }
        if(menu.Visible)return menu;
        while(menu.Items.Count>0){var old=menu.Items[0];menu.Items.RemoveAt(0);old.Dispose();}
        menu.Font=Font;int rowHeight=LogicalToDeviceUnits(36);menu.Size=new Size(Width,rowHeight*Items.Count+menu.Padding.Vertical);
        if(Items.Count>8){
            int height=rowHeight*8;
            var choices=new ListBox{BorderStyle=BorderStyle.None,BackColor=Color.FromArgb(23,31,41),ForeColor=HarborTheme.Ink,Font=Font,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=rowHeight,IntegralHeight=false,Size=new Size(Width-2,height)};
            choices.Items.AddRange(Items.ToArray());choices.SelectedIndex=index;
            choices.DrawItem+=(_,e)=>{if(e.Index<0)return;using var fill=new SolidBrush((e.State&DrawItemState.Selected)!=0?Color.FromArgb(52,77,70):choices.BackColor);e.Graphics.FillRectangle(fill,e.Bounds);TextRenderer.DrawText(e.Graphics,choices.Items[e.Index].ToString(),Font,e.Bounds,HarborTheme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);};
            void Choose(){if(choices.SelectedIndex<0)return;SelectedIndex=choices.SelectedIndex;menu.Close();Focus();}
            choices.MouseClick+=(_,e)=>{if(choices.IndexFromPoint(e.Location)>=0)Choose();};choices.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){Choose();e.Handled=true;}};
            menu.Items.Add(new ToolStripControlHost(choices){AutoSize=false,Size=choices.Size,Padding=Padding.Empty,Margin=Padding.Empty});menu.Size=new Size(Width,height+2);menu.Show(this,new Point(0,Height+LogicalToDeviceUnits(4)));choices.TopIndex=Math.Max(0,index-3);choices.Focus();return menu;
        }
        for(int i=0;i<Items.Count;i++){
            int selected=i;var item=new ToolStripMenuItem(Items[i].ToString()){TextAlign=ContentAlignment.MiddleCenter,ForeColor=HarborTheme.Ink,AutoSize=false,Margin=Padding.Empty,Size=new Size(menu.ClientSize.Width-menu.Padding.Horizontal,rowHeight)};
            item.Click+=(_,_)=>{SelectedIndex=selected;Focus();};menu.Items.Add(item);
        }
        if(Items.Count>0){menu.Show(this,new Point(0,Height+LogicalToDeviceUnits(4)));if(index>=0)menu.Items[index].Select();}
        return menu;
    }
    void LayoutMenu()
    {
        if(menu==null)return;
        if(menu.Items.Count==1&&menu.Items[0] is ToolStripControlHost host){menu.Padding=new Padding(1);menu.ClientSize=new Size(Width,LogicalToDeviceUnits(36)*8+2);host.Size=new Size(Width-2,menu.ClientSize.Height-2);return;}
        // Opening on a scaled display can change the native menu's padding.
        menu.SuspendLayout();menu.Padding=new Padding(1);int rowHeight=LogicalToDeviceUnits(36);
        menu.ClientSize=new Size(Width,rowHeight*menu.Items.Count+2);
        foreach(ToolStripItem item in menu.Items){item.Margin=Padding.Empty;item.Size=new Size(menu.ClientSize.Width-2,rowHeight);}
        menu.ResumeLayout(true);
    }
    protected override void OnClick(EventArgs e){base.OnClick(e);Focus();if(suppressClick){suppressClick=false;return;}if(IsExpanded)menu!.Close();else OpenMenu();}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override bool IsInputKey(Keys keyData)=>(keyData&Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End||base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.KeyCode is Keys.Space or Keys.Enter or Keys.F4||e.Alt&&e.KeyCode==Keys.Down){if(IsExpanded)menu!.Close();else OpenMenu();e.SuppressKeyPress=true;}
        else if(e.KeyCode==Keys.Escape&&IsExpanded){menu!.Close();e.SuppressKeyPress=true;}
        else if(Items.Count>0&&e.KeyCode is Keys.Up or Keys.Down or Keys.Home or Keys.End){SelectedIndex=e.KeyCode switch{Keys.Home=>0,Keys.End=>Items.Count-1,Keys.Down=>Math.Min(Items.Count-1,index+1),_=>Math.Max(0,index-1)};e.SuppressKeyPress=true;}
    }
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);if(char.IsControl(e.KeyChar))return;
        if((DateTime.UtcNow-searchTime).TotalMilliseconds>900)search="";searchTime=DateTime.UtcNow;search+=e.KeyChar;
        int match=Items.FindIndex(item=>(item.ToString()??"").StartsWith(search,StringComparison.CurrentCultureIgnoreCase));if(match>=0)SelectedIndex=match;e.Handled=true;
    }
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void Dispose(bool disposing){if(disposing)menu?.Dispose();base.Dispose(disposing);}
    protected override AccessibleObject CreateAccessibilityInstance()=>new DropdownAccessibility(this);
    sealed class DropdownAccessibility(HarborDropdown owner):ControlAccessibleObject(owner)
    {
        public override string? Value {get=>owner.SelectedItem?.ToString()??"";set{int match=owner.Items.FindIndex(i=>i.ToString()==value);if(match>=0)owner.SelectedIndex=match;}}
        public override string DefaultAction=>owner.IsExpanded?"Collapse":"Expand";
        public override AccessibleStates State=>base.State|AccessibleStates.Focusable|(owner.IsExpanded?AccessibleStates.Expanded:AccessibleStates.Collapsed);
        public override void DoDefaultAction(){if(owner.IsExpanded)owner.menu!.Close();else owner.OpenMenu();}
    }
    sealed class ChoiceRenderer:ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(Color.FromArgb(23,31,41));}
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using var p=new Pen(Color.FromArgb(75,98,112));e.Graphics.DrawRectangle(p,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){}
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            using var b=new SolidBrush(e.Item.Selected?Color.FromArgb(52,77,70):Color.FromArgb(23,31,41));e.Graphics.FillRectangle(b,new Rectangle(Point.Empty,e.Item.Size));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var bounds=new Rectangle(Point.Empty,e.Item.Size);bounds.Inflate(-6,0);
            TextRenderer.DrawText(e.Graphics,e.Text,e.TextFont,bounds,HarborTheme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);
        }
    }
}

