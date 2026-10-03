using System.Diagnostics;

namespace MinecraftHarbor;
internal static class ConsoleTests
{
    internal static void Run(string report)
    {
        report=Path.GetFullPath(report);Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        string root=Path.Combine(Path.GetDirectoryName(report)!,"console-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"server","world"));
        ServerManager.WriteJson(Path.Combine(root,"settings.json"),new Settings{MemoryGB=8});
        ServerManager.WriteJson(Path.Combine(root,"appearance.json"),new AppearanceSettings());
        using var manager=new ServerManager(root);using var form=new MainForm(manager,consoleTestReport:report);Application.Run(form);
    }
}
public sealed partial class MainForm
{
    internal async Task VerifyConsoleDesign(string report)
    {
        var checks=new List<string>();void Assert(bool pass,string message){if(!pass)throw new Exception(message);}
        void Capture(string name){using var bitmap=new Bitmap(Width,Height);DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(Path.Combine(Path.GetDirectoryName(report)!,name+".png"));}
        try{
            ShowPage("Console");await Task.Delay(500);FinishConsoleEntrance();
            Assert(!pages["Console"].Controls.OfType<Button>().Any(b=>b.Text is "Clear Console" or "Open Server Folder" or "List players" or "Save world" or "Open logs"),"Unrequested console buttons remain");
            Assert(!command.Enabled&&!consoleEnter.Enabled,"Stopped server accepts commands");
            Assert(ConsoleSyntax.Display("10:14:22  [10:14:22] [Server thread/INFO]: Starting")=="[10:14:22] [Server thread/INFO]: Starting","Server timestamps were duplicated");
            console.Clear();console.AppendLines(new[]{"10:14:22  [10:14:22] [Server thread/INFO]: Starting Minecraft server version 1.21.1","10:14:23  [Server thread/INFO]: Loading All the Mods 10","10:14:25  [INFO] Preparing spawn area: 100%","10:14:39  [Server thread/INFO]: Done (17.823s)! For help, type \"help\"","10:15:12  [Server thread/INFO]: Notch joined the game","10:17:45  [Server thread/WARN]: Can't keep up! Is the server overloaded? Running 2498ms or 49 ticks behind","10:18:03  [Server thread/ERROR]: Test error message","10:18:27  [Server thread/INFO]: Saved the game (took 1.234s)"});
            var log=console.TextBox;
            void ColorAt(string text,Color color){log.Select(log.Text.IndexOf(text,StringComparison.Ordinal),text.Length);Assert(log.SelectionColor==color,"Log color incorrect: "+text);}
            ColorAt("Server thread/INFO",ConsoleSyntax.Info);ColorAt("Can't keep up!",ConsoleSyntax.Warning);ColorAt("Test error message",ConsoleSyntax.Error);ColorAt("Notch",ConsoleSyntax.Success);
            log.Select(0,0);Capture("console-stopped");
            foreach(var state in new[]{ServerState.Starting,ServerState.Running,ServerState.Stopping,ServerState.Crashed,ServerState.Stopped}){server.State=state;server.StartedAt=state==ServerState.Stopped?null:DateTime.Now.AddMinutes(-134);command.Text="list";RefreshConsoleStatus();Assert(command.Enabled==(state==ServerState.Running)&&consoleEnter.Enabled==(state==ServerState.Running),"Command availability does not track "+state);}
            server.State=ServerState.Running;server.StartedAt=DateTime.Now.AddMinutes(-134);RefreshConsoleStatus();Assert(ConsoleSessionText().StartsWith("Uptime: 2h 14m"),"Uptime incorrect");Capture("console-running");
            foreach(var size in new[]{new Size(1160,680),new Size(1672,940),new Size(1320,744)}){ClientSize=size;await Task.Delay(60);Assert(console.Bottom<consoleCommandFrame.Top&&consoleCommandFrame.Bottom<=pages["Console"].Height,"Console overlaps command bar or footer");Assert(consoleEnter.Right<=pages["Console"].Width&&consoleCommandFrame.Right<consoleEnter.Left,"Command buttons overlap");Assert(console.Height>=140&&console.TextBox.Height>100,"Log viewport clipped");Capture("console-"+size.Width);}
            checks.Add("Reference layout fits minimum, standard and large windows; footer and command bar do not overlap; omitted buttons stay absent");
            checks.Add("Real state mapping and uptime drive the banner; Enter is available only for a nonempty command while running");
            console.AppendLines(Enumerable.Range(0,500).Select(i=>"10:19:00  [Server thread/INFO]: History line "+i).ToArray());log.Select(20,9);log.ScrollToLine(0);int first=log.FirstLine;string selected=log.SelectedText;console.AppendLines(new[]{"10:20:00  [INFO] New live message"});Assert(log.FirstLine==first&&log.SelectedText==selected,"New logs moved the history reader or removed selection");
            log.Select(log.TextLength,0);log.ScrollToLine(log.LastFirst);console.AppendLines(new[]{"10:20:01  [INFO] Following new output"});Assert(log.FirstLine>=log.LastFirst-1,"Live output did not follow bottom");
            var watch=Stopwatch.StartNew();console.AppendLines(Enumerable.Range(0,2600).Select(i=>"10:20:02  [INFO] Burst "+i+" "+new string('x',110)).ToArray());watch.Stop();Assert(log.TextLength<=300000,"Log retention is unbounded");Assert(log.Text.Contains("Burst 2599"),"Newest burst lines lost");
            checks.Add("Severity and player highlights retain selectable text; duplicate timestamps removed; new messages preserve history position and selection or follow the bottom");checks.Add("A 2,600-line burst is batched and bounded; newest messages retained ("+watch.ElapsedMilliseconds+"ms)");
            log.Clear();console.AppendLines(new[]{"10:14:22  [INFO] Starting Minecraft server version 1.21.1","10:14:23  [INFO] Loading All the Mods 10","10:14:25  [INFO] Loading mods…","10:14:32  [INFO] Preparing spawn area: 100%","10:14:39  [INFO] Done (17.823s)! For help, type \"help\"","10:15:12  [INFO] Notch joined the game","10:17:45  [WARN] Can't keep up! Is the server overloaded? Running 2498ms or 49 ticks behind","10:18:27  [INFO] Saved the game (took 1.234s)"});log.Select(0,0);Capture("console-final");
            consoleCommands.Add(new[]{"/list","/stop","/save-all [flush]","/ftbchunks (claim|unclaim|admin)","/ftbultimine (toggle|shape)","/whitelist add <targets>","/whitelist remove <targets>"});
            command.Focus();command.Text="ftb";command.SelectionStart=command.TextLength;UpdateCommandSuggestions();
            Assert(commandSuggestions.Visible&&commandSuggestions.Selected?.Text=="ftbchunks","Command suggestions did not appear");
            Assert(commandSuggestions.Top>=console.Top&&commandSuggestions.Bottom<consoleCommandFrame.Top&&commandSuggestions.Right<=consoleCommandFrame.Right,"Suggestions cover input or exceed viewport");Capture("console-command-suggestions");
            typeof(Control).GetMethod("OnKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(command,new object[]{new KeyEventArgs(Keys.Tab)});
            Assert(command.Text=="ftbchunks ","Tab did not complete or inserted the wrong command");
            consoleHistory!.Record("list");consoleHistory.Record("save-all flush");command.Text="unfinished";
            typeof(Control).GetMethod("OnKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(command,new object[]{new KeyEventArgs(Keys.Up)});
            Assert(command.Text=="save-all flush","Up did not recall command history");
            typeof(Control).GetMethod("OnKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(command,new object[]{new KeyEventArgs(Keys.Down)});
            Assert(command.Text=="unfinished","Down did not restore the draft");
            command.Text="whitelist ";command.SelectionStart=command.TextLength;commandPopupDismissed=false;UpdateCommandSuggestions();Capture("console-command-arguments");
            Assert(commandSuggestions.Selected?.Text=="add","Subcommands not offered");
            server.State=ServerState.Stopped;RefreshConsoleStatus();Assert(!commandSuggestions.Visible&&consoleCommands.Roots.Length==0,"Stopped server retained stale suggestions");
            checks.Add("Suggestion popup fits above the entry; Tab completes without sending, Up recalls history, Down restores a draft, and stopping clears live suggestions");
            ServerManager.WriteJson(report,new{passed=true,checks,realServerTouched=false});
        }catch(Exception ex){ServerManager.WriteJson(report,new{passed=false,error=ex.ToString(),checks,textLength=console.TextBox.TextLength,stringLength=console.TextBox.Text.Length,firstLine=console.TextBox.FirstLine,lineCount=console.TextBox.LineCount});}
        finally{server.State=ServerState.Stopped;server.StartedAt=null;exiting=true;Close();}
    }
}
