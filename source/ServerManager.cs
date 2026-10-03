using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MinecraftHarbor;

public sealed class Settings
{
    public static int MaxMemoryGB => SystemMemory.InstalledGB;
    public string WorldName { get; set; } = "New world";
    public string WorldFolder { get; set; } = "world";
    public string PackVersion { get; set; } = "8.1";
    public string NeoForgeVersion { get; set; } = "21.1.249";
    public int MemoryGB { get; set; } = 12;
    public int MaxPlayers { get; set; } = 8;
    public int ViewDistance { get; set; } = 8;
    public int SimulationDistance { get; set; } = 5;
    public bool KeepAwake { get; set; } = true;
    public bool AutomaticBackups { get; set; } = true;
    public int BackupIntervalMinutes { get; set; } = 60;
    public int BackupRetentionHours { get; set; } = 5;
    public bool KeepManualBackups { get; set; } = false;
    public string OwnerName { get; set; } = "";
    public string OwnerUuid { get; set; } = "";
}
public sealed record Player(string uuid, string name);
public sealed record ProcessRecord(int Pid, DateTime StartedUtc)
{
    public int? SupervisorPid {get;init;}
    public DateTime? SupervisorStartedUtc {get;init;}
    public int? OwnerPid {get;init;}
    public DateTime? OwnerStartedUtc {get;init;}
    public string? ProfileId {get;init;}
}
public enum ServerState { Stopped, Starting, Running, Stopping, Crashed }

public sealed partial class ServerManager : IDisposable
{
    public readonly string Root;
    public ServerLibrary Library { get; }
    public ServerProfile Profile=>Library.Active;
    public string ProfileRoot=>Library.ProfileRoot(Profile);
    public string ServerDir => Path.Combine(ProfileRoot, "server");
    public string BackupDir => Config.WorldFolder=="world"?Path.Combine(ProfileRoot,"backups"):Path.Combine(ProfileRoot,"backups",Config.WorldFolder);
    public string WorldDir => Path.Combine(ServerDir, Config.WorldFolder);
    public WorldEntry World=>Profile.Worlds.Single(w=>w.Folder==Config.WorldFolder);
    public Settings Config { get; private set; }
    public volatile ServerState State = ServerState.Stopped;
    public volatile bool Busy;
    public string Activity = "Ready when you are";
    public DateTime? StartedAt;
    public int OnlinePlayers;
    public string OnlineNames = "Nobody online";
    public DateTime? NextAutomaticBackupUtc { get; private set; }
    public readonly ConcurrentQueue<string> Lines = new();
    public bool HasProcess => child != null && !child.HasExited;
    public bool EulaAccepted => File.Exists(Path.Combine(ServerDir, "eula.txt")) && Regex.IsMatch(File.ReadAllText(Path.Combine(ServerDir, "eula.txt")), @"(?m)^eula=true\s*$");
    readonly SemaphoreSlim gate = new(1, 1);
    readonly object logLock = new();
    readonly Func<ProcessStartInfo>? testLauncher;
    readonly Func<DateTime> utcNow;
    Process? child;
    Task? exitObservation;
    bool expectedExit;
    bool expectsModernFix;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public ServerManager(string root, Func<ProcessStartInfo>? launcher = null,Func<DateTime>? clock=null,TimeSpan? replyTimeout=null)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); testLauncher = launcher;utcNow=clock??(()=>DateTime.UtcNow);backupReplyTimeout=replyTimeout??TimeSpan.FromMinutes(3);
        Library=new ServerLibrary(Root);
        Config = LoadSettings(Profile);
        Directory.CreateDirectory(BackupDir); Directory.CreateDirectory(Path.Combine(Root,"logs"));
        CheckConfig(Config);
    }
    Settings LoadSettings(ServerProfile p)
    {
        var file=Path.Combine(Library.ProfileRoot(p),"settings.json");
        var cfg=File.Exists(file)?JsonSerializer.Deserialize<Settings>(File.ReadAllText(file))??new():new Settings();CheckConfig(cfg);
        if(!p.Worlds.Any(w=>w.Folder==cfg.WorldFolder))throw new InvalidDataException("The selected world is not in this modpack's library.");return cfg;
    }
    public string SelectedWorldFolder(ServerProfile p)=>p.Id==Profile.Id?Config.WorldFolder:LoadSettings(p).WorldFolder;
    public static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path + ".new", JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
        File.Move(path + ".new", path, true);
    }
    public void Log(string text)
    {
        var line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
        Lines.Enqueue(line);
        lock(logLock)
        {
            var file = Path.Combine(Root,"logs", "harbor-"+DateTime.Now.ToString("yyyy-MM-dd")+".log");
            try { File.AppendAllText(file, line + Environment.NewLine); } catch(IOException) { }
        }
    }
    public static string LanAddress()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel);
        foreach(var n in adapters.OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork)))
            foreach(var a in n.GetIPProperties().UnicastAddresses)
                if(a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.") && !IPAddress.IsLoopback(a.Address)) return a.Address.ToString();
        return "127.0.0.1";
    }
    public bool OtherServerRunning()
    {
        var file=Path.Combine(Root,"server-process.json");
        if(!File.Exists(file))return false;
        try {
            var record=JsonSerializer.Deserialize<ProcessRecord>(File.ReadAllText(file))!;
            using var p=Process.GetProcessById(record.Pid);
            bool running=!p.HasExited && Math.Abs((p.StartTime.ToUniversalTime()-record.StartedUtc).TotalSeconds)<1;
            if(!running)ClearStaleProcessRecord(file,record);
            return running;
        } catch(ArgumentException){return false;} catch(InvalidOperationException){return false;}catch(FileNotFoundException){return false;}
        catch(Exception ex) { throw new IOException("Cannot verify the existing server process. Check server-process.json before starting another server.", ex); }
    }
    static void ClearStaleProcessRecord(string file,ProcessRecord record)
    {
        try{if(JsonSerializer.Deserialize<ProcessRecord>(File.ReadAllText(file))==record)File.Delete(file);}
        catch(IOException){}catch(JsonException){}
    }
    Process? MinecraftProcess()
    {
        if(!HasProcess)return null;
        try{
            var record=JsonSerializer.Deserialize<ProcessRecord>(File.ReadAllText(Path.Combine(Root,"server-process.json")));
            if(record==null||record.SupervisorPid!=child!.Id||!ServerSupervisor.IsSameProcess(record))return null;
            return Process.GetProcessById(record.Pid);
        }catch(IOException){return null;}catch(JsonException){return null;}catch(ArgumentException){return null;}
    }
    public long MemoryBytes { get { try { using var minecraft=MinecraftProcess();return minecraft?.WorkingSet64??0;}catch{return 0;} } }
    internal ProcessReading? ReadPerformance()
    {
        try{using var minecraft=MinecraftProcess();if(minecraft==null)return null;return new(minecraft.Id,minecraft.StartTime.ToUniversalTime(),minecraft.TotalProcessorTime.TotalMilliseconds,minecraft.WorkingSet64);}
        catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){return null;}
    }
    public void AcceptEula()
    {
        File.WriteAllText(Path.Combine(ServerDir,"eula.txt"), "# Accepted by the owner in Minecraft Harbor at " + DateTimeOffset.Now.ToString("O") + "\neula=true\n");
    }
    public Dictionary<string,string> Properties()
    {
        var result=new Dictionary<string,string>();
        var f=Path.Combine(ServerDir,"server.properties");
        if(File.Exists(f))foreach(var line in File.ReadAllLines(f)) { if(line.StartsWith('#')||!line.Contains('='))continue; var p=line.IndexOf('=');result[line[..p]]=line[(p+1)..]; }
        return result;
    }
    static void CheckConfig(Settings config)
    {
        ServerLibrary.ValidateWorldFolder(config.WorldFolder);
        if(config.BackupIntervalMinutes<5||config.BackupIntervalMinutes>1440||config.BackupRetentionHours<1||config.BackupRetentionHours>168)throw new ArgumentException("Backup settings are outside the supported range.");
        if(config.MemoryGB<1 || config.MemoryGB>1048576 || config.MaxPlayers<1 || config.MaxPlayers>50 || config.ViewDistance<2 || config.ViewDistance>32 || config.SimulationDistance<2 || config.SimulationDistance>32)
            throw new ArgumentException("Settings are outside the supported range.");
    }
    public void SaveSettings(Settings value)
    {
        if(Busy||HasProcess||OtherServerRunning())throw new InvalidOperationException("Stop the server before changing settings.");
        if(Profile.Deleted)throw new InvalidOperationException("Select a saved server first.");
        CheckConfig(value);SystemMemory.Validate(value.MemoryGB);if(!Profile.Worlds.Any(w=>w.Folder==value.WorldFolder))throw new InvalidDataException("World does not belong to this pack.");WriteJson(Path.Combine(ProfileRoot,"settings.json"), value);Config=value;ApplySettings();
    }
    public void InstallUltimine(string source)
    {
        if(Busy||HasProcess||OtherServerRunning())throw new InvalidOperationException("Stop the server before updating mods.");
        using(var jar=ZipFile.OpenRead(source)) {
            var metadata=jar.GetEntry("META-INF/neoforge.mods.toml")??jar.GetEntry("META-INF/mods.toml")??throw new InvalidDataException("This JAR has no NeoForge mod information.");
            using var reader=new StreamReader(metadata.Open());var toml=reader.ReadToEnd();
            if(!Regex.IsMatch(toml,@"(?m)^\s*modId\s*=\s*[""']ftbultimine[""']\s*$"))throw new InvalidDataException("Choose an FTB Ultimine JAR for NeoForge 1.21.1.");
        }
        var current=Directory.GetFiles(Path.Combine(ServerDir,"mods"),"ftb-ultimine*.jar");
        if(current.Length!=1)throw new InvalidOperationException("Expected one installed Ultimine build. Check the mods folder before updating.");
        var target=current[0];
        if(Path.GetFullPath(source).Equals(Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Choose the new build from outside the server mods folder.");
        if(!Profile.CustomUltimineSync)throw new InvalidOperationException("Custom Ultimine syncing is configured only for the original ATM10 server.");
        var recovery=Path.Combine(ProfileRoot,"mod-backups",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(recovery);
        File.Copy(target,Path.Combine(recovery,Path.GetFileName(target)));
        File.Copy(source,target+".new",true);File.Move(target+".new",target,true);
        Log("Custom Ultimine replaced. Previous JAR saved in "+recovery+". Start the server to publish the update.");
    }
    void ApplySettings()
    {
        var props=Properties();
        props["level-name"]=Config.WorldFolder; props.TryAdd("online-mode","true");props.TryAdd("white-list","true");props.TryAdd("enforce-whitelist","true");
        props["server-port"]="25565";props["server-ip"]=""; props["enable-rcon"]="false"; props["enable-query"]="false";
        props["motd"]=(Profile.DisplayName+" | "+Config.WorldName).Replace('\r',' ').Replace('\n',' ');props["max-players"]=Config.MaxPlayers.ToString();
        props["view-distance"]=Config.ViewDistance.ToString();props["simulation-distance"]=Config.SimulationDistance.ToString();
        props.TryAdd("allow-flight","true"); props.TryAdd("max-tick-time","180000");props.TryAdd("pause-when-empty-seconds","-1");
        File.WriteAllLines(Path.Combine(ServerDir,"server.properties"), props.OrderBy(p=>p.Key).Select(p=>p.Key+"="+p.Value), new UTF8Encoding(false));
        var argsPath=Path.Combine(ServerDir,"user_jvm_args.txt");
        var args=File.Exists(argsPath)?File.ReadAllLines(argsPath).Where(l=>!Regex.IsMatch(l.Trim(),@"^-Xm[sx]" )).ToList():new List<string>();
        args.Insert(0,"-Xmx"+Config.MemoryGB+"G");args.Insert(0,"-Xms4G");
        File.WriteAllLines(argsPath,args,new UTF8Encoding(false));
    }
    async Task Perform(string title,Func<Task> action)
    {
        if(!await gate.WaitAsync(0))throw new InvalidOperationException("Another operation is already in progress.");
        Busy=true;Activity=title;
        try { await action(); }
        finally {Busy=false;Activity=State switch {ServerState.Running=>"Your world is online",ServerState.Starting=>"Loading "+Profile.Name+" and your world…",ServerState.Stopping=>"Saving and stopping…",ServerState.Crashed=>"Server stopped unexpectedly — check Console",_=>"Ready when you are"};gate.Release();}
    }
    public Task StartAsync()=>Perform("Starting your world…",StartCore);
    async Task StartCore()
    {
        if(Profile.Deleted)throw new InvalidOperationException("Select a saved server first.");
        SystemMemory.Validate(Config.MemoryGB);
        if(HasProcess)throw new InvalidOperationException("The server is already running.");
        if(exitObservation!=null)await exitObservation;
        if(OtherServerRunning())throw new InvalidOperationException("A server from a previous Harbor session is still running. Do not start a second copy. See the recovery instructions in START-HERE.txt.");
        if(!EulaAccepted)throw new InvalidOperationException("Accept the Minecraft EULA before starting the server.");
        if(!File.Exists(Path.Combine(WorldDir,"level.dat"))&&!World.CanGenerate)throw new FileNotFoundException("The saved world is missing. Restore a backup first.");
        if(testLauncher==null && IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e=>e.Port==25565))throw new IOException("Port 25565 is already in use. Stop the other Minecraft server first.");
        Directory.CreateDirectory(WorldDir);using(var worldLock=OpenWorldLock()) { }
        ApplySettings();
        expectsModernFix=Directory.Exists(Path.Combine(ServerDir,"mods"))&&Directory.EnumerateFiles(Path.Combine(ServerDir,"mods"),"modernfix*.jar").Any();
        var start=testLauncher?.Invoke() ?? new ProcessStartInfo(Profile.JavaPath);
        start.WorkingDirectory=ServerDir;start.UseShellExecute=false;start.CreateNoWindow=true;start.RedirectStandardInput=true;start.RedirectStandardOutput=true;start.RedirectStandardError=true;
        start.StandardOutputEncoding=Encoding.UTF8;start.StandardErrorEncoding=Encoding.UTF8;
        if(testLauncher==null) {
            start.ArgumentList.Add("-Dfile.encoding=UTF-8");start.ArgumentList.Add("-Dterminal.jline=false");start.ArgumentList.Add("-Dterminal.ansi=false");
            var launch=Path.GetFullPath(Path.Combine(ServerDir,Profile.LaunchFile));
            if(!launch.StartsWith(ServerDir+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(launch))throw new IOException("This pack's server launcher is missing or invalid.");
            if(Profile.UsesArgumentFile){start.ArgumentList.Add("@user_jvm_args.txt");start.ArgumentList.Add("@"+Profile.LaunchFile);}
            else{start.ArgumentList.Add("-Xms4G");start.ArgumentList.Add("-Xmx"+Config.MemoryGB+"G");start.ArgumentList.Add("-jar");start.ArgumentList.Add(Profile.LaunchFile);}
            start.ArgumentList.Add("nogui");
        }
        var p=new Process {StartInfo=ServerSupervisor.Wrap(start,Root,Profile.Id),EnableRaisingEvents=true};
        p.OutputDataReceived+=(_,e)=>{if(e.Data!=null)ReadLine(e.Data);};p.ErrorDataReceived+=(_,e)=>{if(e.Data!=null)ReadLine(e.Data);};
        expectedExit=false;State=ServerState.Starting;ResetPlayerPresence();child=p;
        try {
            p.Start();StartedAt=DateTime.Now;
            NextAutomaticBackupUtc=utcNow().AddMinutes(Config.BackupIntervalMinutes);
            p.BeginOutputReadLine();p.BeginErrorReadLine();
            exitObservation=ObserveExit(p);Log("Starting "+Profile+" / "+Config.WorldName+" with "+Config.MemoryGB+" GB maximum memory. First startup can take several minutes.");
        } catch {
            State=ServerState.Crashed;child=null;StartedAt=null;NextAutomaticBackupUtc=null;
            // Closing the pipe also tells a supervisor that did start to stop its server.
            p.Dispose();
            try{File.Delete(p.StartInfo.ArgumentList[1]);}catch(IOException){}
            throw;
        }
        await Task.CompletedTask;
    }
    async Task ObserveExit(Process p)
    {
        await p.WaitForExitAsync();
        if(child!=p)return;
        CancelBackupReply();SavingNeedsResume=false;
        bool saved=expectedExit&&p.ExitCode==0;
        State=saved?ServerState.Stopped:ServerState.Crashed;StartedAt=null;ResetPlayerPresence(true);NextAutomaticBackupUtc=null;
        if(saved&&Profile.Loader=="vanilla"){
            string metadata=Path.Combine(WorldDir,"level.dat");
            try{if(File.Exists(metadata))new WorldMetadata(metadata).Save(metadata,Config.WorldName,new());}
            catch(Exception ex)when(ex is IOException or InvalidDataException){Log("World saved, but its display name could not be updated: "+ex.Message);}
        }
        Log(saved?"Server stopped and world saved.":"Server exited with code "+p.ExitCode+". A completed save is not confirmed. Open Console to inspect the cause.");
    }
    void ReadLine(string line)
    {
        line=Regex.Replace(line,@"\x1B\[[0-?]*[ -/]*[@-~]", "");if(ReceiveCommandHelp(line))return;Log(line);ReceiveBackupReply(line);ObserveInitialRules(line);
        bool done=Regex.IsMatch(line,@"Done \([\d.,]+s\)!");
        if(done)Activity="Finishing mod initialization…";
        // ATM10 continues loading quests and server hooks after Minecraft's early
        // Done line. ModernFix reports this only after those hooks complete.
        if((line.Contains("Dedicated server took")||done&&!expectsModernFix)&&State==ServerState.Starting){State=ServerState.Running;Activity="Your world is online";ApplyInitialRules();if(World.CanGenerate){World.CanGenerate=false;try{Library.Save();}catch(IOException ex){Log("Could not save world status: "+ex.Message);}}}
        ObservePlayerPresence(line);
    }
    public void Send(string command)
    {
        if(Busy&&Regex.IsMatch(command.Trim().TrimStart('/'),@"^(?:minecraft:)?save-(?:all|off|on)(?:\s|$)",RegexOptions.IgnoreCase))throw new InvalidOperationException("Wait for the current backup to finish before sending save commands.");
        SendCore(command);
    }
    void SendCore(string command)
    {
        command=command.Trim().TrimStart('/');
        if(!HasProcess)throw new InvalidOperationException("The server is stopped.");
        if(command.Length==0||command.Length>2048||command.Any(c=>c=='\n'||c=='\r'||c=='\0'))throw new ArgumentException("Enter one server command.");
        if(command.Equals("stop",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Use the Stop button to save and stop the server.");
        if(State!=ServerState.Running)throw new InvalidOperationException("Wait until the server is online.");
        lock(commandInputLock){child!.StandardInput.WriteLine(command);child.StandardInput.Flush();}Log("> "+command);
    }
    async Task StopCore()
    {
        if(!HasProcess)return;
        expectedExit=true;State=ServerState.Stopping;Activity="Saving and stopping…";
        var p=child!;p.StandardInput.WriteLine("stop");p.StandardInput.Flush();
        try {await p.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(4));if(exitObservation!=null)await exitObservation;}
        catch(TimeoutException){expectedExit=false;throw new TimeoutException("The server is still saving or stalled. It has not been force-closed. Check Console and try Stop again.");}
        State=ServerState.Stopped;
        if(p.ExitCode!=0)throw new IOException("The server exited with an error. Inspect Console before continuing.");
    }
    public Task StopAsync()=>HasProcess?Perform("Saving your world…",StopCore):Task.CompletedTask;
    public Task RestartAsync()=>Perform("Restarting safely…",async()=>{await StopCore();await StartCore();});
    public Task RestoreAsync(string file)=>Perform("Restoring your world…",async()=>{
        if(HasProcess||OtherServerRunning())throw new InvalidOperationException("Stop the server before restoring a backup.");
        file=Path.GetFullPath(file);
        if(!file.StartsWith(Path.GetFullPath(BackupDir)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(file)||Path.GetExtension(file)!=".zip")throw new IOException("Choose a ZIP from this server's backup folder.");
        var stage=Path.Combine(ServerDir,"restore-"+Guid.NewGuid().ToString("N"));
        await Task.Run(()=>{
            Directory.CreateDirectory(stage);
            using var zip=ZipFile.OpenRead(file);
            var metadata=zip.GetEntry("harbor-backup.json");
            if(metadata!=null){using var reader=new StreamReader(metadata.Open());using var json=JsonDocument.Parse(reader.ReadToEnd());if(json.RootElement.TryGetProperty("profileId",out var id)&&id.GetString()!=Profile.Id)throw new IOException("This backup belongs to a different modpack.");if(json.RootElement.TryGetProperty("worldFolder",out var folder)&&folder.GetString()!=Config.WorldFolder)throw new IOException("This backup belongs to a different world.");}
            bool nested=zip.GetEntry("world/level.dat")!=null;
            if(!nested&&zip.GetEntry("level.dat")==null)throw new IOException("This is not a world backup.");
            foreach(var entry in zip.Entries) {
                var name=entry.FullName.Replace('\\','/');
                if(nested){if(!name.StartsWith("world/"))continue;name=name[6..];}
                if(name.Length==0||name.EndsWith('/'))continue;
                var destination=Path.GetFullPath(Path.Combine(stage,name));
                if(!destination.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||name.Contains(':'))throw new IOException("Unsafe backup path rejected.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);entry.ExtractToFile(destination,false);
            }
        });
        await BackupCore("before-restore");
        var recovery=Path.Combine(ProfileRoot,"recovery",Config.WorldFolder+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
        using(var worldLock=OpenWorldLock()) { }
        Directory.Move(WorldDir,recovery);
        try {Directory.Move(stage,WorldDir);}catch{Directory.Move(recovery,WorldDir);throw;}
        Log("Restored "+Path.GetFileName(file)+". The previous world is also retained in recovery. Server remains stopped.");
    });
    public List<Player> Players()=>File.Exists(Path.Combine(ServerDir,"whitelist.json"))?JsonSerializer.Deserialize<List<Player>>(File.ReadAllText(Path.Combine(ServerDir,"whitelist.json")))??new():new();
    public Task SelectAsync(string profileId,string worldFolder)=>Perform("Switching server…",async()=>{
        var next=Library.Data.Profiles.Single(p=>p.Id==profileId&&!p.Deleted);var world=next.Worlds.Single(w=>w.Folder==worldFolder);
        if(next.Id==Profile.Id&&worldFolder==Config.WorldFolder)return;
        if(OtherServerRunning()&&!HasProcess)throw new InvalidOperationException("A previous Harbor server is still running. Stop it before switching.");
        if(HasProcess&&State!=ServerState.Running)throw new InvalidOperationException("Wait for startup or shutdown to finish before switching.");
        var cfg=LoadSettings(next);cfg.WorldFolder=world.Folder;cfg.WorldName=world.Name;
        if(HasProcess)await StopCore();
        if(File.Exists(Path.Combine(WorldDir,"level.dat")))await BackupCore("before-switch");
        WriteJson(Path.Combine(Library.ProfileRoot(next),"settings.json"),cfg);
        var previous=Library.Data.ActiveId;Library.Data.ActiveId=next.Id;
        try{Library.Save();}catch{Library.Data.ActiveId=previous;throw;}
        Config=cfg;Directory.CreateDirectory(BackupDir);State=ServerState.Stopped;OnlinePlayers=0;OnlineNames="Nobody online";StartedAt=null;
        Log("Selected "+next+" / "+world.Name+". Press Start server when ready.");
    });
    public Task CreateWorldAsync(string name)=>Perform("Creating a world slot…",()=>{
        RequireStopped();name=WorldName(name);var world=new WorldEntry{Folder="world-"+Guid.NewGuid().ToString("N"),Name=name,CanGenerate=true};
        Directory.CreateDirectory(Path.Combine(ServerDir,world.Folder));Profile.Worlds.Add(world);
        try{Library.Save();}catch{Profile.Worlds.Remove(world);throw;}
        Log("Added "+name+". Select it and start the server to generate the world.");return Task.CompletedTask;
    });
    static string WorldName(string name){name=name.Trim();if(name.Length==0||name.Length>90||name.Any(char.IsControl))throw new ArgumentException("Use a world name between 1 and 90 characters.");return name;}
    void RequireStopped(){if(HasProcess||OtherServerRunning())throw new InvalidOperationException("Stop and save the server first.");}
    public Task ImportWorldAsync(string source,string name)=>Perform("Copying your saved world…",async()=>{
        RequireStopped();name=WorldName(name);source=Path.GetFullPath(source);
        var info=WorldImport.Read(source);
        if(info.MinecraftVersion.Length>0&&info.MinecraftVersion!=Profile.MinecraftVersion)throw new InvalidDataException("This save uses Minecraft "+info.MinecraftVersion+"; the selected pack uses "+Profile.MinecraftVersion+". Choose its matching pack.");
        var owner=Directory.GetParent(source)?.Parent?.FullName;
        if(owner!=null&&File.Exists(Path.Combine(owner,"minecraftinstance.json"))){
            var cf=CurseForgeProfiles.Read(owner);
            if(cf.MinecraftVersion!=Profile.MinecraftVersion||cf.Loader!=Profile.Loader||cf.LoaderVersion!=Profile.LoaderVersion||cf.ProjectId>0&&Profile.ProjectId>0&&cf.ProjectId!=Profile.ProjectId||cf.PackVersion!=Profile.PackVersion)
                throw new InvalidDataException("This save belongs to a different CurseForge pack or release. Set up and select that pack before importing it.");
        }
        var world=new WorldEntry{Folder="world-"+Guid.NewGuid().ToString("N"),Name=name};
        var stage=Path.Combine(ProfileRoot,"imports",world.Folder);Directory.CreateDirectory(Path.GetDirectoryName(stage)!);
        await Task.Run(()=>WorldImport.Copy(source,stage,info,message=>Activity=message));
        Directory.Move(stage,Path.Combine(ServerDir,world.Folder));Profile.Worlds.Add(world);
        try{Library.Save();}catch{Profile.Worlds.Remove(world);throw;}
        Log("Imported "+name+" into "+Profile.Name+". The source save is unchanged; player inventory was carried over when present.");
    });
    public Task SetUpPackAsync(CurseForgeProfile source,CancellationToken token,string? zip=null,Action<SetupProgress>? progress=null)=>Perform("Preparing "+source.Name+"…",async()=>{
        RequireStopped();
        if(Library.Data.Profiles.Any(p=>p.CurseForgePath.Equals(source.Path,StringComparison.OrdinalIgnoreCase)&&p.PackVersion==source.PackVersion))throw new InvalidOperationException("This CurseForge pack is already set up. Select it under Ready servers.");
        var profile=await PackInstaller.PrepareAsync(Root,source,text=>Activity=text,token,zip,progress);
        Library.Data.Profiles.Add(profile);try{Library.Save();}catch{Library.Data.Profiles.Remove(profile);throw;}
        Log("Server ready: "+profile+". Select it under Ready servers. Its files and worlds are separate from your other packs.");
        progress?.Invoke(new(4,1,"Your server is ready.",true,profile.Worlds[0].CanGenerate));
    });
    public async Task AddPlayerAsync(string name)
    {
        if(Busy||HasProcess||OtherServerRunning())throw new InvalidOperationException("Stop the server to edit the allowlist.");
        if(!Regex.IsMatch(name,@"^[A-Za-z0-9_]{3,16}$"))throw new ArgumentException("Enter a valid Minecraft Java username (3–16 letters, numbers or underscores).");
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};
        var json=await http.GetStringAsync("https://api.mojang.com/users/profiles/minecraft/"+Uri.EscapeDataString(name));
        using var doc=JsonDocument.Parse(json);var id=Guid.Parse(doc.RootElement.GetProperty("id").GetString()!).ToString();
        var canonical=doc.RootElement.GetProperty("name").GetString()!;
        // Recheck after the network operation so a concurrent start cannot overwrite a live allowlist.
        if(Busy||HasProcess||OtherServerRunning())throw new InvalidOperationException("Stop the server to edit the allowlist.");
        var list=Players();if(list.Any(p=>p.uuid==id))return;list.Add(new(id,canonical));WriteJson(Path.Combine(ServerDir,"whitelist.json"),list);Log("Added "+canonical+" to the allowlist.");
    }
    public void RemovePlayer(Player player)
    {
        if(Busy||HasProcess||OtherServerRunning())throw new InvalidOperationException("Stop the server to edit the allowlist.");
        if(player.uuid==Config.OwnerUuid)throw new InvalidOperationException("The owner stays on the allowlist.");
        WriteJson(Path.Combine(ServerDir,"whitelist.json"),Players().Where(p=>p.uuid!=player.uuid).ToList());
    }
    public void Dispose(){if(!HasProcess)child?.Dispose();gate.Dispose();}
}



