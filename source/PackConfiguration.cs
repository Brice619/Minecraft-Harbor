using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace MinecraftHarbor;
internal sealed record PackConfigValue(string File,string Key,string Original,string Kind,int Line=-1)
{
    internal string Value {get;set;}=Original;
    internal string Label=>Regex.Replace(Key.Split('.').Last(),"([a-z])([A-Z])","$1 $2").Replace('_',' ');
}
internal static class PackConfiguration
{
    internal static List<PackConfigValue> Discover(string archive)
    {
        using var zip=ZipFile.OpenRead(archive);var result=new List<PackConfigValue>();var root=zip.Entries.FirstOrDefault(e=>e.FullName.Replace('\\','/').Contains("mods/")&&e.Name.EndsWith(".jar"))?.FullName.Replace('\\','/');int mods=root?.IndexOf("mods/",StringComparison.Ordinal)??-1;string prefix=mods>0?root![..mods]:"";
        foreach(var entry in zip.Entries){string name=entry.FullName.Replace('\\','/');if(!name.StartsWith(prefix))continue;name=name[prefix.Length..];if(!(name.StartsWith("config/")||name.StartsWith("defaultconfigs/"))||entry.Length>512*1024||result.Count>=500)continue;
            if(name.Split('/').Any(p=>p is ".." or "."||p.Contains(':')))continue;using var reader=new StreamReader(entry.Open());string text=reader.ReadToEnd();
            try{result.AddRange(Parse(name,text).Take(500-result.Count));}catch(JsonException){}
        }return result;
    }
    internal static List<PackConfigValue> Parse(string file,string text)
    {
        var values=new List<PackConfigValue>();
        if(file.EndsWith(".json",StringComparison.OrdinalIgnoreCase)){
            var node=JsonNode.Parse(text);void Walk(JsonNode? n,string key){if(n is JsonObject obj){foreach(var p in obj)if(!p.Key.Contains('.'))Walk(p.Value,key.Length==0?p.Key:key+"."+p.Key);}else if(n is JsonValue v){string raw=v.ToJsonString();if(raw is "true" or "false")values.Add(new(file,key,raw,"bool"));else if(int.TryParse(raw,out _))values.Add(new(file,key,raw,"int"));}}Walk(node,"");
        }else if(file.EndsWith(".toml",StringComparison.OrdinalIgnoreCase)||file.EndsWith(".properties",StringComparison.OrdinalIgnoreCase)){
            string section="";var lines=text.Split('\n');for(int i=0;i<lines.Length;i++){string line=lines[i].Trim();if(line.StartsWith('[')){section=line.Trim('[',']',' ','\r');continue;}var match=Regex.Match(line,@"^([\w.\-]+)\s*=\s*(true|false|[+-]?\d+)\s*(?:#.*)?$");if(match.Success)values.Add(new(file,(section.Length>0?section+".":"")+match.Groups[1].Value,match.Groups[2].Value,match.Groups[2].Value is "true" or "false"?"bool":"int",i));}
        }return values;
    }
    internal static void Apply(string server,IEnumerable<PackConfigValue> settings)
    {
        foreach(var group in settings.Where(v=>v.Value!=v.Original).GroupBy(v=>v.File)){
            string prefix=Path.GetFullPath(server)+Path.DirectorySeparatorChar;string file=Path.GetFullPath(Path.Combine(server,group.Key.Replace('/',Path.DirectorySeparatorChar)));if(!file.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!System.IO.File.Exists(file))throw new InvalidDataException("A modpack configuration file is missing.");
            string text=System.IO.File.ReadAllText(file);var available=Parse(group.Key,text);
            foreach(var value in group){if(!available.Any(v=>v.Key==value.Key&&v.Original==value.Original&&v.Kind==value.Kind))throw new InvalidDataException("The modpack configuration has changed. Reload its settings.");if(value.Kind=="bool"&&value.Value is not ("true" or "false")||value.Kind=="int"&&!int.TryParse(value.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out _))throw new ArgumentException("Invalid modpack setting: "+value.Label);}
            if(group.Key.EndsWith(".json",StringComparison.OrdinalIgnoreCase)){var node=JsonNode.Parse(text)!;foreach(var value in group){JsonNode parent=node;var keys=value.Key.Split('.');foreach(var key in keys[..^1])parent=parent[key]!;parent[keys[^1]]=value.Kind=="bool"?JsonValue.Create(bool.Parse(value.Value)):JsonValue.Create(int.Parse(value.Value));}text=node.ToJsonString(new JsonSerializerOptions{WriteIndented=true});}
            else{var lines=text.Split('\n');foreach(var value in group)lines[value.Line]=new Regex(@"(=\s*)(true|false|[+-]?\d+)",RegexOptions.None,TimeSpan.FromSeconds(1)).Replace(lines[value.Line],m=>m.Groups[1].Value+value.Value,1);text=string.Join('\n',lines);}
            System.IO.File.WriteAllText(file,text);
        }
    }
}
