using System.Drawing.Drawing2D;
using System.Diagnostics;

namespace MinecraftHarbor;
public sealed partial class MainForm
{
    readonly BackupCatalog backupCatalog=new();
    IReadOnlyList<BackupEntry> backupEntries=Array.Empty<BackupEntry>();
    readonly PaintedPanel backupBanner=new(),backupTabs=new(),backupTable=new(),backupTotals=new(),backupSchedule=new();
    readonly BackupViewport backupRows=new();
    readonly TextBox backupSearch=new(){PlaceholderText="Search backups…",AccessibleName="Search backups"};
    HarborInputFrame backupSearchFrame=null!;
    HarborButton createBackupButton=null!,backupListTab=null!,backupAutomaticTab=null!;
    readonly HarborToggle backupAutomaticEnabled=new(){AccessibleName="Enable hourly backups"};
    readonly List<(HarborButton Button,BackupEntry Entry)> backupRestoreButtons=new();
    bool showingBackupSchedule,refreshingBackupPreferences;
    string backupListSignature="",backupIdentity="",backupScheduleSignature="";
    Bitmap? backupArtwork;
    CancellationTokenSource? backupArtworkCancellation;
    Action arrangeBackupPage=()=>{};

    void BuildBackups()
    {
        var page=pages["Backups"];page.AutoScroll=false;
        var title=LabelAt(page,"Backups",0,0,900,58,31,Ink,true);
        var subtitle=LabelAt(page,"Manage, restore, and create backups for your server.",0,0,900,33,13,ConsoleSyntax.Info);
        page.Controls.AddRange(new Control[]{backupBanner,backupTabs,backupTable,backupTotals,backupSchedule});
        backupBanner.Draw=DrawBackupBanner;
        createBackupButton=(HarborButton)MakeButton("Create New Backup",220,true);createBackupButton.Glyph="plus";backupBanner.Controls.Add(createBackupButton);createBackupButton.Click+=async(_,_)=>await BackupClick();
        backupTabs.Draw=(g,w,h)=>{using var line=new Pen(Color.FromArgb(48,88,112));g.DrawLine(line,0,h-2,w,h-2);using var selected=new SolidBrush(ConsoleSyntax.Success);float x=showingBackupSchedule?115:0;g.FillRectangle(selected,x,h-5,showingBackupSchedule?168:111,4);};
        backupListTab=(HarborButton)MakeButton("Backups",111);backupListTab.Chrome=true;backupListTab.Click+=(_,_)=>SelectBackupTab(false);
        backupAutomaticTab=(HarborButton)MakeButton("Automatic Backups",168);backupAutomaticTab.Chrome=true;backupAutomaticTab.Click+=(_,_)=>SelectBackupTab(true);
        backupTabs.Controls.AddRange(new Control[]{backupListTab,backupAutomaticTab});
        backupSearchFrame=new(backupSearch);backupTabs.Controls.Add(backupSearchFrame);backupSearch.TextChanged+=(_,_)=>RenderBackupRows();
        backupTable.Draw=(g,w,h)=>{BackupCard(g,w,h);var columns=BackupColumns(w);using var fill=new SolidBrush(Color.FromArgb(22,47,65));g.FillRectangle(fill,2,2,w-4,38);string[] headings={"Name","Date & Time","Size","Type","Actions"};for(int i=0;i<headings.Length;i++)HarborTheme.Text(g,headings[i],13,columns[i],12,Muted,true);};
        backupTable.Controls.Add(backupRows);
        backupTotals.Draw=DrawBackupTotals;
        backupSchedule.Draw=DrawBackupSchedule;backupSchedule.Controls.Add(backupAutomaticEnabled);
        backupAutomaticEnabled.CheckedChanged+=(_,_)=>{
            if(refreshingBackupPreferences)return;
            try{server.SetAutomaticBackups(backupAutomaticEnabled.Checked);RefreshBackupStatus();}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"Automatic backups");RefreshBackupStatus();}
        };
        void Arrange(){int u(int x)=>page.LogicalToDeviceUnits(x);int w=page.ClientSize.Width-u(3),bottom=page.ClientSize.Height-u(52);
            title.SetBounds(0,u(12),w,u(58));subtitle.SetBounds(0,u(71),w,u(33));backupBanner.SetBounds(0,u(123),w,u(78));createBackupButton.SetBounds(w-u(235),u(17),u(220),u(44));
            backupTabs.SetBounds(0,u(214),w,u(51));backupListTab.SetBounds(0,0,u(111),u(47));backupAutomaticTab.SetBounds(u(115),0,u(168),u(47));backupSearchFrame.SetBounds(w-u(300),u(3),u(300),u(40));
            backupTable.SetBounds(0,u(281),w,Math.Max(u(150),bottom-u(296)));backupRows.SetBounds(u(3),u(42),w-u(6),backupTable.Height-u(47));
            backupTotals.SetBounds(0,bottom,w,u(48));backupSchedule.SetBounds(0,u(281),w,Math.Max(u(150),bottom-u(296)));backupAutomaticEnabled.SetBounds(w-u(185),u(22),u(166),u(36));ArrangeBackupRows();
        }
        arrangeBackupPage=Arrange;page.SizeChanged+=(_,_)=>Arrange();backupRows.SizeChanged+=(_,_)=>ArrangeBackupRows();Arrange();SelectBackupTab(false);
        Disposed+=(_,_)=>{var cancellation=backupArtworkCancellation;backupArtworkCancellation=null;cancellation?.Cancel();cancellation?.Dispose();backupArtwork?.Dispose();backupArtwork=null;};
    }
    static void BackupCard(Graphics g,float w,float h)
    {
        var r=new RectangleF(1,1,Math.Max(1,w-2),Math.Max(1,h-2));using var path=HarborTheme.Round(r,9);using var fill=new LinearGradientBrush(r,Color.FromArgb(18,41,58),Color.FromArgb(10,29,43),LinearGradientMode.Horizontal);g.FillPath(fill,path);using var edge=new Pen(Color.FromArgb(44,119,153));g.DrawPath(edge,path);
    }
    static float[] BackupColumns(float w)=>new[]{22f,w-672,w-525,w-446,w-353};
    void DrawBackupBanner(Graphics g,float w,float h)
    {
        BackupCard(g,w,h);var target=new RectangleF(16,10,94,h-20);
        if(backupArtwork!=null){float scale=Math.Min(target.Width/backupArtwork.Width,target.Height/backupArtwork.Height);float iw=backupArtwork.Width*scale,ih=backupArtwork.Height*scale;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(backupArtwork,new RectangleF(target.X+(target.Width-iw)/2,target.Y+(target.Height-ih)/2,iw,ih));}
        else HarborTheme.Icon(g,"cube",43,(h-34)/2,34,Muted);
        HarborTheme.Text(g,server.Profile.Deleted?"No server selected":server.Profile.DisplayName,19,127,14,Ink,true,Math.Max(1,w-377));
        HarborTheme.Text(g,server.Profile.Name+" · "+server.Profile.MinecraftVersion,13,127,43,Muted,false,Math.Max(1,w-377));
        backupBanner.AccessibleName=server.Profile.DisplayName+" · "+server.Profile.Name+" · "+server.Profile.MinecraftVersion;
    }
    void SelectBackupTab(bool automatic)
    {
        showingBackupSchedule=automatic;backupTable.Visible=!automatic;backupSchedule.Visible=automatic;backupSearchFrame.Visible=!automatic;
        backupListTab.ForeColor=automatic?Muted:Ink;backupAutomaticTab.ForeColor=automatic?Ink:Muted;backupTabs.Invalidate();RefreshBackupStatus();
    }
    async Task BackupClick(){await Run(server.BackupAsync);RefreshBackups();}
    void RefreshBackups()
    {
        if(createBackupButton==null)return;
        backupEntries=backupCatalog.Read(server.BackupDir);backupListSignature="";RenderBackupRows();backupTotals.Invalidate();RefreshBackupStatus();RefreshBackupArtwork();backupBanner.Invalidate();
    }
    void RefreshBackupStatus()
    {
        if(createBackupButton==null)return;
        createBackupButton.Enabled=!server.Profile.Deleted&&!server.Busy&&(!server.HasProcess||server.State==ServerState.Running)&&File.Exists(Path.Combine(server.WorldDir,"level.dat"));
        foreach(var item in backupRestoreButtons)item.Button.Enabled=item.Entry.Restorable&&!server.Busy&&!server.HasProcess&&!server.Profile.Deleted;
        refreshingBackupPreferences=true;backupAutomaticEnabled.Checked=server.Config.AutomaticBackups;backupAutomaticEnabled.Enabled=!server.Busy&&!server.Profile.Deleted;refreshingBackupPreferences=false;
        string signature=server.Profile.Id+"|"+server.Config.AutomaticBackups+"|"+server.State+"|"+server.NextAutomaticBackupUtc;
        if(signature!=backupScheduleSignature){backupScheduleSignature=signature;backupSchedule.Invalidate();}
        if(pages["Backups"].Visible&&tick%10==0){var signatureNow=string.Join("|",new DirectoryInfo(server.BackupDir).GetFiles("*.zip").Select(f=>f.Name+":"+f.Length+":"+f.LastWriteTimeUtc.Ticks));if(signatureNow!=backupDiskSignature){backupDiskSignature=signatureNow;RefreshBackups();}}
    }
    string backupDiskSignature="";
    void RenderBackupRows()
    {
        if(backupSearchFrame==null)return;
        var visible=BackupCatalog.Search(backupEntries,backupSearch.Text).ToArray();string signature=server.BackupDir+"|"+backupSearch.Text+"|"+string.Join("|",visible.Select(e=>e.File+e.Name+e.Description+e.Bytes));if(signature==backupListSignature)return;backupListSignature=signature;
        backupRows.SuspendLayout();backupRows.AutoScrollPosition=Point.Empty;
        foreach(var old in backupRows.Controls.Cast<Control>().Where(c=>c is PaintedPanel or Label).ToArray())old.Dispose();backupRestoreButtons.Clear();
        foreach(var entry in visible){var row=new PaintedPanel{Tag=entry,AccessibleName=entry.Name+" · "+entry.Kind+" · "+entry.Size};row.Draw=(g,w,h)=>DrawBackupRow(g,w,h,entry);backupRows.Controls.Add(row);
            var restore=(HarborButton)MakeButton("Restore",110);restore.Glyph="restart";restore.OutlineAccent=true;restore.AccessibleName="Restore "+entry.Name;restore.Click+=async(_,_)=>await RestoreBackup(entry);row.Controls.Add(restore);backupRestoreButtons.Add((restore,entry));
            var download=(HarborButton)MakeButton("Download",122);download.Glyph="download";download.AccessibleName="Download "+entry.Name;download.Click+=async(_,_)=>await ExportBackup(entry);row.Controls.Add(download);
            var delete=(HarborButton)MakeButton("",40);delete.Glyph="trash";delete.Danger=true;delete.QuietDanger=true;delete.AccessibleName=entry.Protected?"Protected original backup":"Delete "+entry.Name;delete.Enabled=!entry.Protected;delete.Click+=(_,_)=>DeleteBackup(entry);row.Controls.Add(delete);
            var more=(HarborButton)MakeButton("…",30);more.Chrome=true;more.AccessibleName="More actions for "+entry.Name;more.Click+=(_,_)=>ShowBackupMenu(more,entry);row.Controls.Add(more);
            foreach(var button in row.Controls.OfType<HarborButton>())button.Font=new Font("Segoe UI",9,FontStyle.Bold);
        }
        if(visible.Length==0){var empty=LabelAt(backupRows,backupEntries.Count==0?"No backups yet":"No matching backups",0,45,600,48,14,Muted);empty.TextAlign=ContentAlignment.MiddleCenter;empty.Tag="empty";}
        ArrangeBackupRows();backupRows.ResumeLayout(true);RefreshBackupStatus();
    }
    void ArrangeBackupRows()
    {
        int u(int n)=>backupRows.LogicalToDeviceUnits(n);int rowWidth=Math.Max(1,backupRows.Width-u(23)),index=0;
        foreach(var row in backupRows.Controls.OfType<PaintedPanel>()){
            row.SetBounds(u(7),u(index++*64)+backupRows.AutoScrollPosition.Y,rowWidth,u(64));int x=rowWidth-u(330);var buttons=row.Controls.OfType<HarborButton>().ToArray();int[] widths={110,122,40,30};for(int i=0;i<buttons.Length;i++){buttons[i].SetBounds(x,u(13),u(widths[i]),u(38));x+=u(widths[i]+7);}
        }
        foreach(var empty in backupRows.Controls.OfType<Label>())empty.Width=rowWidth;
        backupRows.AutoScrollMinSize=new(0,u(index*64));
    }
    void DrawBackupRow(Graphics g,float w,float h,BackupEntry entry)
    {
        // Row columns compensate for the table inset and scrollbar gutter.
        using var surface=new SolidBrush(BackupViewport.Surface);g.FillRectangle(surface,0,0,w,h);var cols=BackupColumns(w+30);for(int i=0;i<cols.Length;i++)cols[i]-=10;
        using var divider=new Pen(Color.FromArgb(30,66,86));g.DrawLine(divider,12,h-1,w-3,h-1);HarborTheme.Icon(g,"cube",17,19,27,Ink);
        float nameWidth=Math.Max(45,cols[1]-70);HarborTheme.Text(g,entry.Name,15,63,10,Ink,true,nameWidth);HarborTheme.Text(g,entry.Description,12,63,34,Muted,false,nameWidth);
        var date=entry.CreatedUtc.ToLocalTime();HarborTheme.Text(g,date.ToString("MMM d, yyyy"),13,cols[1],12,Ink,false,139);HarborTheme.Text(g,date.ToString("h:mm tt"),12,cols[1],34,Muted,false,139);HarborTheme.Text(g,entry.Size,14,cols[2],22,Ink,false,75);
        var color=entry.Kind=="Automatic"?Color.FromArgb(177,146,255):entry.Protected?Color.FromArgb(124,221,178):ConsoleSyntax.Info;
        float badgeWidth=entry.Kind=="Automatic"?81:entry.Protected?70:64;var badge=new RectangleF(cols[3],19,badgeWidth,26);using var path=HarborTheme.Round(badge,5);using var fill=new SolidBrush(Color.FromArgb(35,color));g.FillPath(fill,path);using var border=new Pen(Color.FromArgb(125,color));g.DrawPath(border,path);
        using var font=new Font("Segoe UI",11,FontStyle.Bold,GraphicsUnit.Pixel);using var brush=new SolidBrush(color);using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};g.DrawString(entry.Kind,font,brush,badge,format);
    }
    void DrawBackupTotals(Graphics g,float w,float h)
    {
        BackupCard(g,w,h);HarborTheme.Icon(g,"info",19,13,22,Muted);HarborTheme.Text(g,"World saves, including mod data stored in the world.",12,52,17,Muted,false,Math.Max(1,w-354));
        HarborTheme.Text(g,"Total Backups: "+backupEntries.Count+"  |  Total Size: "+BackupCatalog.FormatSize(backupEntries.Sum(e=>e.Bytes)),12,w-307,17,Muted,false,289,true);
    }
    void DrawBackupSchedule(Graphics g,float w,float h)
    {
        BackupCard(g,w,h);HarborTheme.Text(g,"Hourly Backups",22,25,23,Ink,true,w-225);HarborTheme.Text(g,"The server stays online while its saved world is backed up.",14,25,63,Muted,false,w-50);
        var rows=new[]{("Schedule","Every hour"),("History","Last five hours"),("Original save","Protected and kept permanently"),("Next backup",!server.Config.AutomaticBackups?"Automatic backups are off":server.NextAutomaticBackupUtc.HasValue?server.NextAutomaticBackupUtc.Value.ToLocalTime().ToString("h:mm tt"):"Scheduled when the server starts")};
        float rowHeight=Math.Min(47,(h-94)/4);for(int i=0;i<rows.Length;i++){float y=92+i*rowHeight;using var line=new Pen(Color.FromArgb(36,70,89));g.DrawLine(line,25,y-5,w-25,y-5);HarborTheme.Text(g,rows[i].Item1,13,25,y,Ink,true,w*.4f);HarborTheme.Text(g,rows[i].Item2,13,w*.43f,y,Muted,false,w*.54f-25);}
    }
    async Task RestoreBackup(BackupEntry entry)
    {
        if(server.HasProcess||server.Busy)return;
        if(MessageBox.Show(this,"Restore “"+entry.Name+"”?\n\nThis replaces the current world's progress. Its current save will be retained first. The server stays stopped.","Restore backup",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        await Run(()=>server.RestoreAsync(entry.File));RefreshBackups();
    }
    async Task ExportBackup(BackupEntry entry)
    {
        using var dialog=new SaveFileDialog{Title="Download backup",Filter="World backup (*.zip)|*.zip",FileName=Path.GetFileName(entry.File),OverwritePrompt=true};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        await Run(()=>Task.Run(()=>BackupCatalog.Export(server.BackupDir,entry,dialog.FileName)));
    }
    void DeleteBackup(BackupEntry entry)
    {
        if(entry.Protected||server.Busy)return;
        if(MessageBox.Show(this,"Delete “"+entry.Name+"”?\n\nThis backup will be permanently removed.","Delete backup",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        try{backupCatalog.Delete(server.BackupDir,entry);RefreshBackups();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Delete backup");}
    }
    void ShowBackupMenu(Control anchor,BackupEntry entry)
    {
        var menu=new ContextMenuStrip{BackColor=Color.FromArgb(20,34,46),ForeColor=Ink,ShowImageMargin=false};menu.Items.Add("Edit name and note",null,(_,_)=>EditBackupLabel(entry));menu.Items.Add("Show backup file",null,(_,_)=>Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\""+entry.File+"\""){UseShellExecute=true}));menu.Closed+=(_,_)=>menu.Dispose();menu.Show(anchor,new Point(0,anchor.Height));
    }
    void EditBackupLabel(BackupEntry entry)
    {
        using var dialog=new Form{Text="Backup details",ClientSize=new Size(480,246),BackColor=Bg,ForeColor=Ink,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false};
        LabelAt(dialog,"Name",22,18,430,25,11);var name=new TextBox{Text=entry.Name,MaxLength=100,AccessibleName="Backup name"};TextStyle(name);name.SetBounds(22,48,435,30);dialog.Controls.Add(name);
        LabelAt(dialog,"Note",22,93,430,25,11);var note=new TextBox{Text=entry.Description,MaxLength=240,AccessibleName="Backup note"};TextStyle(note);note.SetBounds(22,121,435,30);dialog.Controls.Add(note);
        var cancel=MakeButton("Cancel",100);cancel.SetBounds(230,184,100,42);cancel.DialogResult=DialogResult.Cancel;dialog.Controls.Add(cancel);var save=MakeButton("Save Changes",118,true);save.SetBounds(340,184,118,42);dialog.Controls.Add(save);dialog.CancelButton=cancel;dialog.AcceptButton=save;
        save.Click+=(_,_)=>{try{backupCatalog.Rename(server.BackupDir,entry,name.Text,note.Text);dialog.DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"Backup details");}};
        if(dialog.ShowDialog(this)==DialogResult.OK)RefreshBackups();
    }
    void RefreshBackupArtwork()
    {
        string key=server.Profile.Id;if(backupIdentity==key)return;backupIdentity=key;backupArtworkCancellation?.Cancel();backupArtworkCancellation?.Dispose();backupArtworkCancellation=new();backupArtwork?.Dispose();backupArtwork=null;var token=backupArtworkCancellation.Token;var profile=server.Profile;
        async Task Load(){Bitmap? image=null;try{image=await PackArtwork.Load(server.Root,profile,token);if(token.IsCancellationRequested||IsDisposed||backupIdentity!=key)return;backupArtwork=image;image=null;backupBanner.Invalidate();}catch(OperationCanceledException){}catch(Exception ex)when(ex is IOException or HttpRequestException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException){server.Log("Backup artwork unavailable: "+ex.Message);}finally{image?.Dispose();}}
        _=Load();
    }
}
