using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MinecraftHarbor;

internal sealed record SupervisedLaunch(string FileName,string WorkingDirectory,string[] Arguments,
    int OwnerPid,DateTime OwnerStartedUtc,string Root,string ProfileId);

// This process owns Minecraft's stdin until Minecraft has saved and exited.
// The window process can be force-terminated without losing the stop channel.
internal static class ServerSupervisor
{
    internal static ProcessStartInfo Wrap(ProcessStartInfo server,string root,string profileId)
    {
        using var owner=Process.GetCurrentProcess();
        string folder=Path.Combine(root,"runtime-state");Directory.CreateDirectory(folder);
        string request=Path.Combine(folder,"launch-"+Guid.NewGuid().ToString("N")+".json");
        if(server.Arguments.Length>0)throw new InvalidOperationException("The server launcher must use an argument list.");
        ServerManager.WriteJson(request,new SupervisedLaunch(server.FileName,server.WorkingDirectory,
            server.ArgumentList.ToArray(),owner.Id,owner.StartTime.ToUniversalTime(),root,profileId));
        var start=new ProcessStartInfo(Environment.ProcessPath!){WorkingDirectory=server.WorkingDirectory,
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,
            RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        start.ArgumentList.Add("--server-supervisor");start.ArgumentList.Add(request);return start;
    }

    internal static bool IsSameProcess(ProcessRecord record)
    {
        try{using var process=Process.GetProcessById(record.Pid);return !process.HasExited&&
            Math.Abs((process.StartTime.ToUniversalTime()-record.StartedUtc).TotalSeconds)<1;}
        catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
    }

    internal static async Task<int> Run(string request)
    {
        var spec=JsonSerializer.Deserialize<SupervisedLaunch>(File.ReadAllText(request))
            ??throw new InvalidDataException("Missing server launch details.");
        File.Delete(request);
        var ownerRecord=new ProcessRecord(spec.OwnerPid,spec.OwnerStartedUtc);
        if(!IsSameProcess(ownerRecord))return 1;
        var info=new ProcessStartInfo(spec.FileName){WorkingDirectory=spec.WorkingDirectory,
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,
            RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        foreach(string argument in spec.Arguments)info.ArgumentList.Add(argument);
        using var server=new Process{StartInfo=info};
        using var lifetime=new ServerLifetimeJob();
        using var cancel=new CancellationTokenSource();
        var inputGate=new SemaphoreSlim(1,1);var outputGate=new object();
        int stopping=0;bool forced=false,started=false;long stopRequested=0;
        string recordFile=Path.Combine(spec.Root,"server-process.json");
        void Output(string text,bool error=false)
        {
            lock(outputGate){try{var writer=error?Console.Error:Console.Out;writer.WriteLine(text);writer.Flush();}
                catch(IOException){}catch(ObjectDisposedException){}}
        }
        void LifecycleLog(string text)
        {
            Output("[Harbor] "+text);
            try{Directory.CreateDirectory(Path.Combine(spec.Root,"logs"));File.AppendAllText(
                Path.Combine(spec.Root,"logs","server-lifetime.log"),DateTime.UtcNow.ToString("o")+" "+text+Environment.NewLine);}
            catch(IOException){}
        }
        async Task WriteInput(string command)
        {
            await inputGate.WaitAsync();
            try{if(!server.HasExited){await server.StandardInput.WriteLineAsync(command);await server.StandardInput.FlushAsync();}}
            catch(IOException){}catch(InvalidOperationException){}
            finally{inputGate.Release();}
        }
        async Task RequestStop(string reason)
        {
            if(Interlocked.Exchange(ref stopping,1)!=0)return;
            Volatile.Write(ref stopRequested,Stopwatch.GetTimestamp());LifecycleLog(reason+" Saving and stopping Minecraft.");
            // A killed UI may have been in the middle of a live backup.
            await WriteInput("save-on");await WriteInput("stop");
        }
        async Task Pump(StreamReader reader,bool error)
        {
            string? line;while((line=await reader.ReadLineAsync())!=null)Output(line,error);
        }
        async Task ReadCommands()
        {
            try{
                while(!cancel.IsCancellationRequested){
                    string? command=await Console.In.ReadLineAsync(cancel.Token);
                    if(command==null){await RequestStop("Harbor's command connection closed.");return;}
                    if(command.Trim().Equals("stop",StringComparison.OrdinalIgnoreCase))await RequestStop("Stop requested.");
                    else if(Volatile.Read(ref stopping)==0)await WriteInput(command);
                }
            }catch(OperationCanceledException){}catch(IOException){await RequestStop("Harbor's command connection was lost.");}
        }
        async Task WatchOwner()
        {
            try{
                while(!cancel.IsCancellationRequested){
                    if(!IsSameProcess(ownerRecord))await RequestStop("Harbor exited.");
                    long when=Volatile.Read(ref stopRequested);
                    if(when!=0&&Stopwatch.GetElapsedTime(when)>TimeSpan.FromMinutes(4)&&!server.HasExited){
                        forced=true;LifecycleLog("Minecraft did not finish stopping within four minutes. Terminating the stalled process; a completed save is not confirmed.");
                        server.Kill(entireProcessTree:true);return;
                    }
                    await Task.Delay(250,cancel.Token);
                }
            }catch(OperationCanceledException){}
        }
        try{
            server.Start();started=true;lifetime.Own(server);using var supervisor=Process.GetCurrentProcess();
            var output=Pump(server.StandardOutput,false);var errors=Pump(server.StandardError,true);
            ServerManager.WriteJson(recordFile,new ProcessRecord(server.Id,server.StartTime.ToUniversalTime()){
                SupervisorPid=supervisor.Id,SupervisorStartedUtc=supervisor.StartTime.ToUniversalTime(),
                OwnerPid=spec.OwnerPid,OwnerStartedUtc=spec.OwnerStartedUtc,ProfileId=spec.ProfileId});
            // Console's synchronized reader can block before returning its Task.
            // Keep it off the lifetime monitor's thread, and do not wait for an
            // open command pipe after Minecraft has already exited.
            _=Task.Run(ReadCommands);var owner=WatchOwner();
            await server.WaitForExitAsync();cancel.Cancel();
            await Task.WhenAll(output,errors,owner);
            LifecycleLog(forced?"Minecraft was terminated after the shutdown timeout.":"Minecraft exited with code "+server.ExitCode+".");
            return forced?1:server.ExitCode;
        }catch(Exception ex){
            LifecycleLog("Server supervisor failed: "+ex.Message);
            if(started&&!server.HasExited){
                await RequestStop("Supervisor failure.");
                try{await server.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(4));}
                catch(TimeoutException){LifecycleLog("Shutdown stalled after a supervisor error. Terminating Minecraft; a completed save is not confirmed.");server.Kill(entireProcessTree:true);await server.WaitForExitAsync();}
            }
            return 1;
        }finally{
            cancel.Cancel();
            // Never remove a record belonging to a newer launch.
            try{if(File.Exists(recordFile)&&JsonSerializer.Deserialize<ProcessRecord>(File.ReadAllText(recordFile))?.SupervisorPid==Environment.ProcessId)File.Delete(recordFile);}
            catch(IOException){}catch(JsonException){}
        }
    }
}
