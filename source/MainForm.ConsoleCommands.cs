using System.Text.RegularExpressions;

namespace MinecraftHarbor;
public sealed partial class MainForm
{
    ConsoleCommandCatalog consoleCommands=new();
    ConsoleCommandHistory? consoleHistory;
    readonly CommandSuggestionPanel commandSuggestions=new();
    readonly System.Windows.Forms.Timer commandDebounce=new(){Interval=250};
    CancellationTokenSource? commandDiscoveryCancellation;
    string commandSession="",commandHelpPath="",commandDiscoveryStatus="";
    readonly HashSet<string> commandHelpPaths=new(StringComparer.Ordinal);
    bool commandSending,commandHistoryMoving,commandPopupDismissed;

    void BuildCommandAssistance()
    {
        var page=pages["Console"];page.Controls.Add(commandSuggestions);
        commandSuggestions.Accepted+=(_,_)=>AcceptCommandSuggestion();
        command.TextChanged+=(_,_)=>{if(!commandHistoryMoving)consoleHistory?.Reset();commandPopupDismissed=false;UpdateCommandSuggestions();commandDebounce.Stop();commandDebounce.Start();};
        command.GotFocus+=(_,_)=>{commandPopupDismissed=false;UpdateCommandSuggestions();};
        command.MouseUp+=(_,_)=>UpdateCommandSuggestions();
        command.LostFocus+=(_,_)=>{if(IsHandleCreated&&!IsDisposed)BeginInvoke(()=>{if(!command.Focused&&!commandSuggestions.ContainsFocus)commandSuggestions.Visible=false;});};
        page.VisibleChanged+=(_,_)=>{if(!page.Visible){commandSuggestions.Visible=false;commandDebounce.Stop();}else RefreshCommandAssistance();};
        page.SizeChanged+=(_,_)=>UpdateCommandSuggestions();
        commandDebounce.Tick+=async(_,_)=>{commandDebounce.Stop();await LoadCommandArgumentHelp();};
        Disposed+=(_,_)=>{commandDebounce.Dispose();var cancellation=commandDiscoveryCancellation;commandDiscoveryCancellation=null;cancellation?.Cancel();cancellation?.Dispose();};
    }
    void RefreshCommandAssistance()
    {
        string session=server.Profile.Id+"|"+server.StartedAt?.Ticks+"|"+(server.State==ServerState.Running);
        if(session==commandSession)return;commandSession=session;commandDiscoveryCancellation?.Cancel();commandDiscoveryCancellation?.Dispose();commandDiscoveryCancellation=new();
        consoleCommands=new();commandHelpPaths.Clear();commandHelpPath="";commandDiscoveryStatus="";
        consoleHistory=new(Path.Combine(server.ProfileRoot,"console-history.json"));
        if(server.State==ServerState.Running&&server.HasProcess){commandDiscoveryStatus="Reading this server’s commands…";_ = LoadRootCommandHelp(session,commandDiscoveryCancellation.Token);}
        UpdateCommandSuggestions();
    }
    async Task LoadRootCommandHelp(string session,CancellationToken token)
    {
        try{var help=await server.CommandHelpAsync("",token);if(token.IsCancellationRequested||IsDisposed||session!=commandSession)return;consoleCommands.Add(help);commandDiscoveryStatus=help.Length==0?"Command suggestions unavailable — you can still enter commands":"";UpdateCommandSuggestions();}
        catch(OperationCanceledException){}
        catch(Exception ex)when(ex is IOException or InvalidOperationException or ObjectDisposedException){if(!IsDisposed&&session==commandSession){commandDiscoveryStatus="Command suggestions unavailable — you can still enter commands";UpdateCommandSuggestions();server.Log("Could not read server commands: "+ex.Message);}}
    }
    async Task LoadCommandArgumentHelp()
    {
        if(commandSending||server.State!=ServerState.Running||!server.HasProcess||commandDiscoveryCancellation==null)return;
        string before=command.Text[..Math.Clamp(command.SelectionStart,0,command.Text.Length)].TrimStart().TrimStart('/');int space=before.LastIndexOf(' ');if(space<1)return;
        string path=before[..space].Trim();string root=path.Split(' ')[0];if(!consoleCommands.Roots.Contains(root,StringComparer.Ordinal)||!Regex.IsMatch(path,@"^[a-zA-Z0-9_:./ -]{1,256}$")||!commandHelpPaths.Add(path))return;
        var session=commandSession;var token=commandDiscoveryCancellation.Token;commandHelpPath=path;
        try{var help=await server.CommandHelpAsync(path,token);if(token.IsCancellationRequested||IsDisposed||session!=commandSession)return;consoleCommands.Add(help);UpdateCommandSuggestions();}
        catch(OperationCanceledException){}
        catch(Exception ex)when(ex is IOException or InvalidOperationException or ObjectDisposedException){if(!IsDisposed&&session==commandSession)server.Log("Command hints unavailable: "+ex.Message);}
    }
    IEnumerable<string> ConsolePlayerNames()
    {
        if(server.OnlinePlayers>0)foreach(string name in server.OnlineNames.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))yield return name;
    }
    void UpdateCommandSuggestions()
    {
        if(IsDisposed||consoleCommandFrame==null)return;
        if(!command.Enabled||!command.Focused||!pages["Console"].Visible||commandPopupDismissed||commandSending){commandSuggestions.Visible=false;return;}
        var choices=consoleCommands.Suggest(command.Text,command.SelectionStart,ConsolePlayerNames());
        string hint=commandDiscoveryStatus.Length>0?commandDiscoveryStatus:consoleCommands.Hint(command.Text);
        commandSuggestions.SetChoices(choices,hint);
        int u(int x)=>pages["Console"].LogicalToDeviceUnits(x);int height=u(50+Math.Min(6,choices.Length)*32);
        commandSuggestions.SetBounds(consoleCommandFrame.Left,consoleCommandFrame.Top-height-u(8),consoleCommandFrame.Width,height);
        commandSuggestions.Visible=choices.Length>0||hint.Length>0;commandSuggestions.BringToFront();
    }
    void AcceptCommandSuggestion()
    {
        var choice=commandSuggestions.Selected;if(choice==null)return;
        string text=command.Text;int end=choice.Start+choice.Length;
        bool appendSpace=end==text.Length;command.Text=text[..choice.Start]+choice.Text+(appendSpace?" ":"")+text[end..];command.SelectionStart=choice.Start+choice.Text.Length+(appendSpace?1:0);command.Focus();UpdateCommandSuggestions();
    }
    void MoveCommandHistory(int direction)
    {
        if(consoleHistory==null)return;commandHistoryMoving=true;
        try{command.Text=consoleHistory.Move(direction,command.Text);command.SelectionStart=command.TextLength;}
        finally{commandHistoryMoving=false;}commandSuggestions.Visible=false;commandPopupDismissed=true;
    }
    async Task SendConsoleDraft()
    {
        if(commandSending||string.IsNullOrWhiteSpace(command.Text)||server.State!=ServerState.Running)return;
        string draft=command.Text;var history=consoleHistory;commandSending=true;commandSuggestions.Visible=false;commandDebounce.Stop();consoleEnter.Enabled=false;command.Enabled=false;
        await Run(async()=>{await server.SendConsoleCommandAsync(draft);try{history?.Record(draft);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){server.Log("Command sent, but its history could not be saved: "+ex.Message);}command.Clear();});
        commandSending=false;RefreshConsoleStatus();
        if(command.Enabled)command.Focus();
    }
}
internal sealed class CommandSuggestionPanel:PaintedPanelBase
{
    CommandChoice[] rows=[];string hint="";int selected,first;
    internal event EventHandler? Accepted;
    internal CommandChoice? Selected=>rows.Length>0?rows[selected]:null;
    internal CommandSuggestionPanel(){Visible=false;Font=new Font("Segoe UI",10);TabStop=false;AccessibleName="Command suggestions";AccessibleRole=AccessibleRole.List;Cursor=Cursors.Hand;}
    internal void SetChoices(CommandChoice[] values,string text)
    {
        if(rows.SequenceEqual(values)&&hint==text)return;string? previous=Selected?.Text;rows=values;hint=text;AccessibleDescription=text;selected=Math.Max(0,Array.FindIndex(rows,r=>r.Text==previous));first=Math.Clamp(first,0,Math.Max(0,rows.Length-6));EnsureSelection();Invalidate();AccessibilityNotifyClients(AccessibleEvents.Reorder,-1);
    }
    internal void MoveSelection(int direction){if(rows.Length==0)return;selected=Math.Clamp(selected+direction,0,rows.Length-1);EnsureSelection();Invalidate();AccessibilityNotifyClients(AccessibleEvents.Selection,selected-first+1);}
    void EnsureSelection(){if(selected<first)first=selected;if(selected>=first+6)first=selected-5;}
    Rectangle RowBounds(int index)=>new(LogicalToDeviceUnits(8),LogicalToDeviceUnits(42+(index-first)*32),Math.Max(1,Width-LogicalToDeviceUnits(16)),LogicalToDeviceUnits(32));
    int Hit(Point point){int row=(point.Y-LogicalToDeviceUnits(42))/Math.Max(1,LogicalToDeviceUnits(32));return point.Y>=LogicalToDeviceUnits(42)&&row>=0&&row<6&&first+row<rows.Length?first+row:-1;}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int index=Hit(e.Location);if(index>=0&&selected!=index){selected=index;Invalidate();}}
    protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);int index=Hit(e.Location);if(index>=0){selected=index;Accepted?.Invoke(this,EventArgs.Empty);}}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);MoveSelection(e.Delta>0?-1:1);}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);using var path=HarborTheme.Round(new RectangleF(1,1,Width-2,Height-2),8);using var fill=new SolidBrush(Color.FromArgb(13,33,47));e.Graphics.FillPath(fill,path);using var pen=new Pen(Color.FromArgb(44,119,153));e.Graphics.DrawPath(pen,path);
        TextRenderer.DrawText(e.Graphics,hint,Font,new Rectangle(LogicalToDeviceUnits(14),LogicalToDeviceUnits(7),Width-LogicalToDeviceUnits(28),LogicalToDeviceUnits(32)),ConsoleSyntax.Info,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        for(int i=first;i<Math.Min(first+6,rows.Length);i++){var bounds=RowBounds(i);if(i==selected){using var highlight=new SolidBrush(Color.FromArgb(30,71,90));using var round=HarborTheme.Round(bounds,5);e.Graphics.FillPath(highlight,round);}bounds.Inflate(-LogicalToDeviceUnits(10),0);TextRenderer.DrawText(e.Graphics,rows[i].Text,Font,bounds,i==selected?ConsoleSyntax.Info:HarborTheme.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);}
    }
    protected override AccessibleObject CreateAccessibilityInstance()=>new SuggestionAccessibility(this);
    sealed class SuggestionAccessibility(CommandSuggestionPanel owner):ControlAccessibleObject(owner)
    {
        public override int GetChildCount()=>Math.Min(6,owner.rows.Length-owner.first);
        public override AccessibleObject? GetChild(int index)=>index>=0&&index<GetChildCount()?new ChoiceAccessibility(owner,this,owner.first+index):null;
    }
    sealed class ChoiceAccessibility(CommandSuggestionPanel owner,AccessibleObject parent,int index):AccessibleObject
    {
        public override string? Name{get=>owner.rows[index].Text;set{}}
        public override string? Description=>owner.rows[index].Hint;
        public override AccessibleRole Role=>AccessibleRole.ListItem;
        public override AccessibleObject? Parent=>parent;
        public override Rectangle Bounds=>owner.RectangleToScreen(owner.RowBounds(index));
        public override AccessibleStates State=>AccessibleStates.Selectable|(index==owner.selected?AccessibleStates.Selected:0);
        public override string DefaultAction=>"Complete command";
        public override void DoDefaultAction(){owner.selected=index;owner.Accepted?.Invoke(owner,EventArgs.Empty);}
    }
}
