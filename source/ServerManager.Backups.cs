using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MinecraftHarbor;

public sealed partial class ServerManager
{
    readonly object backupReplyLock=new();
    readonly TimeSpan backupReplyTimeout;
    TaskCompletionSource<string>? backupReply;
    string[] backupReplies=Array.Empty<string>();
    public bool SavingNeedsResume { get; private set; }
    DateTime resumeRetryUtc;

    void ReceiveBackupReply(string line)
    {
        // Match dedicated-server replies, never player chat containing the same words.
        var match=Regex.Match(line,@"^\[[^\]]+\] \[Server thread/INFO\](?: \[(?:minecraft/(?:DedicatedServer|MinecraftServer)|net\.minecraft\.server\.(?:MinecraftServer|dedicated\.DedicatedServer)/?)\])?: (.+)$");
        if(!match.Success)return;
        var message=match.Groups[1].Value;
        lock(backupReplyLock){
            if(backupReply==null)return;
            if(backupReplies.Contains(message))backupReply.TrySetResult(message);
            else if(message.StartsWith("Unable to save the game",StringComparison.Ordinal))backupReply.TrySetException(new IOException(message));
        }
    }
    void CancelBackupReply(){lock(backupReplyLock)backupReply?.TrySetException(new IOException("The server exited before confirming its save."));}
    async Task SaveCommand(string command,params string[] replies)
    {
        var pending=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock(backupReplyLock){backupReply=pending;backupReplies=replies;}
        try{SendCore(command);await pending.Task.WaitAsync(backupReplyTimeout);}
        catch(TimeoutException){throw new TimeoutException("Minecraft did not confirm '"+command+"'. No completed backup was created; check Console.");}
        finally{lock(backupReplyLock){if(backupReply==pending){backupReply=null;backupReplies=Array.Empty<string>();}}}
    }
    async Task ResumeSaving()
    {
        try{
            await SaveCommand("save-on","Automatic saving is now enabled","Saving is already turned on");
            SavingNeedsResume=false;Log("Normal world saving is enabled.");
        }catch{
            resumeRetryUtc=utcNow().AddSeconds(10);
            Log("ATTENTION: normal saving was not confirmed. Harbor will retry save-on; check Console.");throw;
        }
    }
    public Task BackupAsync()=>Perform("Backing up your world…",()=>BackupCurrentWorld("manual"));
    public void SetAutomaticBackups(bool enabled)
    {
        if(Busy)throw new InvalidOperationException("Wait for the current operation to finish.");
        if(Profile.Deleted)throw new InvalidOperationException("Select a saved server first.");
        var value=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(Config))!;
        value.AutomaticBackups=enabled;value.BackupIntervalMinutes=60;value.BackupRetentionHours=5;
        WriteJson(Path.Combine(ProfileRoot,"settings.json"),value);Config=value;
        NextAutomaticBackupUtc=enabled&&State==ServerState.Running?utcNow().AddHours(1):null;
    }
    async Task BackupCurrentWorld(string reason)
    {
        if(!HasProcess){await BackupCore(reason);return;}
        if(State!=ServerState.Running)throw new InvalidOperationException("Wait until startup or shutdown finishes before backing up.");
        var stage=Path.Combine(ProfileRoot,"backup-staging",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);var created=utcNow();
        try{
            try{
                SavingNeedsResume=true;
                await SaveCommand("save-off","Automatic saving is now disabled","Saving is already turned off");
                Activity="Saving the world while you stay connected…";
                await SaveCommand("save-all flush","Saved the game");
                created=utcNow();Activity="Copying the saved world; server stays online…";
                await Task.Run(()=>CopyLiveWorld(stage));
                if(!HasProcess)throw new IOException("The server exited during the backup. The copy was not published.");
            }finally{if(HasProcess&&SavingNeedsResume)await ResumeSaving();}
            // Compression and archive verification run after normal saving is restored.
            await WriteBackup(stage,reason,created,"online");
        }finally{DeleteBackupStage(stage);}
    }
    public async Task<bool> TryAutomaticBackupAsync()
    {
        if(Busy||State!=ServerState.Running)return false;
        if(SavingNeedsResume){if(utcNow()>=resumeRetryUtc)await Perform("Resuming normal world saving…",ResumeSaving);return false;}
        if(!Config.AutomaticBackups||!NextAutomaticBackupUtc.HasValue||utcNow()<NextAutomaticBackupUtc.Value)return false;
        try{
            await Perform("Hourly backup: saving your world…",async()=>{
                Log("Hourly world backup started. The server stays online.");await BackupCurrentWorld("hourly");
                NextAutomaticBackupUtc=utcNow().AddMinutes(Config.BackupIntervalMinutes);
            });return true;
        }catch{if(HasProcess)NextAutomaticBackupUtc=utcNow().AddMinutes(5);throw;}
    }
    public Task CloseAsync()=>StopAsync();
    FileStream OpenWorldLock()=>new(Path.Combine(WorldDir,"session.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    Task<string> BackupCore(string reason)
    {
        if(HasProcess||OtherServerRunning())throw new InvalidOperationException("The world must be stopped before creating this backup.");
        return WriteBackup(WorldDir,reason,utcNow(),"stopped");
    }
    static Dictionary<string,(long Size,DateTime Modified)> WorldFiles(string root)
    {
        var result=new Dictionary<string,(long,DateTime)>(StringComparer.OrdinalIgnoreCase);
        void Visit(string folder){
            foreach(var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos()){
                if((entry.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("World backup cannot follow a linked file or folder: "+entry.Name);
                if(entry is DirectoryInfo)Visit(entry.FullName);
                else if(entry.Name!="session.lock"){var file=(FileInfo)entry;result.Add(Path.GetRelativePath(root,file.FullName),(file.Length,file.LastWriteTimeUtc));}
            }
        }
        Visit(root);return result;
    }
    void CopyLiveWorld(string stage)
    {
        if(!File.Exists(Path.Combine(WorldDir,"level.dat")))throw new IOException("No saved world to back up.");
        var files=WorldFiles(WorldDir);
        foreach(var item in files){
            var destination=Path.Combine(stage,item.Key);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source=new FileStream(Path.Combine(WorldDir,item.Key),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var target=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            source.CopyTo(target);
            if(target.Length!=item.Value.Size)throw new IOException("A world file changed during backup. The incomplete copy was discarded.");
        }
        // Some mods save independently of Minecraft. Reject a changing file set rather than
        // publish a known inconsistent copy. This cannot freeze arbitrary mod-owned storage.
        var after=WorldFiles(WorldDir);
        if(files.Count!=after.Count||files.Any(f=>!after.TryGetValue(f.Key,out var value)||value!=f.Value))
            throw new IOException("A mod or player save changed files during backup. Normal saving is restored; Harbor will retry later.");
    }
    void DeleteBackupStage(string stage)
    {
        var parent=Path.GetFullPath(Path.Combine(ProfileRoot,"backup-staging"))+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(stage).StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid temporary backup path.");
        try{if(Directory.Exists(stage))Directory.Delete(stage,true);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){Log("Could not remove temporary backup files: "+e.Message);}
    }
    async Task<string> WriteBackup(string source,string reason,DateTime created,string mode)
    {
        if(!File.Exists(Path.Combine(source,"level.dat")))throw new IOException("No saved world to back up.");
        Activity="Writing and checking the world backup…";
        Directory.CreateDirectory(BackupDir);
        var target=Path.Combine(BackupDir,DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff")+"_"+reason+".zip");
        await Task.Run(()=>{
            using var worldLock=mode=="stopped"?OpenWorldLock():null;
            try{
                using(var archive=ZipFile.Open(target+".partial",ZipArchiveMode.Create)){
                    foreach(var item in WorldFiles(source))archive.CreateEntryFromFile(Path.Combine(source,item.Key),"world/"+item.Key.Replace('\\','/'),CompressionLevel.Fastest);
                    foreach(var name in new[]{"server.properties","whitelist.json","ops.json"}){
                        var file=Path.Combine(ServerDir,name);if(File.Exists(file))archive.CreateEntryFromFile(file,"settings/"+name);
                    }
                    using var writer=new StreamWriter(archive.CreateEntry("harbor-backup.json").Open());
                    writer.Write(JsonSerializer.Serialize(new{world=Config.WorldName,pack=Profile.Name,version=Profile.PackVersion,profileId=Profile.Id,worldFolder=Config.WorldFolder,createdUtc=created,reason,mode}));
                }
                using(var check=ZipFile.OpenRead(target+".partial")){
                    if(check.GetEntry("world/level.dat")==null)throw new IOException("Backup is missing level.dat.");
                    foreach(var entry in check.Entries){using var stream=entry.Open();stream.CopyTo(Stream.Null);}
                }
                File.Move(target+".partial",target);
            }catch{Log("Backup did not finish. Any .partial file is not a restorable backup.");throw;}
        });
        Log("Backup saved: "+Path.GetFileName(target));PruneBackups();return target;
    }
    int PruneBackups()
    {
        var candidates=new List<(string File,DateTime Created)>();
        foreach(var file in Directory.EnumerateFiles(BackupDir,"*.zip")){
            if(Path.GetFileName(file).StartsWith("original-",StringComparison.OrdinalIgnoreCase))continue;
            try{
                using var zip=ZipFile.OpenRead(file);var meta=zip.GetEntry("harbor-backup.json");if(meta==null||meta.Length>65536)continue;
                using var reader=new StreamReader(meta.Open());using var json=JsonDocument.Parse(reader.ReadToEnd());var entry=json.RootElement;
                if(!entry.TryGetProperty("reason",out var r)||r.GetString() is not ("hourly" or "after-stop" or "after-close" or "before-restart" or "before-switch" or "before-restore" or "manual"))continue;
                if(Config.KeepManualBackups&&r.GetString()=="manual")continue;
                if(entry.TryGetProperty("profileId",out var profile)&&profile.GetString()!=Profile.Id)continue;
                if(entry.TryGetProperty("worldFolder",out var world)&&world.GetString()!=Config.WorldFolder)continue;
                if(!entry.TryGetProperty("createdUtc",out var created)||!created.TryGetDateTime(out var when))continue;
                candidates.Add((file,when.ToUniversalTime()));
            }catch(Exception e)when(e is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException or UnauthorizedAccessException){Log("Skipped unreadable backup during cleanup: "+Path.GetFileName(file));}
        }
        var cutoff=utcNow().AddHours(-Config.BackupRetentionHours);int removed=0;
        foreach(var old in candidates.OrderByDescending(c=>c.Created).ThenByDescending(c=>c.File).Skip(1).Where(c=>c.Created<cutoff)){
            try{File.Delete(old.File);removed++;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Log("Could not remove expired backup: "+ex.Message);}
        }
        if(removed>0)Log($"Removed {removed} expired backup(s). Keeping {Config.BackupRetentionHours} hours of history and at least the latest backup for this world.");return removed;
    }
}
