using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace MinecraftHarbor;
internal static class LocalMods
{
    internal static string Identity(string path,string loader)
    {
        using var zip=ZipFile.OpenRead(path);string metadata=loader=="fabric"?"fabric.mod.json":loader=="neoforge"&&zip.GetEntry("META-INF/neoforge.mods.toml")!=null?"META-INF/neoforge.mods.toml":"META-INF/mods.toml";
        var entry=zip.GetEntry(metadata)??throw new InvalidDataException("This JAR does not identify a mod for "+loader+".");if(entry.Length>1024*1024)throw new InvalidDataException("The mod metadata is too large.");using var reader=new StreamReader(entry.Open());string text=reader.ReadToEnd();string id;
        if(loader=="fabric"){using var doc=JsonDocument.Parse(text);id=doc.RootElement.GetProperty("id").GetString()!;}
        else{var match=Regex.Match(text,@"(?m)^\s*modId\s*=\s*[""']([a-zA-Z0-9_\-]+)[""']");if(!match.Success)throw new InvalidDataException("This JAR does not identify its mod.");id=match.Groups[1].Value;}
        if(!Regex.IsMatch(id,@"^[a-zA-Z0-9_\-]+$"))throw new InvalidDataException("Invalid mod identifier.");return id;
    }
    internal static void Install(string folder,LocalMod mod,string loader)
    {
        var matches=new List<string>();foreach(var file in Directory.EnumerateFiles(folder,"*.jar")){try{if(Identity(file,loader)==mod.ModId)matches.Add(file);}catch(Exception ex)when(ex is InvalidDataException or JsonException){} }
        foreach(var file in matches)File.Delete(file);string target=Path.Combine(folder,mod.Name);if(File.Exists(target))throw new InvalidDataException("An unrelated mod uses the same filename as "+mod.Name+".");File.Copy(mod.File,target);
    }
}
