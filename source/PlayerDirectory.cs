using System.Text.Json;
using System.Text.RegularExpressions;

namespace MinecraftHarbor;
internal sealed record ManagedPlayer(string Uuid,string Name,bool Online,bool Whitelisted,int OperatorLevel,bool Banned,string BanReason,long? PlayTicks,DateTime? LastSeenUtc,DateTime? LastSavedUtc)
{
    public bool Named=>Regex.IsMatch(Name,@"^[A-Za-z0-9_]{1,16}$");
    public string Status=>Banned?"Banned":Online?"Online":"Offline";
    public string Role=>OperatorLevel>0?"Operator":"Player";
    public string Playtime=>PlayTicks.HasValue?PlayerDirectory.Duration(PlayTicks.Value):"—";
    public string LastSeen=>Online?"Now":LastSeenUtc.HasValue?PlayerDirectory.Ago(LastSeenUtc.Value):LastSavedUtc.HasValue?"Saved "+LastSavedUtc.Value.ToLocalTime().ToString("MMM d"):"—";
}
internal sealed class PlayerHistory
{
    public string Name {get;set;}="";
    public string Uuid {get;set;}="";
    public DateTime? LastSeenUtc {get;set;}
}
internal sealed class PlayerDirectory
{
    readonly Dictionary<string,(DateTime Modified,long Size,long? Ticks)> statsCache=new(StringComparer.OrdinalIgnoreCase);
    internal static JsonElement[] ReadArray(string path)
    {
        if(!File.Exists(path))return [];
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var doc=JsonDocument.Parse(input);
        if(doc.RootElement.ValueKind!=JsonValueKind.Array)throw new InvalidDataException(Path.GetFileName(path)+" is not a player list.");return doc.RootElement.EnumerateArray().Select(e=>e.Clone()).ToArray();
    }
    internal static string Text(JsonElement item,string key)=>item.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    public IReadOnlyList<ManagedPlayer> Read(string serverDir,string worldDir,IReadOnlyList<PlayerHistory> history,IReadOnlyDictionary<string,DateTime> online)
    {
        var whitelist=ReadArray(Path.Combine(serverDir,"whitelist.json"));var ops=ReadArray(Path.Combine(serverDir,"ops.json"));var bans=ReadArray(Path.Combine(serverDir,"banned-players.json"));var cache=ReadArray(Path.Combine(serverDir,"usercache.json"));
        var people=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);void Add(string id,string name){if(Guid.TryParse(id,out var uuid)){string key=uuid.ToString();if(!people.ContainsKey(key)||name.Length>0)people[key]=name;}}
        foreach(var item in cache.Concat(whitelist).Concat(ops).Concat(bans))Add(Text(item,"uuid"),Text(item,"name"));foreach(var item in history)Add(item.Uuid,item.Name);
        var statsDir=Path.Combine(worldDir,"stats");if(Directory.Exists(statsDir))foreach(var file in Directory.EnumerateFiles(statsDir,"*.json"))Add(Path.GetFileNameWithoutExtension(file),"");
        var dataDir=Path.Combine(worldDir,"playerdata");if(Directory.Exists(dataDir))foreach(var file in Directory.EnumerateFiles(dataDir,"*.dat"))Add(Path.GetFileNameWithoutExtension(file),"");
        var result=new List<ManagedPlayer>();
        foreach(var person in people){bool isOnline=online.TryGetValue(person.Value,out var joined);var info=history.FirstOrDefault(p=>p.Uuid.Equals(person.Key,StringComparison.OrdinalIgnoreCase)||p.Name.Equals(person.Value,StringComparison.OrdinalIgnoreCase));var op=ops.FirstOrDefault(p=>Text(p,"uuid").Equals(person.Key,StringComparison.OrdinalIgnoreCase));var ban=bans.FirstOrDefault(p=>Text(p,"uuid").Equals(person.Key,StringComparison.OrdinalIgnoreCase));int level=op.ValueKind!=JsonValueKind.Undefined&&op.TryGetProperty("level",out var l)&&l.TryGetInt32(out int n)?n:0;
            string statsFile=Path.Combine(statsDir,person.Key+".json");long? ticks=null;DateTime? saved=null;
            if(File.Exists(statsFile)){var file=new FileInfo(statsFile);saved=file.LastWriteTimeUtc;if(!statsCache.TryGetValue(statsFile,out var cached)||cached.Modified!=file.LastWriteTimeUtc||cached.Size!=file.Length){long? parsed=null;try{using var stream=new FileStream(statsFile,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var json=JsonDocument.Parse(stream);if(json.RootElement.TryGetProperty("stats",out var stats)&&stats.TryGetProperty("minecraft:custom",out var custom)&&(custom.TryGetProperty("minecraft:play_time",out var value)||custom.TryGetProperty("minecraft:play_one_minute",out value))&&value.TryGetInt64(out var amount))parsed=Math.Max(0,amount);}catch(Exception ex)when(ex is IOException or JsonException){}cached=(file.LastWriteTimeUtc,file.Length,parsed);statsCache[statsFile]=cached;}ticks=cached.Ticks;
                if(isOnline&&ticks.HasValue){DateTime since=joined>file.LastWriteTimeUtc?joined:file.LastWriteTimeUtc;ticks+=Math.Max(0,(long)((DateTime.UtcNow-since).TotalSeconds*20));}
            }
            result.Add(new(person.Key,person.Value.Length>0?person.Value:person.Key[..8]+"…",isOnline,whitelist.Any(p=>Text(p,"uuid").Equals(person.Key,StringComparison.OrdinalIgnoreCase)),level,ban.ValueKind!=JsonValueKind.Undefined,ban.ValueKind!=JsonValueKind.Undefined?Text(ban,"reason"):"",ticks,info?.LastSeenUtc,saved));
        }
        foreach(var presence in online.Where(p=>!result.Any(r=>r.Name.Equals(p.Key,StringComparison.OrdinalIgnoreCase))))result.Add(new("",presence.Key,true,false,0,false,"",null,null,null));
        return result.OrderByDescending(p=>p.Online&&!p.Banned).ThenBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static IEnumerable<ManagedPlayer> Filter(IEnumerable<ManagedPlayer> players,string tab,string query)=>players.Where(p=>tab switch{"Whitelist"=>p.Whitelisted,"Operators"=>p.OperatorLevel>0,"Bans"=>p.Banned,_=>true}).Where(p=>string.IsNullOrWhiteSpace(query)||(p.Name+" "+p.Uuid+" "+p.Role+" "+p.Status).Contains(query.Trim(),StringComparison.OrdinalIgnoreCase));
    internal static string Duration(long ticks){long minutes=ticks/1200;return minutes>=60?minutes/60+"h "+minutes%60+"m":minutes+"m";}
    internal static string Ago(DateTime date){var age=DateTime.UtcNow-date.ToUniversalTime();return age.TotalMinutes<1?"Just now":age.TotalHours<1?(int)age.TotalMinutes+"m ago":age.TotalDays<1?(int)age.TotalHours+"h ago":age.TotalDays<7?(int)age.TotalDays+"d ago":date.ToLocalTime().ToString("MMM d, yyyy");}
}
