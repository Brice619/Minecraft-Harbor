using System.Net.Http.Json;
using System.Text.Json;
using MinecraftHarbor;
namespace HarborAgent;
public sealed record PackSyncLocalFile(string Path,string Hash,long Size,long WrittenUtcTicks);
public sealed record PackSyncState(string Host,string ServerId,string[] Files,PackSyncLocalFile[]? LocalFiles=null);
public sealed record PackSyncTransaction(string Backup,string[] Files,string[] Existed,string? LoaderBefore=null,string? LoaderAfter=null);
public sealed record PendingPackUpdate(string Stage,string[] Updated,string[] Removed,PackSyncState State,string ProfileHash,string? LoaderAfter=null);
public sealed partial class ClientCore
{
    static readonly JsonSerializerOptions PackJson=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public async Task<string> SyncPack(string folder,LanSyncInfo info,Action<string>? check=null)
    {
        check??=RequireGameClosed;check(folder);ClientProfile.Read(folder);await Task.Run(()=>{Recover(folder,check,HarborUpdates.UpdateShutdown.CloseCurseForge);PackUpdateStorage.RetireLegacyMetadata(folder,HarborUpdates.UpdateShutdown.CloseCurseForge);});
        Progress?.Invoke("Checking mod updates…");
        var profile=ClientProfile.Read(folder);if(info.ProjectId>0?profile.ProjectId!=info.ProjectId:!profile.Name.Equals(info.Pack,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Choose an existing copy of "+info.Pack+" from the same modpack project.");
        bool updateLoader=ReadLoaderVersion(folder,info,false)!=info.LoaderVersion;
        if(updateLoader){if(info.Loader!="neoforge")RequireMatchingLoader(folder,info);Progress?.Invoke("Closing CurseForge…");await Task.Run(HarborUpdates.UpdateShutdown.CloseCurseForge);}
        using var response=await Post(Config.Host+"harbor/manifest",new PackSyncRequest(Config.Id,Config.Token,""));response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>32*1024*1024)throw new InvalidDataException("Pack manifest is too large.");var manifest=await response.Content.ReadFromJsonAsync<PackSyncManifest>()??throw new InvalidDataException("Missing pack manifest.");
        ValidateManifest(manifest);if(!SameServerVersion(info,manifest))throw new InvalidDataException("The server changed packs. Check for updates again.");
        string stateFile=System.IO.Path.Combine(folder,".harbor-sync-state.json");var previous=File.Exists(stateFile)?JsonSerializer.Deserialize<PackSyncState>(File.ReadAllText(stateFile),PackJson):null;if(previous!=null&&(previous.Host!=Config.Host||previous.ServerId!=manifest.ServerId))previous=null;
        var paths=manifest.Files.Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changes=await Task.Run(()=>{var existing=PackSyncPaths.Enumerate(folder).ToHashSet(StringComparer.OrdinalIgnoreCase);var cache=(previous?.LocalFiles??[]).ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase);return manifest.Files.Where(f=>{if(!existing.Contains(f.Path))return true;string target=System.IO.Path.Combine(folder,f.Path.Replace('/',System.IO.Path.DirectorySeparatorChar));var local=new FileInfo(target);if(local.Length!=f.Size)return true;if(cache.TryGetValue(f.Path,out var known)&&known.Hash==f.Hash&&known.Size==local.Length&&known.WrittenUtcTicks==local.LastWriteTimeUtc.Ticks)return false;return PackSyncPaths.HashFile(target)!=f.Hash;}).ToArray();});
        var removed=(previous?.Files??[]).Where(PackSyncPaths.Allowed).Concat(PackSyncPaths.Enumerate(folder).Where(p=>p.StartsWith("mods/",StringComparison.OrdinalIgnoreCase)&&p.EndsWith(".jar",StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).Where(p=>!paths.Contains(p)&&File.Exists(PackSyncPaths.Resolve(folder,p))).ToArray();
        var nextState=new PackSyncState(Config.Host,manifest.ServerId,paths.ToArray(),manifest.Files.Select(f=>new PackSyncLocalFile(f.Path,f.Hash,f.Size,0)).ToArray());
        if(changes.Length==0&&removed.Length==0&&!updateLoader){SaveState(stateFile,nextState);return "Up to date";}
        string stage=PackUpdateStorage.NewStage(folder);
        try{
            int index=0;foreach(var file in changes){Progress?.Invoke($"Downloading update {++index} of {changes.Length}…");using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(2));using var download=await Post(Config.Host+"harbor/file",new PackSyncRequest(Config.Id,Config.Token,manifest.Hash,file.Path));download.EnsureSuccessStatusCode();if(download.Content.Headers.ContentLength!=file.Size)throw new InvalidDataException("Downloaded file size changed: "+file.Path);string destination=PackSyncPaths.Resolve(stage,file.Path);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);using(var input=await download.Content.ReadAsStreamAsync())using(var output=File.Create(destination)){byte[] buffer=new byte[65536];long size=0;int count;while((count=await input.ReadAsync(buffer,deadline.Token))>0){size+=count;if(size>file.Size)throw new InvalidDataException("Update exceeds its published size.");await output.WriteAsync(buffer.AsMemory(0,count),deadline.Token);}if(size!=file.Size)throw new InvalidDataException("Download was incomplete.");}if(PackSyncPaths.HashFile(destination)!=file.Hash)throw new InvalidDataException("Update failed verification. Your pack has not been changed.");}
            LoaderProfileUpdate? loaderUpdate=updateLoader?await NeoForgeUpdate.Prepare(folder,info,Progress,check):null;
            check(folder);var latest=await Info();if(!SameServerVersion(latest,manifest)||!latest.FullPack)throw new InvalidDataException("The server changed during download. Check for updates again.");
            if(loaderUpdate==null)RequireMatchingLoader(folder,latest);Progress?.Invoke("Applying updates to your selected profile…");await Task.Run(()=>CommitPack(folder,stage,changes.Select(f=>f.Path).ToArray(),removed,nextState,check,loaderUpdate));
            RequireMatchingLoader(folder,latest);
            return $"Up to date · {changes.Length} updated, {removed.Length} removed"+(updateLoader?" · NeoForge "+info.LoaderVersion:"");
        }finally{if(!File.Exists(System.IO.Path.Combine(folder,".harbor-sync-transaction.json")))PackUpdateStorage.CleanStage(stage);}
    }
    internal static void ValidateManifest(PackSyncManifest m){if(m.Files==null||m.Files.Length>100000||m.Files.Sum(f=>f.Size)>16L*1024*1024*1024||m.Files.Any(f=>!PackSyncPaths.Allowed(f.Path)||f.Size<0||f.Size>512L*1024*1024||f.Hash.Length!=64||f.Hash.Any(c=>!Uri.IsHexDigit(c)))||m.Files.Select(f=>f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=m.Files.Length||PackSyncPaths.HashList(m.Files)!=m.Hash)throw new InvalidDataException("Invalid pack manifest.");}
    static void SaveState(string file,PackSyncState state){string root=System.IO.Path.GetDirectoryName(file)!;if(state.LocalFiles!=null)state=state with{LocalFiles=state.LocalFiles.Select(f=>f with{WrittenUtcTicks=File.GetLastWriteTimeUtc(System.IO.Path.Combine(root,f.Path.Replace('/',System.IO.Path.DirectorySeparatorChar))).Ticks}).ToArray()};File.WriteAllText(file+".new",JsonSerializer.Serialize(state,PackJson));File.Move(file+".new",file,true);}
    internal static void CommitPack(string folder,string stage,string[] updated,string[] removed,PackSyncState state,Action<string> check,LoaderProfileUpdate? loader=null,Action? launcherCheck=null)
    {
        check(folder);PackUpdateStorage.ValidateStage(folder,stage);
        launcherCheck??=NeoForgeUpdate.RequireLauncherClosed;
        string metadata=File.ReadAllText(System.IO.Path.Combine(folder,"minecraftinstance.json"));
        if(loader!=null){launcherCheck();if(metadata!=loader.Before)throw new InvalidOperationException("The profile changed during update. Try again.");}
        var transaction=new PendingPackUpdate(stage,updated,removed,state,PackSyncPaths.HashFile(System.IO.Path.Combine(folder,"minecraftinstance.json")),loader?.After);
        ValidatePending(folder,transaction);
        string journal=System.IO.Path.Combine(folder,".harbor-sync-transaction.json");File.WriteAllText(journal,JsonSerializer.Serialize(transaction,PackJson));
        FinishUpdate(folder,transaction,check,launcherCheck);File.Delete(journal);PackUpdateStorage.CleanStage(stage);
    }
    internal static void Recover(string folder,Action<string> check,Action? launcherCheck=null)
    {
        string journal=System.IO.Path.Combine(folder,".harbor-sync-transaction.json");if(!File.Exists(journal))return;check(folder);
        string contents=File.ReadAllText(journal);using var record=JsonDocument.Parse(contents);
        if(record.RootElement.TryGetProperty("stage",out _))
        {
            var pending=JsonSerializer.Deserialize<PendingPackUpdate>(contents,PackJson)??throw new InvalidDataException("Invalid pending update.");
            ValidatePending(folder,pending);FinishUpdate(folder,pending,check,launcherCheck??NeoForgeUpdate.RequireLauncherClosed);File.Delete(journal);PackUpdateStorage.CleanStage(pending.Stage);return;
        }
        // Finish recovering transactions left by an older client; never create new backups.
        var transaction=JsonSerializer.Deserialize<PackSyncTransaction>(contents,PackJson)??throw new InvalidDataException("Invalid update recovery record.");if(transaction.LoaderBefore!=null)(launcherCheck??NeoForgeUpdate.RequireLauncherClosed)();Restore(folder,transaction);File.Delete(journal);
    }
    static void ValidatePending(string folder,PendingPackUpdate update)
    {
        PackUpdateStorage.ValidateStage(folder,update.Stage);
        if(update.Updated.Concat(update.Removed).Any(p=>!PackSyncPaths.Allowed(p))||update.State.LocalFiles==null||update.Updated.Any(p=>!update.State.LocalFiles.Any(f=>f.Path==p)))throw new InvalidDataException("Invalid pending mod update.");
    }
    static void FinishUpdate(string folder,PendingPackUpdate update,Action<string> check,Action launcherCheck)
    {
        string metadata=System.IO.Path.Combine(folder,"minecraftinstance.json");
        void CheckProfile(){check(folder);string current=File.ReadAllText(metadata);if(PackSyncPaths.HashFile(metadata)!=update.ProfileHash&&current!=update.LoaderAfter)throw new InvalidOperationException("The selected profile changed during update. Your saved update has not been applied to another profile.");}
        CheckProfile();if(update.LoaderAfter!=null)launcherCheck();
        foreach(string path in update.Updated)
        {
            CheckProfile();string source=PackSyncPaths.Resolve(update.Stage,path),target=PackSyncPaths.Resolve(folder,path);
            var expected=update.State.LocalFiles!.First(f=>f.Path==path);string candidate=File.Exists(source)?source:target;
            if(!File.Exists(candidate)||new FileInfo(candidate).Length!=expected.Size||PackSyncPaths.HashFile(candidate)!=expected.Hash)throw new InvalidDataException("A pending download failed verification: "+path);
            if(File.Exists(source))
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                // Publish beside the target so replacement is atomic even when the pack is on another disk.
                string temporary=target+".harbor-update-"+Guid.NewGuid().ToString("N")+".tmp";
                try{File.Copy(source,temporary);if(PackSyncPaths.HashFile(temporary)!=expected.Hash)throw new IOException("Temporary download failed verification.");CheckProfile();File.Move(temporary,target,true);}
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
        }
        foreach(string path in update.Removed){CheckProfile();File.Delete(PackSyncPaths.Resolve(folder,path));}
        CheckProfile();if(update.LoaderAfter!=null){launcherCheck();WriteLoaderMetadata(metadata,update.LoaderAfter);}
        SaveState(System.IO.Path.Combine(folder,".harbor-sync-state.json"),update.State);
    }
    static void WriteLoaderMetadata(string file,string contents){File.WriteAllText(file+".harbor-new",contents);File.Move(file+".harbor-new",file,true);}
    static void Restore(string folder,PackSyncTransaction t){string backupRoot=System.IO.Path.GetFullPath(System.IO.Path.Combine(folder,"harbor-sync-backups"))+System.IO.Path.DirectorySeparatorChar;if(!System.IO.Path.GetFullPath(t.Backup).StartsWith(backupRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid backup recovery location.");if(t.LoaderBefore!=null){string metadata=System.IO.Path.Combine(folder,"minecraftinstance.json");if(File.ReadAllText(metadata)==t.LoaderAfter)WriteLoaderMetadata(metadata,t.LoaderBefore);}foreach(string path in t.Files){string target=PackSyncPaths.Resolve(folder,path);if(t.Existed.Contains(path,StringComparer.OrdinalIgnoreCase)){string source=PackSyncPaths.Resolve(t.Backup,path);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);File.Copy(source,target,true);}else if(File.Exists(target))File.Delete(target);}}
}
