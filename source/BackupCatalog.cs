using System.IO.Compression;
using System.Text.Json;

namespace MinecraftHarbor;

internal sealed record BackupEntry(string File,string Name,string Description,DateTime CreatedUtc,long Bytes,string Kind,bool Protected,bool Restorable)
{
    public string Size=>BackupCatalog.FormatSize(Bytes);
}
internal sealed record BackupLabel(string Name,string Description);
internal sealed class BackupCatalog
{
    readonly Dictionary<string,(long Size,DateTime Modified,BackupEntry Entry)> cache=new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<BackupEntry> Read(string directory)
    {
        if(!Directory.Exists(directory))return Array.Empty<BackupEntry>();
        var entries=new List<BackupEntry>();
        foreach(var file in new DirectoryInfo(directory).EnumerateFiles("*.zip")){
            if(!cache.TryGetValue(file.FullName,out var item)||item.Size!=file.Length||item.Modified!=file.LastWriteTimeUtc){var entry=ReadEntry(file);cache[file.FullName]=(file.Length,file.LastWriteTimeUtc,entry);}
            entries.Add(cache[file.FullName].Entry);
        }
        foreach(var missing in cache.Keys.Where(k=>!File.Exists(k)).ToArray())cache.Remove(missing);
        return entries.OrderByDescending(e=>e.CreatedUtc).ToArray();
    }
    static BackupEntry ReadEntry(FileInfo file)
    {
        bool original=file.Name.StartsWith("original-",StringComparison.OrdinalIgnoreCase),restorable=false;
        string kind=original?"Original":"Manual",name=original?"Original imported save":"Manual Backup",description=original?"Protected original save":"World save",reason="";DateTime created=file.LastWriteTimeUtc;
        try{
            using var zip=ZipFile.OpenRead(file.FullName);restorable=zip.GetEntry("world/level.dat")!=null||zip.GetEntry("level.dat")!=null;
            var meta=zip.GetEntry("harbor-backup.json");
            if(meta!=null&&meta.Length<=65536){using var reader=new StreamReader(meta.Open());using var json=JsonDocument.Parse(reader.ReadToEnd());var root=json.RootElement;
                if(root.TryGetProperty("createdUtc",out var date)&&date.TryGetDateTime(out var value))created=value.ToUniversalTime();
                if(root.TryGetProperty("reason",out var r))reason=r.GetString()??"";
            }
            if(!original){if(reason=="hourly"||file.Name.EndsWith("_hourly.zip",StringComparison.OrdinalIgnoreCase)){kind="Automatic";name="Automatic Backup";description="Hourly world save";}
                else if(reason.StartsWith("before-")){name=reason switch{"before-restore"=>"Before Restore","before-switch"=>"Before Server Switch","before-restart"=>"Before Restart",_=>"Recovery Backup"};description="World save before the change";}
                else if(file.Name.Contains("after-close")||file.Name.Contains("after-stop")){name="Previous World Save";description="Earlier saved world";}
            }
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException){description="Backup could not be read";restorable=false;}
        try{var labelPath=file.FullName+".label.json";if(System.IO.File.Exists(labelPath)){var label=JsonSerializer.Deserialize<BackupLabel>(System.IO.File.ReadAllText(labelPath));if(label!=null&&!string.IsNullOrWhiteSpace(label.Name)){name=label.Name;if(restorable)description=label.Description;}}}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){/* A display note cannot invalidate a saved world. */}
        return new(file.FullName,name,description,created,file.Length,kind,original,restorable);
    }
    public static IEnumerable<BackupEntry> Search(IEnumerable<BackupEntry> entries,string text)=>entries.Where(e=>string.IsNullOrWhiteSpace(text)||(e.Name+" "+e.Description+" "+e.Kind+" "+Path.GetFileName(e.File)).Contains(text.Trim(),StringComparison.OrdinalIgnoreCase));
    public static string FormatSize(long bytes)=>bytes>=1073741824?(bytes/1073741824d).ToString("0.0")+" GB":bytes>=1048576?(bytes/1048576d).ToString("0.0")+" MB":bytes>=1024?(bytes/1024d).ToString("0.0")+" KB":bytes+" B";
    public void Rename(string directory,BackupEntry entry,string name,string description)
    {
        Validate(directory,entry);if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>100||description.Length>240)throw new ArgumentException("Enter a backup name of up to 100 characters and a note of up to 240 characters.");
        ServerManager.WriteJson(entry.File+".label.json",new BackupLabel(name.Trim(),description.Trim()));cache.Remove(entry.File);
    }
    public void Delete(string directory,BackupEntry entry)
    {
        Validate(directory,entry);if(entry.Protected||Path.GetFileName(entry.File).StartsWith("original-",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("The original imported save is protected.");
        System.IO.File.Delete(entry.File);if(System.IO.File.Exists(entry.File+".label.json"))System.IO.File.Delete(entry.File+".label.json");cache.Remove(entry.File);
    }
    public static void Export(string directory,BackupEntry entry,string destination)
    {
        Validate(directory,entry);destination=Path.GetFullPath(destination);if(destination.Equals(Path.GetFullPath(entry.File),StringComparison.OrdinalIgnoreCase))throw new IOException("Choose a different location for the exported backup.");
        System.IO.File.Copy(entry.File,destination,true);
    }
    static void Validate(string directory,BackupEntry entry)
    {
        if(!string.Equals(Path.GetDirectoryName(Path.GetFullPath(entry.File)),Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||Path.GetExtension(entry.File)!=".zip"||!System.IO.File.Exists(entry.File))throw new IOException("This backup is no longer in the selected server's backup folder.");
    }
}
