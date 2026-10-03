using System.Drawing.Drawing2D;

namespace MinecraftHarbor;
public sealed partial class MainForm
{
    readonly PaintedPanel playerStats=new(),playerTabs=new(),playerTable=new(),playerPager=new(),playerServerBadge=new();
    readonly PaintedPanelBase playerRowHost=new();
    readonly TextBox playerSearch=new(){PlaceholderText="Search players…",AccessibleName="Search players"};
    HarborInputFrame playerSearchFrame=null!;
    HarborButton playerAddButton=null!,playerPrevious=null!,playerNext=null!;
    readonly List<HarborButton> playerTabButtons=new();
    readonly List<HarborButton> playerPageButtons=new();
    readonly Dictionary<string,Bitmap> playerHeads=new();
    readonly HashSet<string> playerHeadRequests=new();
    CancellationTokenSource? playerImageCancellation=new();
    IReadOnlyList<ManagedPlayer> managedPlayers=Array.Empty<ManagedPlayer>();
    ManagedPlayer[] filteredPlayers=[];
    string selectedPlayerTab="Player List",playerRowsSignature="",playerCountsSignature="";
    int playerPage,playersPerPage=6;
    bool playerLoadRunning,playerLoadPending,playerDataLoaded,playerImagesDisabled;
    Label playerLoading=null!;
    Action arrangePlayerPage=()=>{};

    void BuildPlayers()
    {
        var page=pages["Players"];page.AutoScroll=false;
        var title=LabelAt(page,"Players",0,0,900,58,31,Ink,true);var subtitle=LabelAt(page,"Manage players, permissions, and access for your server.",0,0,900,33,13,ConsoleSyntax.Info);
        page.Controls.AddRange(new Control[]{playerStats,playerTabs,playerTable,playerServerBadge});playerStats.Draw=DrawPlayerStats;playerServerBadge.Draw=DrawPlayerServerBadge;playerServerBadge.Cursor=Cursors.Hand;playerServerBadge.AccessibleName="Open server overview";playerServerBadge.Click+=(_,_)=>ShowPage("Home");
        playerTabs.Draw=(g,w,h)=>{using var line=new Pen(Color.FromArgb(48,88,112));g.DrawLine(line,0,h-2,w,h-2);int index=Array.IndexOf(new[]{"Player List","Whitelist","Operators","Bans"},selectedPlayerTab);using var selected=new SolidBrush(ConsoleSyntax.Success);g.FillRectangle(selected,index*112,h-5,108,4);};
        foreach(var text in new[]{"Player List","Whitelist","Operators","Bans"}){var button=(HarborButton)MakeButton(text,108);button.Chrome=true;button.Font=new Font("Segoe UI",9.5f,FontStyle.Regular);button.Click+=(_,_)=>SelectPlayerTab(text);playerTabs.Controls.Add(button);playerTabButtons.Add(button);}
        playerSearchFrame=new(playerSearch);playerTabs.Controls.Add(playerSearchFrame);playerSearch.TextChanged+=(_,_)=>{playerPage=0;RenderPlayerRows();};
        playerAddButton=(HarborButton)MakeButton("Add Player",171,true);playerAddButton.Glyph="plus";playerAddButton.MenuArrow=true;playerTabs.Controls.Add(playerAddButton);playerAddButton.Click+=(_,_)=>ShowAddPlayerMenu();
        playerTable.Draw=(g,w,h)=>{BackupCard(g,w,h);using var bg=new SolidBrush(Color.FromArgb(22,47,65));g.FillRectangle(bg,2,2,w-4,33);var positions=PlayerColumns(w);string[] headers={"Player","Status","Role","Playtime","Last Seen","Actions"};for(int i=0;i<headers.Length;i++)HarborTheme.Text(g,headers[i],13,positions[i],10,Muted,true);};
        playerTable.Controls.Add(playerRowHost);playerTable.Controls.Add(playerPager);playerPager.Draw=DrawPlayerPager;
        playerPrevious=(HarborButton)MakeButton("",31);playerPrevious.Glyph="back";playerPrevious.AccessibleName="Previous player page";playerPrevious.Click+=(_,_)=>{playerPage=Math.Max(0,playerPage-1);RenderPlayerRows();};playerPager.Controls.Add(playerPrevious);
        playerNext=(HarborButton)MakeButton("",31);playerNext.Glyph="next";playerNext.AccessibleName="Next player page";playerNext.Click+=(_,_)=>{playerPage++;RenderPlayerRows();};playerPager.Controls.Add(playerNext);
        for(int i=0;i<5;i++){var button=(HarborButton)MakeButton("",31);button.Font=new Font("Segoe UI",9,FontStyle.Bold);button.Click+=(_,_)=>{if(button.Tag is int index){playerPage=index;RenderPlayerRows();}};playerPageButtons.Add(button);playerPager.Controls.Add(button);}
        playerLoading=LabelAt(playerRowHost,"Loading players…",0,30,600,45,13,Muted);playerLoading.TextAlign=ContentAlignment.MiddleCenter;
        void Arrange(){int u(int x)=>page.LogicalToDeviceUnits(x);int w=page.ClientSize.Width-u(3);title.SetBounds(0,u(12),w-u(220),u(58));subtitle.SetBounds(0,u(71),w,u(33));playerServerBadge.SetBounds(w-u(205),u(12),u(205),u(64));playerStats.SetBounds(0,u(123),w,u(73));playerTabs.SetBounds(0,u(209),w,u(49));
            for(int i=0;i<playerTabButtons.Count;i++)playerTabButtons[i].SetBounds(u(i*112),0,u(108),u(45));int searchWidth=Math.Clamp(w-u(640),u(170),u(264));playerSearchFrame.SetBounds(w-u(183)-searchWidth,u(3),searchWidth,u(40));playerAddButton.SetBounds(w-u(171),u(3),u(171),u(40));
            playerTable.SetBounds(0,u(274),w,Math.Max(u(170),page.ClientSize.Height-u(280)));playerRowHost.SetBounds(u(3),u(35),w-u(6),Math.Max(1,playerTable.Height-u(82)));playerPager.SetBounds(u(2),playerTable.Height-u(45),w-u(4),u(43));playerPrevious.SetBounds(playerPager.Width-u(276),u(5),u(31),u(32));playerNext.SetBounds(playerPager.Width-u(42),u(5),u(31),u(32));for(int i=0;i<playerPageButtons.Count;i++)playerPageButtons[i].SetBounds(playerPager.Width-u(237-i*39),u(5),u(31),u(32));playerLoading.SetBounds(0,u(30),playerRowHost.Width,u(45));int size=Math.Clamp(playerRowHost.Height/u(52),1,6);if(size!=playersPerPage){playersPerPage=size;playerPage=0;}playerRowsSignature="";RenderPlayerRows();
        }
        arrangePlayerPage=Arrange;page.SizeChanged+=(_,_)=>Arrange();Arrange();
        Disposed+=(_,_)=>{var cancel=playerImageCancellation;playerImageCancellation=null;cancel?.Cancel();cancel?.Dispose();foreach(var head in playerHeads.Values)head.Dispose();playerHeads.Clear();};
    }
    void SelectPlayerTab(string text){selectedPlayerTab=text;playerPage=0;playerTabs.Invalidate();RenderPlayerRows();}
    async void RefreshRoster()
    {
        if(playerAddButton==null||IsDisposed)return;if(playerLoadRunning){playerLoadPending=true;return;}playerLoadRunning=true;string key=server.Profile.Id+"|"+server.Config.WorldFolder;
        try{var players=await Task.Run(server.ManagedPlayers);if(IsDisposed||key!=server.Profile.Id+"|"+server.Config.WorldFolder)return;managedPlayers=players;playerDataLoaded=true;playerLoading.Visible=false;RenderPlayerRows();playerStats.Invalidate();playerServerBadge.Invalidate();}
        catch(Exception ex)when(ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or InvalidOperationException){server.Log("Player list could not refresh: "+ex.Message);if(!playerDataLoaded){playerLoading.Text="Could not read the player list. See Console.";playerLoading.Visible=true;}}
        finally{playerLoadRunning=false;if(playerLoadPending&&!IsDisposed){playerLoadPending=false;RefreshRoster();}}
    }
    void RefreshPlayerStatus()
    {
        if(playerAddButton==null)return;playerAddButton.Enabled=!server.Busy&&!server.Profile.Deleted&&(!server.HasProcess||server.State==ServerState.Running);
        string counts=server.State+"|"+server.OnlinePlayers+"|"+server.Config.MaxPlayers+"|"+managedPlayers.Count+"|"+managedPlayers.Count(p=>p.OperatorLevel>0)+"|"+managedPlayers.Count(p=>p.Whitelisted);if(counts!=playerCountsSignature){playerCountsSignature=counts;playerStats.Invalidate();playerServerBadge.Invalidate();}
        foreach(var row in playerRowHost.Controls.OfType<PaintedPanel>()){if(row.Tag is not ManagedPlayer player)continue;var buttons=row.Controls.OfType<HarborButton>().ToArray();bool active=!server.Busy&&server.State==ServerState.Running&&player.Online&&player.Named&&!player.Banned;if(buttons.Length==3){buttons[0].Enabled=player.Banned?!server.Busy&&player.Named&&player.Uuid.Length>0&&(!server.HasProcess||server.State==ServerState.Running):active;buttons[1].Enabled=active;buttons[2].Enabled=!server.Busy;}}
        if(pages["Players"].Visible&&tick%5==0&&!playerLoadRunning)RefreshRoster();
    }
    void DrawPlayerServerBadge(Graphics g,float w,float h)
    {
        BackupCard(g,w,h);var (label,color)=ConsoleState(server.State);using var dot=new SolidBrush(color);g.FillEllipse(dot,15,21,11,11);HarborTheme.Text(g,server.State==ServerState.Running?"Server Online":label,14,43,14,Ink,true,w-67);HarborTheme.Text(g,server.OnlinePlayers+" / "+server.Config.MaxPlayers+" players",12,43,37,Muted,false,w-67);HarborTheme.Icon(g,"next",w-27,23,16,Muted);
    }
    void DrawPlayerStats(Graphics g,float w,float h)
    {
        float width=(w-54)/4;string[] values={server.OnlinePlayers+" / "+server.Config.MaxPlayers,playerDataLoaded?managedPlayers.Count.ToString():"—",playerDataLoaded?managedPlayers.Count(p=>p.OperatorLevel>0).ToString():"—",playerDataLoaded?managedPlayers.Count(p=>p.Whitelisted).ToString():"—"};string[] labels={"Online Players","Total Players","Operators","Whitelisted"};string[] icons={"players","player","shield","players"};
        for(int i=0;i<4;i++){var saved=g.Save();g.TranslateTransform(i*(width+18),0);BackupCard(g,width,h);HarborTheme.Icon(g,icons[i],21,19,35,Color.FromArgb(58,155,250));HarborTheme.Text(g,values[i],25,78,12,Ink,true,width-88);HarborTheme.Text(g,labels[i],14,78,45,Muted,false,width-88);g.Restore(saved);}
    }
    static float[] PlayerColumns(float w)=>new[]{22f,w-715,w-590,w-488,w-385,w-275};
    void RenderPlayerRows()
    {
        if(playerSearchFrame==null)return;filteredPlayers=PlayerDirectory.Filter(managedPlayers,selectedPlayerTab,playerSearch.Text).ToArray();int pagesCount=Math.Max(1,(filteredPlayers.Length+playersPerPage-1)/playersPerPage);playerPage=Math.Clamp(playerPage,0,pagesCount-1);var visible=filteredPlayers.Skip(playerPage*playersPerPage).Take(playersPerPage).ToArray();string signature=playerRowHost.Width+"|"+selectedPlayerTab+"|"+playerSearch.Text+"|"+playerPage+"|"+string.Join("|",visible.Select(p=>p.Uuid+p.Name+p.Status+p.Role+p.Playtime+p.LastSeen+p.Whitelisted));if(signature==playerRowsSignature){RefreshPlayerStatus();return;}playerRowsSignature=signature;
        playerRowHost.SuspendLayout();foreach(var old in playerRowHost.Controls.OfType<PaintedPanel>().ToArray())old.Dispose();foreach(var empty in playerRowHost.Controls.OfType<Label>().Where(l=>l!=playerLoading).ToArray())empty.Dispose();int u(int n)=>playerRowHost.LogicalToDeviceUnits(n);int index=0;
        foreach(var person in visible){var row=new PaintedPanel{Tag=person,AccessibleName=person.Name+" · "+person.Status+" · "+person.Role+" · "+person.Playtime};row.Draw=(g,w,h)=>DrawManagedPlayer(g,w,h,person);playerRowHost.Controls.Add(row);row.SetBounds(u(7),u(index++*52),playerRowHost.Width-u(15),u(52));
            var teleport=(HarborButton)MakeButton(person.Banned?"Unban":"Teleport",119);teleport.Glyph=person.Banned?"restart":"teleport";teleport.AccessibleName=(person.Banned?"Unban ":"Teleport ")+person.Name;teleport.Click+=async(_,_)=>{if(person.Banned)await ApplyPlayerAction(person,PlayerAction.Unban);else ShowTeleportDialog(person);};row.Controls.Add(teleport);
            var kick=(HarborButton)MakeButton("Kick",88);kick.Glyph="kick";kick.AccessibleName="Kick "+person.Name;kick.Click+=async(_,_)=>await ApplyPlayerAction(person,PlayerAction.Kick);row.Controls.Add(kick);
            var more=(HarborButton)MakeButton("…",26);more.Chrome=true;more.AccessibleName="More actions for "+person.Name;more.Click+=(_,_)=>ShowPlayerMenu(more,person);row.Controls.Add(more);
            int x=row.Width-u(254);int[] widths={119,88,26};int b=0;foreach(var button in row.Controls.OfType<HarborButton>()){button.Font=new Font("Segoe UI",9,FontStyle.Bold);button.SetBounds(x,u(8),u(widths[b]),u(36));x+=u(widths[b++]+7);}LoadPlayerHead(person,row);
        }
        if(playerDataLoaded&&visible.Length==0){var empty=LabelAt(playerRowHost,string.IsNullOrWhiteSpace(playerSearch.Text)?"No players in "+selectedPlayerTab.ToLowerInvariant():"No matching players",0,30,playerRowHost.Width,45,13,Muted);empty.TextAlign=ContentAlignment.MiddleCenter;}
        playerPrevious.Enabled=playerPage>0;playerNext.Enabled=playerPage+1<pagesCount;int firstPage=Math.Clamp(playerPage-2,0,Math.Max(0,pagesCount-5));for(int i=0;i<playerPageButtons.Count;i++){var button=playerPageButtons[i];int pageIndex=firstPage+i;button.Visible=pageIndex<pagesCount;button.Tag=pageIndex;button.Text=(pageIndex+1).ToString();button.AccessibleName="Player page "+button.Text;button.Primary=pageIndex==playerPage;button.Invalidate();}playerPager.Invalidate();playerRowHost.ResumeLayout(true);RefreshPlayerStatus();
    }
    void DrawManagedPlayer(Graphics g,float w,float h,ManagedPlayer player)
    {
        using var fill=new SolidBrush(BackupViewport.Surface);g.FillRectangle(fill,0,0,w,h);using var line=new Pen(Color.FromArgb(31,66,86));g.DrawLine(line,12,h-1,w-3,h-1);var columns=PlayerColumns(w+21);for(int i=0;i<columns.Length;i++)columns[i]-=10;
        if(preferences.PlayerHeads&&playerHeads.TryGetValue(player.Uuid,out var head)){g.InterpolationMode=InterpolationMode.NearestNeighbor;g.DrawImage(head,new RectangleF(16,10,32,32));}else HarborTheme.Icon(g,"player",17,11,30,Color.FromArgb(58,155,250));
        HarborTheme.Text(g,player.Name,14,63,7,Ink,true,Math.Max(30,columns[1]-70));HarborTheme.Text(g,player.Uuid.Length>0?player.Uuid[..Math.Min(18,player.Uuid.Length)]+"…":"Identity pending",11,63,29,Muted,false,Math.Max(30,columns[1]-70));
        var color=player.Banned?Color.FromArgb(255,81,101):player.Online?ConsoleSyntax.Success:Muted;using var dot=new SolidBrush(color);g.FillEllipse(dot,columns[1],21,10,10);HarborTheme.Text(g,player.Status,13,columns[1]+18,17,color,false,99);
        var roleColor=player.OperatorLevel>0?Color.FromArgb(255,101,119):ConsoleSyntax.Info;var badge=new RectangleF(columns[2],13,player.OperatorLevel>0?77:63,27);using var path=HarborTheme.Round(badge,5);using var tint=new SolidBrush(Color.FromArgb(28,roleColor));g.FillPath(tint,path);using var edge=new Pen(Color.FromArgb(130,roleColor));g.DrawPath(edge,path);using var font=new Font("Segoe UI",12,FontStyle.Bold,GraphicsUnit.Pixel);using var brush=new SolidBrush(roleColor);using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};g.DrawString(player.Role,font,brush,badge,format);
        HarborTheme.Text(g,player.Playtime,13,columns[3],17,Muted,false,98);HarborTheme.Text(g,player.LastSeen,12,columns[4],18,Muted,false,104);
    }
    void DrawPlayerPager(Graphics g,float w,float h)
    {
        using var fill=new SolidBrush(BackupViewport.Surface);g.FillRectangle(fill,0,0,w,h);using var line=new Pen(Color.FromArgb(34,71,95));g.DrawLine(line,0,0,w,0);int first=filteredPlayers.Length==0?0:playerPage*playersPerPage+1,last=Math.Min(filteredPlayers.Length,(playerPage+1)*playersPerPage);HarborTheme.Text(g,"Showing "+first+"–"+last+" of "+filteredPlayers.Length+" players",12,15,14,Muted,false,w-300);
    }
    async void LoadPlayerHead(ManagedPlayer player,Control row)
    {
        if(!preferences.PlayerHeads||playerImagesDisabled||player.Uuid.Length==0||playerImageCancellation==null||!playerHeadRequests.Add(player.Uuid))return;var token=playerImageCancellation.Token;Bitmap? image=null;
        try{image=await PlayerAvatars.Load(server.Root,player.Uuid,token);if(token.IsCancellationRequested||IsDisposed||image==null||!preferences.PlayerHeads)return;playerHeads[player.Uuid]=image;image=null;foreach(var panel in playerRowHost.Controls.OfType<PaintedPanel>().Where(r=>r.Tag is ManagedPlayer p&&p.Uuid==player.Uuid))panel.Invalidate();}
        catch(OperationCanceledException){}catch(Exception ex)when(ex is HttpRequestException or IOException or ArgumentException or UnauthorizedAccessException){server.Log("Player image unavailable for "+player.Name+".");}finally{image?.Dispose();}
    }
    ContextMenuStrip PlayerMenu()=>new(){BackColor=Color.FromArgb(20,34,46),ForeColor=Ink,ShowImageMargin=false};
    void ShowAddPlayerMenu(){var menu=PlayerMenu();menu.Items.Add("Add to whitelist",null,(_,_)=>ShowAddPlayerDialog(PlayerAction.Whitelist));menu.Items.Add("Add operator",null,(_,_)=>ShowAddPlayerDialog(PlayerAction.Operator));menu.Items.Add("Ban player",null,(_,_)=>ShowAddPlayerDialog(PlayerAction.Ban));menu.Closed+=(_,_)=>BeginInvoke((Action)(()=>menu.Dispose()));menu.Show(playerAddButton,new Point(0,playerAddButton.Height));}
    void ShowPlayerMenu(Control anchor,ManagedPlayer person)
    {
        var menu=PlayerMenu();bool owner=person.Uuid.Equals(server.Config.OwnerUuid,StringComparison.OrdinalIgnoreCase);void Add(string text,PlayerAction action,bool enabled=true){var item=menu.Items.Add(text,null,async(_,_)=>await ApplyPlayerAction(person,action));item.Enabled=enabled&&person.Named&&person.Uuid.Length>0;}
        Add(person.Whitelisted?"Remove from whitelist":"Add to whitelist",person.Whitelisted?PlayerAction.RemoveWhitelist:PlayerAction.Whitelist,!owner||!person.Whitelisted);Add(person.OperatorLevel>0?"Remove operator":"Make operator",person.OperatorLevel>0?PlayerAction.RemoveOperator:PlayerAction.Operator,!owner||person.OperatorLevel==0);Add(person.Banned?"Unban player":"Ban player",person.Banned?PlayerAction.Unban:PlayerAction.Ban,!owner||person.Banned);menu.Items.Add(new ToolStripSeparator());var copy=menu.Items.Add("Copy UUID",null,(_,_)=>Clipboard.SetText(person.Uuid));copy.Enabled=person.Uuid.Length>0;menu.Closed+=(_,_)=>BeginInvoke((Action)(()=>menu.Dispose()));menu.Show(anchor,new Point(0,anchor.Height));
    }
    Form PlayerDialog(string title,int height=225)=>new(){Text=title,ClientSize=new Size(455,height),BackColor=Bg,ForeColor=Ink,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false};
    async void ShowAddPlayerDialog(PlayerAction action)
    {
        using var dialog=PlayerDialog(action==PlayerAction.Ban?"Ban player":action==PlayerAction.Operator?"Add operator":"Add to whitelist");LabelAt(dialog,"Minecraft username",22,18,411,28,12,Ink,true);var username=new TextBox{MaxLength=16,AccessibleName="Minecraft username"};var frame=new HarborInputFrame(username);frame.SetBounds(22,53,411,42);dialog.Controls.Add(frame);LabelAt(dialog,action==PlayerAction.Operator?"Operators can run administrative Minecraft commands.":action==PlayerAction.Ban?"This player will not be able to join your server.":"Allow this Minecraft account to join your server.",22,109,411,36,10,Muted);
        var cancel=MakeButton("Cancel",110);cancel.SetBounds(190,164,110,42);cancel.DialogResult=DialogResult.Cancel;var add=MakeButton(action==PlayerAction.Ban?"Ban Player":"Add Player",123,true);add.SetBounds(310,164,123,42);add.DialogResult=DialogResult.OK;dialog.Controls.AddRange(new Control[]{cancel,add});dialog.AcceptButton=add;dialog.CancelButton=cancel;if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        string profile=server.Profile.Id;await Run(async()=>{var player=await server.ResolveManagedPlayer(username.Text.Trim());if(profile!=server.Profile.Id)throw new InvalidOperationException("The selected server changed. Open Add Player again.");await server.PlayerActionAsync(action,player);});RefreshRoster();
    }
    async Task ApplyPlayerAction(ManagedPlayer person,PlayerAction action)
    {
        if(server.Busy)return;bool confirm=action is PlayerAction.Ban or PlayerAction.Kick or PlayerAction.Operator or PlayerAction.RemoveOperator or PlayerAction.RemoveWhitelist;
        if(confirm&&MessageBox.Show(this,(action switch{PlayerAction.Ban=>"Ban ",PlayerAction.Kick=>"Kick ",PlayerAction.Operator=>"Give operator permissions to ",PlayerAction.RemoveOperator=>"Remove operator permissions from ",_=>"Remove whitelist access for "})+person.Name+"?","Player action",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        await Run(()=>server.PlayerActionAsync(action,new(person.Uuid,person.Name)));RefreshRoster();
    }
    void ShowTeleportDialog(ManagedPlayer person)
    {
        var destinations=managedPlayers.Where(p=>p.Online&&!p.Banned&&p.Named&&!p.Name.Equals(person.Name,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Name).ToArray();if(destinations.Length==0){MessageBox.Show(this,"Another player needs to be online to use this teleport action.","Teleport player");return;}
        using var dialog=PlayerDialog("Teleport "+person.Name);LabelAt(dialog,"Teleport to another online player",22,20,411,28,12,Ink,true);var target=new HarborDropdown();target.Items.AddRange(destinations.Cast<object>().ToArray());target.SelectedIndex=0;target.SetBounds(22,65,411,42);dialog.Controls.Add(target);var cancel=MakeButton("Cancel",110);cancel.SetBounds(190,164,110,42);cancel.DialogResult=DialogResult.Cancel;var send=MakeButton("Teleport",123,true);send.SetBounds(310,164,123,42);send.DialogResult=DialogResult.OK;dialog.Controls.AddRange(new Control[]{cancel,send});dialog.AcceptButton=send;dialog.CancelButton=cancel;if(dialog.ShowDialog(this)==DialogResult.OK){string destination=target.SelectedItem?.ToString()??"";_ = Run(async()=>{await server.TeleportPlayerAsync(person.Name,destination);RefreshRoster();});}
    }
}


