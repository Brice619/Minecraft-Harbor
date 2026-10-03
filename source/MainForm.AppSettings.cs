using System.Drawing.Drawing2D;

namespace MinecraftHarbor;
public sealed partial class MainForm
{
    AppPreferences preferences=new(),settingsDraft=new();
    string settingsCategory="Appearance",settingsBackground="night";
    bool applicationShown;
    readonly PaintedPanel settingsTabs=new(),settingsCard=new(),settingsTools=new();
    readonly List<HarborButton> settingsTabButtons=new();
    HarborButton appSave=null!,appCancel=null!,appReset=null!;
    ServerState notifiedState;
    string? notifiedBackup,notifiedProfile;
    void BuildSettings()
    {
        var page=pages["Settings"];page.AutoScroll=false;
        var title=LabelAt(page,"Settings",0,0,900,58,31,Ink,true);var subtitle=LabelAt(page,"Make Minecraft Harbor work the way you like.",0,0,900,33,13,ConsoleSyntax.Info);
        page.Controls.AddRange(new Control[]{settingsTabs,settingsCard,settingsTools});
        settingsTabs.Draw=(g,w,h)=>{using var line=new Pen(Color.FromArgb(48,88,112));g.DrawLine(line,0,h-2,w,h-2);int index=Array.IndexOf(new[]{"Appearance","Behavior","Console","Notifications","Tools"},settingsCategory);using var fill=new SolidBrush(ConsoleSyntax.Success);g.FillRectangle(fill,index*145,h-5,137,4);};
        foreach(string name in new[]{"Appearance","Behavior","Console","Notifications","Tools"}){var button=(HarborButton)MakeButton(name,137);button.Chrome=true;button.Font=new Font("Segoe UI",10);button.Click+=(_,_)=>{page.AutoScrollPosition=Point.Empty;settingsCategory=name;RenderAppSettings();};settingsTabs.Controls.Add(button);settingsTabButtons.Add(button);}
        settingsCard.Draw=BackupCard;settingsTools.Draw=BackupCard;
        appReset=(HarborButton)MakeButton("Reset Defaults",155);appReset.Click+=(_,_)=>{settingsDraft=new();settingsBackground="night";RenderAppSettings();};
        appCancel=(HarborButton)MakeButton("Cancel",125);appCancel.Click+=(_,_)=>LoadAppSettingsDraft();
        appSave=(HarborButton)MakeButton("Save Changes",165,true);appSave.Click+=async(_,_)=>await Run(()=>{settingsDraft.LastPage=preferences.LastPage;settingsDraft.Save(server.Root);if(appearance.Background!=settingsBackground){ServerManager.WriteJson(Path.Combine(server.Root,"appearance.json"),new AppearanceSettings{Background=settingsBackground});appearance.Background=settingsBackground;RefreshArtwork();}preferences=settingsDraft.Copy();ApplyAppPreferences();server.Log("App settings saved.");tips.Show("App settings saved",appSave,0,-28,1600);return Task.CompletedTask;});
        page.Controls.AddRange(new Control[]{appReset,appCancel,appSave});
        var logs=(HarborButton)MakeButton("Open Logs",155);logs.Glyph="folder";logs.Click+=(_,_)=>OpenPath(Path.Combine(server.Root,"logs"));
        var data=(HarborButton)MakeButton("App Folder",155);data.Glyph="folder";data.Click+=(_,_)=>OpenPath(server.Root);
        var edit=(HarborButton)MakeButton("Server Settings",190);edit.Glyph="settings";edit.Click+=(_,_)=>{ShowPage("Server Management");OpenServerEditor(server.Profile);};settingsTools.Controls.AddRange(new Control[]{logs,data,edit});
        void Arrange(){int u(int n)=>page.LogicalToDeviceUnits(n);int w=page.ClientSize.Width-u(3);title.SetBounds(0,u(12),w,u(58));subtitle.SetBounds(0,u(71),w,u(33));settingsTabs.SetBounds(0,u(123),w,u(46));for(int i=0;i<settingsTabButtons.Count;i++)settingsTabButtons[i].SetBounds(u(i*145),0,u(137),u(43));settingsCard.Width=w;settingsTools.Width=w;RenderAppSettings();logs.SetBounds(u(18),u(18),u(155),u(42));data.SetBounds(u(185),u(18),u(155),u(42));edit.SetBounds(w-u(208),u(18),u(190),u(42));}
        page.SizeChanged+=(_,_)=>Arrange();page.VisibleChanged+=(_,_)=>{if(page.Visible){LoadAppSettingsDraft();Arrange();}};settingsDraft=preferences.Copy();settingsBackground=appearance.Background;Arrange();
        Disposed+=(_,_)=>FinishPageEntrance();SizeChanged+=(_,_)=>FinishPageEntrance();Resize+=(_,_)=>{if(applicationShown&&preferences.MinimizeToTray&&WindowState==FormWindowState.Minimized)Hide();};
    }
    void LoadAppSettingsDraft(){settingsDraft=preferences.Copy();settingsBackground=appearance.Background;RenderAppSettings();}
    void RenderAppSettings()
    {
        if(appSave==null)return;var page=pages["Settings"];page.AutoScrollPosition=Point.Empty;int u(int n)=>page.LogicalToDeviceUnits(n);foreach(Control control in settingsCard.Controls.Cast<Control>().ToArray())control.Dispose();int index=0,w=settingsCard.Width;int controlWidth=Math.Min(u(340),w/2-u(28));
        void Row(string title,string detail,Control input){int y=u(9+index++*66);LabelAt(settingsCard,title,u(22),y+u(6),w-controlWidth-u(65),u(25),12,Ink,true);LabelAt(settingsCard,detail,u(22),y+u(34),w-controlWidth-u(65),u(29),9.5f,Muted);input.SetBounds(w-controlWidth-u(22),y+u(14),controlWidth,u(39));settingsCard.Controls.Add(input);}
        void Toggle(string title,string detail,bool value,Action<bool> set){var input=new HarborToggle{Checked=value,AccessibleName=title,ShowStateLabel=false};input.CheckedChanged+=(_,_)=>set(input.Checked);Row(title,detail,input);input.Left=settingsCard.Width-u(72);input.Width=u(51);}
        void Choices(string title,string detail,object[] choices,int selected,Action<int> set){var input=new HarborDropdown{AccessibleName=title};input.Items.AddRange(choices);input.SelectedIndex=selected;input.SelectedIndexChanged+=(_,_)=>set(input.SelectedIndex);Row(title,detail,input);}
        if(settingsCategory=="Appearance"){
            Choices("Background artwork","Choose Harbor’s scene or the current pack’s art.",new object[]{"Harbor night scene","Current modpack artwork"},settingsBackground=="modpack"?1:0,i=>settingsBackground=i==1?"modpack":"night");
            Toggle("Page animations","Fade pages and settings screens into view.",settingsDraft.Animations,v=>settingsDraft.Animations=v);
            int[] speed={250,500,750,1000};Choices("Fade speed","Choose how long page transitions last.",new object[]{"Quick · 0.25 seconds","Normal · 0.5 seconds","Relaxed · 0.75 seconds","Slow · 1 second"},Math.Max(0,Array.IndexOf(speed,settingsDraft.FadeMilliseconds)),i=>settingsDraft.FadeMilliseconds=speed[i]);
            Toggle("Player head images","Load and cache Minecraft player avatars.",settingsDraft.PlayerHeads,v=>settingsDraft.PlayerHeads=v);
        }else if(settingsCategory=="Behavior"){
            Toggle("Remember last tab","Reopen the tab you used most recently.",settingsDraft.RememberPage,v=>settingsDraft.RememberPage=v);
            Toggle("Minimize to system tray","Hide from the taskbar when minimized. The shortcut reopens Harbor.",settingsDraft.MinimizeToTray,v=>settingsDraft.MinimizeToTray=v);
            Toggle("Keep this PC awake","Keep Windows awake while the server is running.",settingsDraft.KeepAwake,v=>settingsDraft.KeepAwake=v);
            Toggle("Confirm stop and restart","Ask before using the Stop or Restart buttons.",settingsDraft.ConfirmStop,v=>settingsDraft.ConfirmStop=v);
        }else if(settingsCategory=="Console"){
            float[] fonts={9,10.5f,12,14,16};Choices("Text size","Choose a comfortable size for console messages.",new object[]{"Small · 9 pt","Default · 10.5 pt","Medium · 12 pt","Large · 14 pt","Extra large · 16 pt"},Math.Max(0,Array.IndexOf(fonts,settingsDraft.ConsoleFontSize)),i=>settingsDraft.ConsoleFontSize=fonts[i]);
            Toggle("Follow new messages","Follow the latest logs unless you scroll up or select text.",settingsDraft.ConsoleFollow,v=>settingsDraft.ConsoleFollow=v);
            int[] limits={100000,300000,1000000};Choices("Visible log history","How much text the console retains. Log files stay on disk.",new object[]{"Short · 100,000 characters","Standard · 300,000 characters","Extended · 1,000,000 characters"},Math.Max(0,Array.IndexOf(limits,settingsDraft.ConsoleHistoryCharacters)),i=>settingsDraft.ConsoleHistoryCharacters=limits[i]);
        }else if(settingsCategory=="Notifications"){
            Toggle("Server status alerts","Notify when the server starts or stops while Harbor is in the background.",settingsDraft.NotifyServer,v=>settingsDraft.NotifyServer=v);
            Toggle("Error alerts","Notify about a server crash or failed automatic backup.",settingsDraft.NotifyErrors,v=>settingsDraft.NotifyErrors=v);
            Toggle("Backup completed alerts","Notify when a new backup is ready while Harbor is in the background.",settingsDraft.NotifyBackups,v=>settingsDraft.NotifyBackups=v);
        }
        bool tools=settingsCategory=="Tools";settingsCard.Visible=!tools;settingsTools.Visible=tools;appReset.Visible=appCancel.Visible=appSave.Visible=!tools;
        settingsTabs.Invalidate();settingsCard.SetBounds(0,u(180),w,u(index*66+24));int bottom=204+index*66+16;appReset.SetBounds(0,u(bottom),u(155),u(42));appCancel.SetBounds(w-u(303),u(bottom),u(125),u(42));appSave.SetBounds(w-u(165),u(bottom),u(165),u(42));settingsTools.SetBounds(0,u(180),w,u(78));page.AutoScrollMinSize=Size.Empty;
    }
    void ApplyAppPreferences()
    {
        console.TextBox.Configure(preferences.ConsoleFontSize,preferences.ConsoleFollow,preferences.ConsoleHistoryCharacters);
        if(!MotionEnabled){FinishHomeEntrance();FinishConsoleEntrance();FinishPageEntrance();}
        playerRowsSignature="";playerHeadRequests.RemoveWhere(id=>!playerHeads.ContainsKey(id));if(pages["Players"].Visible)RenderPlayerRows();
    }
    async Task ConfirmedServerAction(bool restartServer)
    {
        if(preferences.ConfirmStop&&MessageBox.Show(this,restartServer?"Save and restart this server?":"Save and stop this server?","Minecraft Harbor",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        await Run(restartServer?server.RestartAsync:server.StopAsync);
    }
    void NotifyInBackground(string title,string message,ToolTipIcon icon=ToolTipIcon.Info){if(closeInProgress||Visible&&WindowState!=FormWindowState.Minimized)return;tray.ShowBalloonTip(4000,title,message,icon);}
    void RefreshAppNotifications(FileInfo? latest)
    {
        if(notifiedProfile!=server.Profile.Id){notifiedProfile=server.Profile.Id;notifiedBackup=null;notifiedState=server.State;}
        if(server.State!=notifiedState){if(preferences.NotifyServer&&server.State is ServerState.Running or ServerState.Stopped)NotifyInBackground(server.State==ServerState.Running?"Server online":"Server stopped",server.Config.WorldName);if(preferences.NotifyErrors&&server.State==ServerState.Crashed)NotifyInBackground("Server needs attention","Open Harbor’s Console for details.",ToolTipIcon.Error);notifiedState=server.State;}
        string key=latest==null?"":latest.Name+latest.LastWriteTimeUtc.Ticks;if(notifiedBackup!=null&&key!=notifiedBackup&&key.Length>0&&preferences.NotifyBackups)NotifyInBackground("Backup completed",server.Config.WorldName+" has a new backup.");notifiedBackup=key;
    }
}
