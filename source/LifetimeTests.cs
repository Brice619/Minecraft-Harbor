using System.Diagnostics;
using System.Text.Json;

namespace MinecraftHarbor;
internal static class LifetimeTests
{
    internal static void Window(string report)
    {
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"window-close-fixture-"+Guid.NewGuid().ToString("N"));
        LibraryTests.WriteWorld(Path.Combine(root,"server","world"));File.WriteAllText(Path.Combine(root,"server","eula.txt"),"eula=true");
        ProcessStartInfo Launcher(){var info=new ProcessStartInfo(Environment.ProcessPath!);info.ArgumentList.Add("--mock-server");return info;}
        using var server=new ServerManager(root,Launcher);using var form=new MainForm(server,lifetimeTestReport:report);
        Application.Run(form);
        if(server.HasProcess||server.State!=ServerState.Stopped||!File.Exists(Path.Combine(root,"server","mock-saved-on-stop.txt")))throw new Exception("Closing the window did not save and stop Minecraft");
        if(Directory.EnumerateFiles(server.BackupDir,"*.zip",SearchOption.AllDirectories).Any())throw new Exception("Closing the window created a backup");
        ServerManager.WriteJson(report,new{passed=true,checks=new[]{"Actual window close waits for saving and server shutdown instead of hiding to the tray","Closing adds no backup"},realServerTouched=false});
    }
    internal static async Task Owner(string root)
    {
        ProcessStartInfo Launcher(){var info=new ProcessStartInfo(Environment.ProcessPath!);info.ArgumentList.Add("--mock-server");return info;}
        using var server=new ServerManager(root,Launcher);
        await server.StartAsync();
        while(server.State!=ServerState.Running){if(server.State==ServerState.Crashed)throw new Exception("Fixture failed startup");await Task.Delay(20);}
        File.WriteAllText(Path.Combine(root,"owner-ready"),"ready");
        if(File.Exists(Path.Combine(root,"normal-close")))await server.CloseAsync();
        else await Task.Delay(Timeout.Infinite);
    }
    internal static async Task Run(string report)
    {
        string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"lifetime-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var checks=new List<string>();
        async Task Until(Func<bool> condition){var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed>TimeSpan.FromSeconds(20))throw new TimeoutException("Lifecycle test timed out");await Task.Delay(30);}}
        async Task Case(string name,bool forced,bool endSupervisor=false)
        {
            string root=Path.Combine(folder,name);LibraryTests.WriteWorld(Path.Combine(root,"server","world"));File.WriteAllText(Path.Combine(root,"server","eula.txt"),"eula=true");
            if(endSupervisor)File.WriteAllText(Path.Combine(root,"server","mock-stay-after-input-eof"),"stay alive until Windows ends the owned process");
            if(!forced)File.WriteAllText(Path.Combine(root,"normal-close"),"close normally");
            var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
            info.ArgumentList.Add("--lifetime-owner");info.ArgumentList.Add(root);
            using var owner=Process.Start(info)!;
            await Until(()=>File.Exists(Path.Combine(root,"owner-ready")));
            ProcessRecord? record=null;
            if(forced){
                record=JsonSerializer.Deserialize<ProcessRecord>(File.ReadAllText(Path.Combine(root,"server-process.json")));
                if(record?.SupervisorPid==null||record.OwnerPid!=owner.Id)throw new Exception("Separate lifetime owner was not recorded");
                // End only our disposable UI-equivalent process, as Task Manager would.
                if(endSupervisor){using var supervisor=Process.GetProcessById(record.SupervisorPid.Value);supervisor.Kill();}
                else owner.Kill();
            }
            if(endSupervisor){
                await Until(()=>!ServerSupervisor.IsSameProcess(record!));owner.Kill();await owner.WaitForExitAsync();
                using var reopened=new ServerManager(root);
                if(reopened.OtherServerRunning())throw new Exception("Terminating the supervisor stranded its Minecraft process");
                checks.Add("Force-ending the supervisor itself also ends its owned Minecraft process through Windows; a stale process record does not prevent reopening");return;
            }
            await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await Until(()=>File.Exists(Path.Combine(root,"server","mock-saved-on-stop.txt"))&&!File.Exists(Path.Combine(root,"server-process.json")));
            if(record!=null&&ServerSupervisor.IsSameProcess(record))throw new Exception("Minecraft fixture survived close");
            if(Directory.Exists(Path.Combine(root,"backups"))&&Directory.EnumerateFiles(Path.Combine(root,"backups"),"*.zip",SearchOption.AllDirectories).Any())throw new Exception("Close created an unwanted backup");
            string[] commands=File.ReadAllLines(Path.Combine(root,"server","mock-commands.log"));
            if(!commands.Contains("stop")||commands.Count(c=>c=="stop")!=1)throw new Exception("Shutdown did not send one stop request");
            checks.Add(forced?"Force-ending the window owner makes the independent supervisor enable saving, stop Minecraft, wait for the mock save, and clear its process record":"Normal close waits for Minecraft's save and shutdown; no ZIP backup is created");
        }
        await Case("normal",false);await Case("force-ended",true);await Case("supervisor-ended",true,true);
        ServerManager.WriteJson(report,new{passed=true,checks,realServerTouched=false});
    }
}
