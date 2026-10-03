using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MinecraftHarbor;
public sealed partial class ServerManager
{
    readonly SemaphoreSlim commandHelpGate=new(1,1);
    readonly object commandHelpLock=new(),commandInputLock=new();
    HelpReply? commandHelpReply;
    sealed class HelpReply(string root)
    {
        internal readonly string Root=root;
        internal readonly HashSet<string> Usages=new(StringComparer.Ordinal);
        internal long LastReply=Stopwatch.GetTimestamp();
    }
    // Read only help; never send the draft itself to discover arguments.
    internal async Task<string[]> CommandHelpAsync(string path,CancellationToken token)
    {
        if(path.Length>256||!Regex.IsMatch(path,@"^[a-zA-Z0-9_:. /-]*$"))return [];
        await commandHelpGate.WaitAsync(token);
        HelpReply? reply=null;
        try{
            if(State!=ServerState.Running||!HasProcess)return [];
            var process=child;string root=path.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";
            reply=new(root);lock(commandHelpLock)commandHelpReply=reply;
            lock(commandInputLock){if(child!=process||!HasProcess||State!=ServerState.Running)return [];child!.StandardInput.WriteLine("help"+(path.Length>0?" "+path:""));child.StandardInput.Flush();}
            var watch=Stopwatch.StartNew();
            while(watch.Elapsed<TimeSpan.FromSeconds(5)){
                await Task.Delay(50,token);
                if(State!=ServerState.Running||child!=process||!HasProcess)return [];
                lock(commandHelpLock)if(reply.Usages.Count>0&&Stopwatch.GetElapsedTime(reply.LastReply)>TimeSpan.FromMilliseconds(400))return reply.Usages.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            lock(commandHelpLock)return reply.Usages.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }finally{lock(commandHelpLock)if(commandHelpReply==reply)commandHelpReply=null;commandHelpGate.Release();}
    }
    bool ReceiveCommandHelp(string line)
    {
        // Ignore chat and unrelated logs even while an internal help request is active.
        var match=Regex.Match(line,@"(?:^|:\s+)(/[a-zA-Z0-9_:.\-]+(?:\s+[^\r\n]*)?)$");
        if(!match.Success)return false;string usage=match.Groups[1].Value.Trim();
        string root=usage[1..].Split(' ')[0];
        lock(commandHelpLock){var reply=commandHelpReply;if(reply==null||usage.Length>4096||(reply.Root.Length>0&&root!=reply.Root))return false;reply.Usages.Add(usage);reply.LastReply=Stopwatch.GetTimestamp();return true;}
    }
    internal async Task SendConsoleCommandAsync(string text)
    {
        string normalized=text.Trim().TrimStart('/');
        if(normalized.Length==0||normalized.Length>2048||normalized.Any(c=>c is '\r' or '\n' or '\0'))throw new ArgumentException("Enter one server command.");
        if(normalized is "stop" or "minecraft:stop"){Log("> "+normalized);await StopAsync();return;}
        // Let internal help finish so user help output is never swallowed by it.
        await commandHelpGate.WaitAsync();commandHelpGate.Release();Send(normalized);
    }
}
