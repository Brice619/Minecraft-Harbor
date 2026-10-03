using System.Diagnostics;
using System.Text.Json;
using HarborUpdates;
namespace HarborAgent;

internal static class UpdateTests
{
    internal static void InstallFiles(string root,List<string> checks)
    {
        string folder=Path.Combine(root,"client-install"),source=Environment.ProcessPath!;Directory.CreateDirectory(folder);
        string target=Path.Combine(folder,"Minecraft Harbor Client.exe"),config=Path.Combine(folder,"connection.json");
        File.WriteAllText(target,"old application");File.WriteAllText(config,"preserve my pairing and folder");
        if(!ClientInstallation.Exists(folder))throw new Exception("Existing client was not detected");
        using(var locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            bool rejected=false;try{ClientInstallation.Install(source,folder);}catch(IOException){rejected=true;}
            if(!rejected||File.ReadAllText(target)!="old application")throw new Exception("Locked update damaged the installed client");
        }
        ClientInstallation.Install(source,folder);
        if(new FileInfo(target).Length<65536||!File.Exists(Path.Combine(folder,"Minecraft Harbor Client.dll"))||!File.Exists(Path.Combine(folder,"coreclr.dll"))||File.ReadAllText(config)!="preserve my pairing and folder")throw new Exception("Client update missed compiled files or altered its connection");
        if(Directory.GetDirectories(folder,".harbor-install-*").Length!=0)throw new Exception("Client installer left staging files");
        File.Delete(Path.Combine(folder,"System.Management.dll"));
        ClientInstallation.Install(Environment.ProcessPath!,folder);
        if(!File.Exists(Path.Combine(folder,"System.Management.dll")))throw new Exception("Client update did not repair a missing compiled dependency");
        checks.Add("Client installer extracts and verifies all compiled client/runtime files, replaces an older single-file client, repairs missing DLLs, preserves pairing, and rejects locked updates before replacement");
        string report=Path.Combine(root,"installed-runtime.json");
        var start=new ProcessStartInfo(target){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--runtime-check");start.ArgumentList.Add(report);
        using var installed=Process.Start(start)??throw new Exception("Installed client did not start");
        if(!installed.WaitForExit(30000)||installed.ExitCode!=0||!File.Exists(report))throw new Exception("Installed client is missing runtime files");
        checks.Add("The actual installed client starts from its installation folder with its bundled runtime, loads its Windows UI and system-query dependencies, and preserves existing connection files");
    }

    internal static void RuntimeCheck(string report)
    {
        using var form=new Form();form.CreateControl();
        using var query=new System.Management.ManagementObjectSearcher("SELECT Version FROM Win32_OperatingSystem");using var results=query.Get();
        if(results.Count==0)throw new Exception("Windows system-query dependency is unavailable");
        File.WriteAllText(report,JsonSerializer.Serialize(new{passed=true,uiLoaded=true,systemQueryLoaded=true,bundledRuntime=true}));
    }

    internal static void Fixture(string report)
    {
        using var form=new Form{Text="Harbor update shutdown fixture",ShowInTaskbar=false};
        using var timer=new System.Windows.Forms.Timer{Interval=100};bool closing=false,ready=false;
        form.Shown+=(_,_)=>{form.Hide();File.WriteAllText(report+".ready","ready");timer.Start();};
        form.FormClosing+=(_,e)=>{if(ready)return;e.Cancel=true;closing=true;};
        var watch=Stopwatch.StartNew();
        timer.Tick+=(_,_)=>{
            if(File.Exists(report+".release")||closing&&!report.Contains("veto")&&watch.ElapsedMilliseconds>1000)
            {File.WriteAllText(report+".saved","saved before exit");ready=true;form.Close();}
        };
        Application.Run(form);
    }

    internal static void Shutdown(string report)
    {
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"shutdown-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        foreach(bool veto in new[]{false,true})
        {
            string fixture=Path.Combine(root,veto?"veto":"save");
            var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--shutdown-fixture");start.ArgumentList.Add(fixture);
            using var process=Process.Start(start)??throw new Exception("Fixture did not start");
            try
            {
                var wait=Stopwatch.StartNew();while(!File.Exists(fixture+".ready")){if(process.HasExited||wait.Elapsed>TimeSpan.FromSeconds(20))throw new Exception("Fixture did not become ready");Thread.Sleep(50);}
                bool timedOut=false;try{UpdateShutdown.CloseApplication(Environment.ProcessPath!,TimeSpan.FromSeconds(veto?1:10));}catch(IOException){timedOut=true;}
                if(veto){if(!timedOut||process.HasExited)throw new Exception("Updater forced a busy application closed");}
                else if(timedOut||!process.HasExited||!File.Exists(fixture+".saved"))throw new Exception("Hidden application was not saved and closed");
            }
            finally{File.WriteAllText(fixture+".release","release");if(!process.WaitForExit(15000))throw new Exception("Fixture cleanup failed");}
        }
        File.WriteAllText(report,JsonSerializer.Serialize(new{passed=true,hiddenAppClosed=true,waitedForSave=true,noForcedTermination=true,realAppsTouched=false}));
    }
}
