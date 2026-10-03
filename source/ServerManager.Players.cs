using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MinecraftHarbor;
internal enum PlayerAction {Whitelist,RemoveWhitelist,Operator,RemoveOperator,Ban,Unban,Kick,Teleport}
public sealed partial class ServerManager
{
    readonly object playerPresenceLock=new();
    readonly Dictionary<string,DateTime> onlinePlayerNames=new(StringComparer.OrdinalIgnoreCase);
    List<PlayerHistory> playerHistory=new();string playerHistoryKey="";
    readonly PlayerDirectory playerDirectory=new();
    readonly object playerDirectoryLock=new();
    TaskCompletionSource<string>? playerActionReply;string playerReplyName="",playerReplyAction="";
    string PlayerHistoryPath=>Path.Combine(ProfileRoot,"player-history",Config.WorldFolder+".json");
    void EnsurePlayerHistory()
    {
        if(playerHistoryKey==PlayerHistoryPath)return;playerHistoryKey=PlayerHistoryPath;onlinePlayerNames.Clear();
        try{playerHistory=File.Exists(playerHistoryKey)?JsonSerializer.Deserialize<List<PlayerHistory>>(File.ReadAllText(playerHistoryKey))??new():new();}catch(Exception ex)when(ex is IOException or JsonException){playerHistory=new();Log("Player history could not be read: "+ex.Message);}
    }
    void SavePlayerHistory(){try{Directory.CreateDirectory(Path.GetDirectoryName(PlayerHistoryPath)!);WriteJson(PlayerHistoryPath,playerHistory);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Log("Player history could not be saved: "+ex.Message);}}
    internal void ObservePlayerPresence(string line)
    {
        var message=Regex.Match(line,@"^(?:\[[^\]]+\] \[(?:Server thread|User Authenticator #\d+)/INFO\](?: \[[^\]]+\])?: )?(.+)$");if(!message.Success)return;string text=message.Groups[1].Value;
        lock(playerPresenceLock){EnsurePlayerHistory();
            var id=Regex.Match(text,@"^UUID of player ([A-Za-z0-9_]{1,16}) is ([a-fA-F0-9-]{32,36})$");if(id.Success&&Guid.TryParse(id.Groups[2].Value,out var uuid)){var known=playerHistory.FirstOrDefault(p=>p.Name.Equals(id.Groups[1].Value,StringComparison.OrdinalIgnoreCase));if(known==null){known=new(){Name=id.Groups[1].Value};playerHistory.Add(known);}known.Uuid=uuid.ToString();SavePlayerHistory();}
            var joined=Regex.Match(text,@"^([A-Za-z0-9_]{1,16}) joined the game$");var left=Regex.Match(text,@"^([A-Za-z0-9_]{1,16}) left the game$");
            var list=Regex.Match(text,@"^There are (\d+) of a max of \d+ players online:?(.*)$");bool changed=false;
            if(joined.Success){string name=joined.Groups[1].Value;onlinePlayerNames.TryAdd(name,utcNow());MarkSeen(name);changed=true;}
            if(left.Success){string name=left.Groups[1].Value;onlinePlayerNames.Remove(name);MarkSeen(name);changed=true;}
            if(list.Success){var names=list.Groups[2].Value.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Where(n=>Regex.IsMatch(n,@"^[A-Za-z0-9_]{1,16}$")).ToHashSet(StringComparer.OrdinalIgnoreCase);foreach(var previous in onlinePlayerNames.Keys.Where(n=>!names.Contains(n)).ToArray()){MarkSeen(previous);onlinePlayerNames.Remove(previous);changed=true;}foreach(var name in names)if(onlinePlayerNames.TryAdd(name,utcNow())){MarkSeen(name);changed=true;}}
            if(changed||list.Success){OnlinePlayers=onlinePlayerNames.Count;OnlineNames=OnlinePlayers==0?"Nobody online":string.Join(", ",onlinePlayerNames.Keys.Order(StringComparer.OrdinalIgnoreCase));if(changed)SavePlayerHistory();}
            if(playerActionReply!=null&&text.Contains(playerReplyName,StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(text,playerReplyAction))playerActionReply.TrySetResult(text);
        }
    }
    void MarkSeen(string name){var entry=playerHistory.FirstOrDefault(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase));if(entry==null){entry=new(){Name=name};playerHistory.Add(entry);}entry.LastSeenUtc=utcNow();}
    void ResetPlayerPresence(bool recordDepartures=false){lock(playerPresenceLock){EnsurePlayerHistory();if(recordDepartures){foreach(var name in onlinePlayerNames.Keys)MarkSeen(name);SavePlayerHistory();}onlinePlayerNames.Clear();OnlinePlayers=0;OnlineNames="Nobody online";playerActionReply?.TrySetException(new IOException("The server exited before confirming the player action."));}}
    internal IReadOnlyList<ManagedPlayer> ManagedPlayers()
    {
        PlayerHistory[] history;Dictionary<string,DateTime> online;string directory,world,key;lock(playerPresenceLock){EnsurePlayerHistory();directory=ServerDir;world=WorldDir;key=playerHistoryKey;history=playerHistory.Select(p=>new PlayerHistory{Name=p.Name,Uuid=p.Uuid,LastSeenUtc=p.LastSeenUtc}).ToArray();online=new(onlinePlayerNames,StringComparer.OrdinalIgnoreCase);}
        lock(playerDirectoryLock){var players=playerDirectory.Read(directory,world,history,online);lock(playerPresenceLock){if(key==PlayerHistoryPath){bool changed=false;foreach(var player in players.Where(p=>p.Uuid.Length>0&&p.Named)){var match=playerHistory.FirstOrDefault(p=>p.Name.Equals(player.Name,StringComparison.OrdinalIgnoreCase));if(match!=null&&match.Uuid!=player.Uuid){match.Uuid=player.Uuid;changed=true;}}if(changed)SavePlayerHistory();}}return players;}
    }
    internal async Task<Player> ResolveManagedPlayer(string name)
    {
        ValidatePlayerName(name);var known=ManagedPlayers().FirstOrDefault(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)&&Guid.TryParse(p.Uuid,out _));if(known!=null)return new(known.Uuid,known.Name);
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};using var response=await http.GetAsync("https://api.mojang.com/users/profiles/minecraft/"+Uri.EscapeDataString(name));if(!response.IsSuccessStatusCode||response.StatusCode==System.Net.HttpStatusCode.NoContent)throw new IOException("Minecraft could not find that username. Check the spelling and try again.");using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return new(Guid.Parse(json.RootElement.GetProperty("id").GetString()!).ToString(),json.RootElement.GetProperty("name").GetString()!);
    }
    internal static void ValidatePlayerName(string name){if(!Regex.IsMatch(name,@"^[A-Za-z0-9_]{1,16}$"))throw new ArgumentException("Enter a Minecraft username using letters, numbers, or underscores.");}
    internal static string PlayerCommand(PlayerAction action,Player player,string value="")
    {
        ValidatePlayerName(player.name);if(value.Length>200||value.Any(c=>c is '\r' or '\n' or '\0'))throw new ArgumentException("Enter a reason of up to 200 characters on one line.");
        return action switch{PlayerAction.Whitelist=>"whitelist add "+player.name,PlayerAction.RemoveWhitelist=>"whitelist remove "+player.name,PlayerAction.Operator=>"op "+player.name,PlayerAction.RemoveOperator=>"deop "+player.name,PlayerAction.Ban=>"ban "+player.name+(value.Length>0?" "+value:""),PlayerAction.Unban=>"pardon "+player.name,PlayerAction.Kick=>"kick "+player.name+(value.Length>0?" "+value:""),_=>throw new ArgumentException("Choose a teleport destination.")};
    }
    internal Task PlayerActionAsync(PlayerAction action,Player player,string value="")=>Perform("Updating "+player.name+"…",async()=>{
        ValidatePlayerName(player.name);if(!Guid.TryParse(player.uuid,out var id))throw new ArgumentException("This player's Minecraft identity is not available yet.");player=player with{uuid=id.ToString()};
        if(player.uuid.Equals(Config.OwnerUuid,StringComparison.OrdinalIgnoreCase)&&action is PlayerAction.RemoveWhitelist or PlayerAction.RemoveOperator or PlayerAction.Ban)throw new InvalidOperationException("The owner keeps access to this server.");
        string command=PlayerCommand(action,player,value);
        if(HasProcess){if(State!=ServerState.Running)throw new InvalidOperationException("Wait until the server is online.");string file=action switch{PlayerAction.Whitelist or PlayerAction.RemoveWhitelist=>"whitelist.json",PlayerAction.Operator or PlayerAction.RemoveOperator=>"ops.json",PlayerAction.Ban or PlayerAction.Unban=>"banned-players.json",_=>""};
            var reply=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);lock(playerPresenceLock){playerActionReply=reply;playerReplyName=player.name;playerReplyAction=action switch{PlayerAction.Whitelist=>@"^Added .+ to the whitelist$",PlayerAction.RemoveWhitelist=>@"^Removed .+ from the whitelist$",PlayerAction.Operator=>@"^Made .+ a server operator$",PlayerAction.RemoveOperator=>@"^Made .+ no longer a server operator$",PlayerAction.Ban=>@"^Banned .+",PlayerAction.Unban=>@"^Unbanned .+",PlayerAction.Kick=>@"^Kicked .+",_=>@"^Teleported .+"};}
            try{SendCore(command);if(file.Length>0){bool present=action is PlayerAction.Whitelist or PlayerAction.Operator or PlayerAction.Ban;var watch=System.Diagnostics.Stopwatch.StartNew();while(watch.Elapsed.TotalSeconds<8){await Task.Delay(100);if(!HasProcess)throw new IOException("Server exited before confirming this change.");try{if(PlayerDirectory.ReadArray(Path.Combine(ServerDir,file)).Any(e=>PlayerDirectory.Text(e,"uuid").Equals(player.uuid,StringComparison.OrdinalIgnoreCase))==present)return;}catch(Exception ex)when(ex is IOException or JsonException){}}throw new IOException("Minecraft has not confirmed this change. Check Console before trying again.");}await reply.Task.WaitAsync(TimeSpan.FromSeconds(8));}
            finally{lock(playerPresenceLock)if(playerActionReply==reply)playerActionReply=null;}
        }else{
            RequireStopped();if(action==PlayerAction.Kick)throw new InvalidOperationException("Kick is available only while the player is online.");
            string file=action is PlayerAction.Whitelist or PlayerAction.RemoveWhitelist?"whitelist.json":action is PlayerAction.Operator or PlayerAction.RemoveOperator?"ops.json":"banned-players.json";var path=Path.Combine(ServerDir,file);var list=File.Exists(path)?JsonNode.Parse(File.ReadAllText(path)) as JsonArray??throw new InvalidDataException("Player list could not be read."):new JsonArray();
            foreach(var item in list.Where(n=>n?["uuid"]?.GetValue<string>().Equals(player.uuid,StringComparison.OrdinalIgnoreCase)==true).ToArray())list.Remove(item);
            if(action is PlayerAction.Whitelist or PlayerAction.Operator or PlayerAction.Ban){var item=new JsonObject{["uuid"]=player.uuid,["name"]=player.name};if(action==PlayerAction.Operator){item["level"]=4;item["bypassesPlayerLimit"]=false;}if(action==PlayerAction.Ban){item["created"]=DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");item["source"]="Minecraft Harbor";item["expires"]="forever";item["reason"]=value.Length>0?value:"Banned by a server operator.";}list.Add(item);}WriteJson(path,list);Log("Updated "+player.name+" in "+file+".");
        }
    });
    internal static string TeleportCommand(string name,string destination){ValidatePlayerName(name);ValidatePlayerName(destination);if(name.Equals(destination,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose another online player.");return "tp "+name+" "+destination;}
    internal Task TeleportPlayerAsync(string name,string destination)=>Perform("Teleporting "+name+"…",async()=>{
        string command=TeleportCommand(name,destination);lock(playerPresenceLock){if(!onlinePlayerNames.ContainsKey(name)||!onlinePlayerNames.ContainsKey(destination))throw new InvalidOperationException("Both players must be online.");}
        var reply=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);lock(playerPresenceLock){playerActionReply=reply;playerReplyName=name;playerReplyAction=@"^Teleported .+";}
        try{SendCore(command);await reply.Task.WaitAsync(TimeSpan.FromSeconds(8));}finally{lock(playerPresenceLock)if(playerActionReply==reply)playerActionReply=null;}
    });
}
