using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
namespace MinecraftHarbor;
internal static class VanillaWorldSetup
{
    internal static async Task Generate(ServerProfile profile,string folder,Settings settings,Dictionary<string,string> rules,Action<SetupProgress> progress,CancellationToken token)
    {
        string propsFile=Path.Combine(folder,"server.properties"),original=File.ReadAllText(propsFile);
        var privateProperties=original.Split('\n').Where(l=>!l.StartsWith("server-port=")&&!l.StartsWith("server-ip="));
        File.WriteAllLines(propsFile,privateProperties.Concat(new[]{"server-port=0","server-ip=127.0.0.1"}),new UTF8Encoding(false));
        using var process=new Process{StartInfo=new ProcessStartInfo(profile.JavaPath){WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(var argument in new[]{"-Xms512M","-Xmx4G","-jar",profile.LaunchFile,"nogui"})process.StartInfo.ArgumentList.Add(argument);
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var saved=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);bool applying=false;object logLock=new();string log=Path.Combine(folder,"harbor-setup.log");
        void Line(object _,DataReceivedEventArgs e){if(e.Data==null)return;lock(logLock)File.AppendAllText(log,e.Data+Environment.NewLine);
            if(Regex.IsMatch(e.Data,@"Done \([\d.,]+s\)!"))ready.TrySetResult();
            if(applying&&(e.Data.Contains("Unknown",StringComparison.OrdinalIgnoreCase)||e.Data.Contains("Incorrect argument",StringComparison.OrdinalIgnoreCase)||e.Data.Contains("No game rule",StringComparison.OrdinalIgnoreCase)))saved.TrySetException(new InvalidDataException("Minecraft rejected a game setting. See harbor-setup.log."));
            if(applying&&(e.Data.Contains("Saved the game",StringComparison.OrdinalIgnoreCase)||e.Data.Contains("Saved the world",StringComparison.OrdinalIgnoreCase)))saved.TrySetResult();
        }
        process.OutputDataReceived+=Line;process.ErrorDataReceived+=Line;
        try{
            token.ThrowIfCancellationRequested();process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
            var exited=process.WaitForExitAsync();var first=await Task.WhenAny(ready.Task,exited).WaitAsync(TimeSpan.FromMinutes(4),token);
            if(first==exited&&!ready.Task.IsCompletedSuccessfully)throw new IOException("Minecraft exited while creating the world. See harbor-setup.log.");
            await ready.Task;progress(new(2,0,"Applying your chosen game rules and saving the new world…"));applying=true;
            foreach(var rule in rules){var command=VanillaCatalog.RuleCommand(profile.MinecraftVersion,rule.Key,rule.Value);await process.StandardInput.WriteLineAsync("gamerule "+command.Key+" "+command.Value);}
            await process.StandardInput.WriteLineAsync("save-all");await process.StandardInput.FlushAsync();
            var finish=await Task.WhenAny(saved.Task,exited).WaitAsync(TimeSpan.FromSeconds(60),token);
            if(finish==exited&&!saved.Task.IsCompletedSuccessfully)throw new IOException("Minecraft exited before the new world finished saving.");await saved.Task;
            progress(new(2,1,"World settings saved. Closing the setup process…"));await process.StandardInput.WriteLineAsync("stop");await process.StandardInput.FlushAsync();await exited.WaitAsync(TimeSpan.FromMinutes(2),token);
            string level=Path.Combine(folder,"world","level.dat");if(process.ExitCode!=0||!File.Exists(level))throw new IOException("The new world did not finish saving. See harbor-setup.log.");
            new WorldMetadata(level).Save(level,settings.WorldName,new());profile.Worlds[0].CanGenerate=false;
        }finally{
            try{if(process.Id>0&&!process.HasExited){try{await process.StandardInput.WriteLineAsync("stop");await process.StandardInput.FlushAsync();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));}catch{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}}}catch(InvalidOperationException){}
            File.WriteAllText(propsFile,original,new UTF8Encoding(false));
        }
    }
}
