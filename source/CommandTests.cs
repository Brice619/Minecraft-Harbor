using System.Diagnostics;

namespace MinecraftHarbor;
internal static class CommandTests
{
    internal static void ShowDemo(string directory)
    {
        string root=Path.Combine(Path.GetFullPath(directory),"console-demo-"+Guid.NewGuid().ToString("N"));
        LibraryTests.WriteWorld(Path.Combine(root,"server","world"));File.WriteAllText(Path.Combine(root,"server","eula.txt"),"eula=true");
        ServerManager.WriteJson(Path.Combine(root,"settings.json"),new Settings{MemoryGB=8,AutomaticBackups=false,WorldName="Console test"});
        ProcessStartInfo Launcher(){var p=new ProcessStartInfo(Environment.ProcessPath!);p.ArgumentList.Add("--mock-server");return p;}
        using var manager=new ServerManager(root,Launcher);using var form=new MainForm(manager,consoleTestReport:"__command_demo__");Application.Run(form);
    }
    internal static async Task Run(string report)
    {
        report=Path.GetFullPath(report);Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        string root=Path.Combine(Path.GetDirectoryName(report)!,"commands-fixture-"+Guid.NewGuid().ToString("N"));
        LibraryTests.WriteWorld(Path.Combine(root,"server","world"));File.WriteAllText(Path.Combine(root,"server","eula.txt"),"eula=true");
        ServerManager.WriteJson(Path.Combine(root,"settings.json"),new Settings{MemoryGB=8,AutomaticBackups=false});
        ProcessStartInfo Launcher(){var p=new ProcessStartInfo(Environment.ProcessPath!);p.ArgumentList.Add("--mock-server");return p;}
        using var server=new ServerManager(root,Launcher);var checks=new List<string>();
        void Assert(bool pass,string message){if(!pass)throw new Exception(message);}
        try{
            await server.StartAsync();var watch=Stopwatch.StartNew();while(server.State!=ServerState.Running){if(watch.Elapsed.TotalSeconds>10)throw new TimeoutException();await Task.Delay(20);}
            var catalog=new ConsoleCommandCatalog();var roots=await server.CommandHelpAsync("",CancellationToken.None);catalog.Add(roots);
            Assert(catalog.Roots.Contains("stop")&&catalog.Roots.Contains("ftbultimine")&&catalog.Roots.Contains("ftbchunks"),"Live vanilla/server/mod roots missing");
            Assert(!server.Lines.Any(l=>l.Contains("/ftbultimine")),"Internal discovery flooded visible console");
            Assert(catalog.Suggest("/ftbu",5,[]).Single().Text=="ftbultimine","Slash completion incorrect");
            Assert(catalog.Suggest("whitelist ",10,[]).Any(c=>c.Text=="add"),"Alternative literals missing");
            catalog.Add(await server.CommandHelpAsync("whitelist",CancellationToken.None));
            Assert(catalog.Suggest("whitelist add Te",16,["TestOwner"]).Single().Text=="TestOwner","Player argument completion missing");
            Assert(catalog.Hint("kick TestOwner").Contains("<targets>"),"Syntax hint missing");
            var middle=catalog.Suggest("whitelist ad TestOwner",12,[]).Single(c=>c.Text=="add");Assert(middle.Start==10&&middle.Length==2,"Mid-command completion overwrites later arguments");
            catalog.Add(await server.CommandHelpAsync("gamerule",CancellationToken.None));Assert(catalog.Suggest("gamerule keep",13,[]).Any(c=>c.Text=="keepInventory"),"Deeper help does not discover rule arguments");
            var before=File.ReadAllLines(Path.Combine(server.ServerDir,"mock-commands.log"));Assert((await server.CommandHelpAsync("list\nstop",CancellationToken.None)).Length==0,"Unsafe help query accepted");Assert(File.ReadAllLines(Path.Combine(server.ServerDir,"mock-commands.log")).SequenceEqual(before),"Draft injection sent commands");
            checks.Add("Reads actual mock-server help, including mod commands; discovers subcommands and rules; preserves later arguments and ignores unsafe drafts");
            var history=new ConsoleCommandHistory(Path.Combine(root,"console-history.json"));history.Record("list");history.Record("kick TestOwner");history.Record("kick TestOwner");Assert(history.Entries.Count==2,"Consecutive history duplicated");Assert(history.Move(-1,"unfinished")=="kick TestOwner"&&history.Move(-1,"")=="list"&&history.Move(1,"")=="kick TestOwner"&&history.Move(1,"")=="unfinished","History or draft restoration incorrect");
            Assert(new ConsoleCommandHistory(Path.Combine(root,"console-history.json")).Entries.Count==2,"History not persisted");Assert(new ConsoleCommandHistory(Path.Combine(root,"other-profile","console-history.json")).Entries.Count==0,"History leaked across server profiles");
            for(int i=0;i<110;i++)history.Record("say "+i);Assert(history.Entries.Count==100,"History unbounded");checks.Add("Per-server history persists, removes consecutive repeats, retains 100 entries, and restores the unfinished draft");
            await server.SendConsoleCommandAsync("/list");await Task.Delay(80);Assert(server.Lines.Any(l=>l.Contains("There are 1")),"User command response missing");
            bool rejected=false;try{await server.SendConsoleCommandAsync("list\nstop");}catch(ArgumentException){rejected=true;}Assert(rejected&&server.HasProcess,"Multiline command accepted");
            int backups=Directory.GetFiles(server.BackupDir,"*.zip").Length;await server.SendConsoleCommandAsync("/stop");Assert(!server.HasProcess&&server.State==ServerState.Stopped,"Console stop did not save and stop gracefully");Assert(Directory.GetFiles(server.BackupDir,"*.zip").Length==backups,"Console stop made unwanted backup");
            Assert((await server.CommandHelpAsync("",CancellationToken.None)).Length==0,"Offline discovery started a process");
            checks.Add("User commands reach the process and return logs; stop uses safe shutdown without a backup; offline help never starts a server");
            ServerManager.WriteJson(report,new{passed=true,checks,realServerTouched=false});
        }finally{if(server.HasProcess)await server.StopAsync();}
    }
}
