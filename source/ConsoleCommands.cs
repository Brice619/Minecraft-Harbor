using System.Text.Json;
using System.Text.RegularExpressions;

namespace MinecraftHarbor;
internal sealed record CommandChoice(string Text,string Hint,int Start,int Length){public override string ToString()=>Text;}
internal sealed class ConsoleCommandCatalog
{
    readonly Dictionary<string,List<string>> usages=new(StringComparer.Ordinal);
    internal string[] Roots=>usages.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    internal void Add(IEnumerable<string> lines)
    {
        foreach(string line in lines){string value=line.Trim().TrimStart('/');string root=value.Split(' ')[0];if(!Regex.IsMatch(root,@"^[a-zA-Z0-9_:.-]+$"))continue;if(!usages.TryGetValue(root,out var list))usages[root]=list=[];if(!list.Contains(value,StringComparer.Ordinal))list.Add(value);}
    }
    internal string Hint(string text)
    {
        string root=text.TrimStart().TrimStart('/').Split(' ')[0];
        return usages.TryGetValue(root,out var list)?string.Join("   •   ",list.Take(2)):"Tab to complete · ↑/↓ command history";
    }
    internal CommandChoice[] Suggest(string text,int caret,IEnumerable<string> players)
    {
        caret=Math.Clamp(caret,0,text.Length);string before=text[..caret];int start=before.LastIndexOf(' ')+1;string fragment=before[start..];if(start==0&&fragment.StartsWith('/')){start++;fragment=fragment[1..];}
        int end=caret;while(end<text.Length&&!char.IsWhiteSpace(text[end]))end++;
        var previous=before[..(before.LastIndexOf(' ')+1)].Trim().TrimStart('/').Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var found=new Dictionary<string,string>(StringComparer.Ordinal);
        if(previous.Length==0){foreach(string root in Roots)found[root]=usages[root][0];}
        else if(usages.TryGetValue(previous[0],out var list)){
            foreach(string usage in list){var tokens=Regex.Matches(usage,@"<[^>]+>|\([^)]*\)|\[[^]]*\]|[^\s]+").Select(m=>m.Value).ToArray();if(tokens.Length<=previous.Length)continue;
                bool fits=true;for(int i=0;i<previous.Length;i++){if(tokens[i].Contains("...")){fits=false;break;}if(!tokens[i].StartsWith('<')&&!Literals(tokens[i]).Contains(previous[i],StringComparer.OrdinalIgnoreCase)){fits=false;break;}}
                if(!fits)continue;string next=tokens[previous.Length];foreach(string literal in Literals(next))found[literal]=usage;
                if(next.StartsWith('<')&&Regex.IsMatch(next,@"(?i)player|target|member|name")){foreach(string player in players.Where(p=>Regex.IsMatch(p,@"^[a-zA-Z0-9_]{1,16}$")))found[player]=usage;}
            }
        }
        return found.Where(p=>p.Key.StartsWith(fragment,StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(p=>new CommandChoice(p.Key,p.Value,start,end-start)).ToArray();
    }
    static IEnumerable<string> Literals(string token)=>token.Trim('(',')','[',']').Split('|').Where(t=>Regex.IsMatch(t,@"^[a-zA-Z0-9_:.-]+$"));
}
internal sealed class ConsoleCommandHistory
{
    readonly string file;
    internal List<string> Entries{get;}=[];
    int index;string draft="";
    internal ConsoleCommandHistory(string file){this.file=file;try{var rows=JsonSerializer.Deserialize<string[]>(File.ReadAllText(file))??[];Entries.AddRange(rows.Where(Valid).TakeLast(100));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){}Reset();}
    static bool Valid(string text)=>text.Length is >0 and <=2048&&!text.Any(c=>c is '\r' or '\n' or '\0');
    internal void Record(string text){text=text.Trim();if(!Valid(text))return;if(Entries.LastOrDefault()!=text)Entries.Add(text);if(Entries.Count>100)Entries.RemoveRange(0,Entries.Count-100);Reset();Directory.CreateDirectory(Path.GetDirectoryName(file)!);ServerManager.WriteJson(file,Entries);}
    internal void Reset(){index=Entries.Count;draft="";}
    internal string Move(int direction,string current){if(Entries.Count==0)return current;if(index==Entries.Count&&direction<0)draft=current;index=Math.Clamp(index+direction,0,Entries.Count);return index==Entries.Count?draft:Entries[index];}
}
