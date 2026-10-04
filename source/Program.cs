using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MinecraftHarbor;
internal static class Program
{
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint processId);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint processId);
    [STAThread]
    static int Main(string[] args)
    {
        var root=args.Contains("--root")?Path.GetFullPath(args[Array.IndexOf(args,"--root")+1]):InstalledApp.DefaultRoot();
        try
        {
            if(args.Contains("--connector-test")){int i=Array.IndexOf(args,"--connector-test");AutoModpackTests.Run(args[i+1],args[i+2]);return 0;}
            if(args.Contains("--world-picker-test")){PackWorldTests.Run(args[Array.IndexOf(args,"--world-picker-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--world-picker-live-test")){int i=Array.IndexOf(args,"--world-picker-live-test");PackWorldTests.Live(args[i+1],args[i+2],args[i+3]);return 0;}
            if(args.Contains("--installed-pack-live-test")){int i=Array.IndexOf(args,"--installed-pack-live-test");InstalledPackTests.Live(args[i+1],args[i+2],args[i+3]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--fresh-install-test")){InstalledApp.VerifyFreshInstall(args[Array.IndexOf(args,"--fresh-install-test")+1]);return 0;}
            if(args.Contains("--pack-publish-test")){PackPublishTests.Run(args[Array.IndexOf(args,"--pack-publish-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--lan-test")){LanTests.Run(args[Array.IndexOf(args,"--lan-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--server-supervisor"))return ServerSupervisor.Run(args[Array.IndexOf(args,"--server-supervisor")+1]).GetAwaiter().GetResult();
            if(args.Contains("--lifetime-test")){LifetimeTests.Run(args[Array.IndexOf(args,"--lifetime-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--lifetime-owner")){LifetimeTests.Owner(args[Array.IndexOf(args,"--lifetime-owner")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--artwork-test")){ArtworkTests.Run(args[Array.IndexOf(args,"--artwork-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--commands-test")){CommandTests.Run(args[Array.IndexOf(args,"--commands-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--modrinth-test")){ModrinthTests.Run(args[Array.IndexOf(args,"--modrinth-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--modrinth-live-test")){ModrinthTests.Live(args[Array.IndexOf(args,"--modrinth-live-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--modrinth-live-pack")){int i=Array.IndexOf(args,"--modrinth-live-pack");ModrinthTests.LivePack(args[i+1],args[i+2]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--modded-test")){ModdedTests.Run(args[Array.IndexOf(args,"--modded-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--vanilla-test")){VanillaTests.Run(args[Array.IndexOf(args,"--vanilla-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--management-test")){ManagementTests.Run(args[Array.IndexOf(args,"--management-test")+1],args[Array.IndexOf(args,"--management-test")+2]);return 0;}
            if(args.Contains("--mock-server")) { MockServer();return 0; }
            if(args.Contains("--self-test")){SelfTest(args[Array.IndexOf(args,"--self-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--verify")){Verify(root).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--sync-test")){SyncTest(root,args[Array.IndexOf(args,"--sync-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--library-test")){LibraryTests.Run(args[Array.IndexOf(args,"--library-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--backup-test")){BackupTests.Run(args[Array.IndexOf(args,"--backup-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--players-test")){PlayerTests.Run(args[Array.IndexOf(args,"--players-test")+1]).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--player-image-test")){int i=Array.IndexOf(args,"--player-image-test");using var image=PlayerAvatars.Load(Path.GetFullPath(args[i+1]),args[i+2],CancellationToken.None).GetAwaiter().GetResult();WriteImageReport(args[i+3],image);return image==null?1:0;}
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if(args.Contains("--world-picker-ui-test")){PackWorldTests.Ui(args[Array.IndexOf(args,"--world-picker-ui-test")+1]);return 0;}
            if(args.Contains("--installed-pack-ui-test")){InstalledPackTests.Ui(args[Array.IndexOf(args,"--installed-pack-ui-test")+1]);return 0;}
            if(args.Contains("--settings-ui-test")){AppSettingsTests.Run(args[Array.IndexOf(args,"--settings-ui-test")+1]);return 0;}
            if(args.Contains("--settings-demo")){AppSettingsTests.Run(args[Array.IndexOf(args,"--settings-demo")+1],true);return 0;}
            if(args.Contains("--players-ui-test")){PlayerTests.Ui(args[Array.IndexOf(args,"--players-ui-test")+1]);return 0;}
            if(args.Contains("--players-demo")){PlayerTests.Ui(args[Array.IndexOf(args,"--players-demo")+1],true);return 0;}
            if(args.Contains("--backups-ui-test")){BackupUiTests.Run(args[Array.IndexOf(args,"--backups-ui-test")+1]);return 0;}
            if(args.Contains("--backups-demo")){BackupUiTests.Run(args[Array.IndexOf(args,"--backups-demo")+1],true);return 0;}
            if(args.Contains("--lifetime-window-test")){LifetimeTests.Window(args[Array.IndexOf(args,"--lifetime-window-test")+1]);return 0;}
            if(args.Contains("--console-demo")){CommandTests.ShowDemo(args[Array.IndexOf(args,"--console-demo")+1]);return 0;}
            if(args.Contains("--console-test")){ConsoleTests.Run(args[Array.IndexOf(args,"--console-test")+1]);return 0;}
            if(args.Contains("--design-test")){DesignTests.Run(args[Array.IndexOf(args,"--design-test")+1]);return 0;}
            var screenshot=args.Contains("--preview")?args[Array.IndexOf(args,"--preview")+1]:null;
            using var mutex=new Mutex(true,"Local\\MinecraftHarbor-ATM10",out var first);
            if(!first&&screenshot==null){
                try{using var pipe=new NamedPipeClientStream(".","MinecraftHarbor-Show",PipeDirection.Out);pipe.Connect(5000);if(GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var processId))AllowSetForegroundWindow(processId);using var writer=new StreamWriter(pipe){AutoFlush=true};writer.WriteLine("show");}
                catch(IOException){MessageBox.Show("Harbor could not reopen its window. Try the shortcut again in a moment.","Minecraft Harbor");}
                catch(TimeoutException){MessageBox.Show("Harbor is busy. Try opening it again in a moment.","Minecraft Harbor");}
                return 0;
            }
            InstalledApp.Prepare(root);using var manager=new ServerManager(root);
            using var form=new MainForm(manager,screenshot,args.Contains("--autostart"));
            Application.Run(form);return 0;
        }
        catch(Exception ex)
        {
            var text=ex.ToString();Directory.CreateDirectory(Path.Combine(root,"logs"));File.WriteAllText(Path.Combine(root,"logs","last-error.txt"),text);
            if(!args.Any(a=>a.StartsWith("--")))MessageBox.Show(ex.Message,"Minecraft Harbor");
            return 1;
        }
    }
    static void MockServer()
    {
        var properties=Path.Combine(Environment.CurrentDirectory,"server.properties");
        var folder=File.Exists(properties)?File.ReadLines(properties).FirstOrDefault(l=>l.StartsWith("level-name="))?[11..]??"world":"world";
        ServerLibrary.ValidateWorldFolder(folder);if(!File.Exists(Path.Combine(Environment.CurrentDirectory,folder,"level.dat")))LibraryTests.WriteWorld(Path.Combine(Environment.CurrentDirectory,folder));
        Console.WriteLine("Done (0.2s)! For help, type help");Console.WriteLine("Dedicated server took 0.3 seconds to load");Console.Out.Flush();
        string? line;while((line=Console.ReadLine())!=null){
            File.AppendAllText("mock-commands.log",line+Environment.NewLine);
            if(line=="stop"){File.WriteAllText("mock-saved-on-stop.txt","saved by normal shutdown");Console.WriteLine("Saving world");Console.Out.Flush();return;}
            if(line=="help")foreach(string usage in new[]{"/help [<command>]","/list","/save-all [flush]","/stop","/kick <targets> [<reason>]","/whitelist (add|remove|list|on|off|reload)","/gamemode (survival|creative|adventure|spectator)","/gamerule <rule>","/ftbchunks (claim|unclaim|admin)","/ftbultimine (toggle|shape)"})Console.WriteLine("[12:00:00] [Server thread/INFO] [minecraft/MinecraftServer]: "+usage);
            else if(line=="help whitelist")foreach(string usage in new[]{"/whitelist add <targets>","/whitelist remove <targets>","/whitelist list","/whitelist on","/whitelist off","/whitelist reload"})Console.WriteLine("[12:00:00] [Server thread/INFO]: "+usage);
            else if(line=="help gamerule")foreach(string usage in new[]{"/gamerule keepInventory [<value>]","/gamerule doDaylightCycle [<value>]"})Console.WriteLine("[12:00:00] [Server thread/INFO]: "+usage);
            else if(line.StartsWith("help "))Console.WriteLine("[12:00:00] [Server thread/INFO]: No command was found");
            else if(line=="list")Console.WriteLine("There are 1 of a max of 8 players online: TestOwner");
            else if(line=="save-off"){File.WriteAllText("mock-saving.txt","off");Console.WriteLine("[12:00:00] [Server thread/INFO] [minecraft/MinecraftServer]: Automatic saving is now disabled");}
            else if(line=="save-on"){
                if(!File.Exists("mock-reject-save-on")){File.WriteAllText("mock-saving.txt","on");Console.WriteLine("[12:00:00] [Server thread/INFO] [net.minecraft.server.MinecraftServer/]: Automatic saving is now enabled");}
            }
            else if(line=="save-all flush"){
                if(File.Exists("mock-reject-flush"))Console.WriteLine("[12:00:00] [Server thread/INFO] [minecraft/MinecraftServer]: <TestOwner> Saved the game");
                else{File.WriteAllText(Path.Combine(folder,"flushed-state.txt"),"written-by-confirmed-flush");Console.WriteLine("[12:00:00] [Server thread/INFO]: Saved the game");}
            }
            else if(!PlayerTests.MockCommand(line))Console.WriteLine("Command received: "+line);
            Console.Out.Flush();
        }
        if(File.Exists("mock-stay-after-input-eof"))Thread.Sleep(Timeout.Infinite);
    }
    static void WriteImageReport(string report,Bitmap? image)=>ServerManager.WriteJson(Path.GetFullPath(report),new{passed=image!=null,width=image?.Width,height=image?.Height,realWorldModified=false});
    static async Task Until(Func<bool> condition,int seconds=30)
    {
        var timer=Stopwatch.StartNew();while(!condition()){if(timer.Elapsed.TotalSeconds>seconds)throw new TimeoutException("Test wait expired.");await Task.Delay(100);}
    }
    static async Task SelfTest(string report)
    {
        var result=new List<string>();
        var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"self-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"server","world"));File.WriteAllText(Path.Combine(root,"server","world","level.dat"),"fixture-world");
        File.WriteAllText(Path.Combine(root,"server","world","player.txt"),"inventory-original");
        ProcessStartInfo Launcher(){var p=new ProcessStartInfo(Environment.ProcessPath!);p.ArgumentList.Add("--mock-server");return p;}
        using var s=new ServerManager(root,Launcher);
        try {
            bool rejected=false;try{await s.StartAsync();}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("EULA guard failed");result.Add("EULA guard passes");
            File.WriteAllText(Path.Combine(s.ServerDir,"eula.txt"),"eula=true\n");
            await s.StartAsync();await Until(()=>s.State==ServerState.Running);result.Add("Process starts and readiness is detected");
            rejected=false;try{await s.StartAsync();}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Duplicate server was allowed");result.Add("Duplicate start is blocked");
            rejected=false;try{s.SaveSettings(s.Config);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Live settings were allowed");result.Add("Live settings mutations blocked");
            s.Send("list");await Until(()=>s.OnlinePlayers==1);result.Add("Player count parsed from server output");
            var processBefore=File.ReadAllText(Path.Combine(root,"server-process.json"));await s.BackupAsync();
            if(s.State!=ServerState.Running||File.ReadAllText(Path.Combine(root,"server-process.json"))!=processBefore)throw new Exception("Online backup interrupted the server");result.Add("Online backup flushes, copies and verifies without stopping or restarting");
            await s.StopAsync();await Until(()=>!s.HasProcess);result.Add("Graceful stop and post-stop backup pass");
            var zip=Directory.GetFiles(s.BackupDir,"*.zip").First();
            using(var archive=ZipFile.OpenRead(zip)){using var r=new StreamReader(archive.GetEntry("world/player.txt")!.Open());if(r.ReadToEnd()!="inventory-original")throw new Exception("Backup content differs");}result.Add("Archived inventory content verified");
            File.WriteAllText(Path.Combine(s.WorldDir,"player.txt"),"inventory-changed");await s.RestoreAsync(zip);
            if(File.ReadAllText(Path.Combine(s.WorldDir,"player.txt"))!="inventory-original")throw new Exception("Restore failed");
            if(!Directory.GetFiles(Path.Combine(root,"recovery"),"player.txt",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p)=="inventory-changed"))throw new Exception("Recovery copy missing");result.Add("Restore recovers inventory and preserves replaced world");
            var evil=Path.Combine(s.BackupDir,"unsafe.zip");using(var a=ZipFile.Open(evil,ZipArchiveMode.Create)){a.CreateEntry("world/level.dat");using var w=new StreamWriter(a.CreateEntry("world/../../escape.txt").Open());w.Write("bad");}
            rejected=false;try{await s.RestoreAsync(evil);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("Unsafe archive accepted");result.Add("Archive path traversal rejected");
            if(s.State!=ServerState.Stopped)throw new Exception("Server did not stay stopped");
            Directory.CreateDirectory(Path.Combine(s.ServerDir,"mods"));
            var oldMod=Path.Combine(s.ServerDir,"mods","ftb-ultimine-fixture.jar");File.WriteAllText(oldMod,"old-build");
            var newMod=Path.Combine(root,"new.jar");using(var a=ZipFile.Open(newMod,ZipArchiveMode.Create)){using var w=new StreamWriter(a.CreateEntry("META-INF/neoforge.mods.toml").Open());w.Write("[[mods]]\nmodId=\"ftbultimine\"\nversion=\"test\"\n");}
            s.InstallUltimine(newMod);
            if(!File.ReadAllBytes(oldMod).SequenceEqual(File.ReadAllBytes(newMod)))throw new Exception("Ultimine install differs");
            if(!Directory.GetFiles(Path.Combine(root,"mod-backups"),"*.jar",SearchOption.AllDirectories).Any(f=>File.ReadAllText(f)=="old-build"))throw new Exception("Mod backup missing");
            rejected=false;try{s.InstallUltimine(evil);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("Non-mod was accepted");
            if(!File.ReadAllBytes(oldMod).SequenceEqual(File.ReadAllBytes(newMod)))throw new Exception("Invalid update changed current build");
            result.Add("Ultimine update validates mod identity, preserves old build and rejects invalid JAR without replacing it");
            File.WriteAllText(report,JsonSerializer.Serialize(new{passed=true,checks=result},new JsonSerializerOptions{WriteIndented=true}));
        }finally{if(s.HasProcess)await s.StopAsync();}
    }
    static async Task SyncTest(string root,string report)
    {
        var checks=new List<string>();using var setup=new ClientSetup(root);
        var helper=setup.Helper();
        if(!helper.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"client-setup",ClientSetup.HelperName))))throw new Exception("Helper differs");
        var installer=setup.Installer();
        checks.Add("Checksum-verified installer and helper are offered for the existing profile");
        if(ClientSetup.IsLocalPeer(System.Net.IPAddress.Parse("8.8.8.8")))throw new Exception("Public source allowed");
        if(!ClientSetup.IsLocalPeer(System.Net.IPAddress.Loopback))throw new Exception("Loopback blocked");checks.Add("Setup listener rejects non-local source addresses");
        setup.Start();if(!setup.Status.StartsWith("Available"))throw new Exception(setup.Status);
        using var http=new HttpClient(new HttpClientHandler{UseProxy=false});
        var home=await http.GetStringAsync("http://127.0.0.1:25566/");if(!home.Contains("Download Windows installer")||!home.Contains("Enable sync")||home.Contains("→ Import"))throw new Exception("Existing-profile installer instructions missing");
        var installerServed=await http.GetByteArrayAsync("http://127.0.0.1:25566/"+ClientSetup.InstallerName);if(!installerServed.SequenceEqual(installer))throw new Exception("HTTP installer differs");
        using var installerHead=await http.SendAsync(new HttpRequestMessage(HttpMethod.Head,"http://127.0.0.1:25566/"+ClientSetup.InstallerName));if(installerHead.Content.Headers.ContentLength!=installer.Length||(await installerHead.Content.ReadAsByteArrayAsync()).Length!=0)throw new Exception("Installer HEAD invalid");
        var served=await http.GetByteArrayAsync("http://127.0.0.1:25566/"+ClientSetup.HelperName);if(!served.SequenceEqual(helper))throw new Exception("HTTP helper differs");
        using var retired=await http.GetAsync("http://127.0.0.1:25566/"+ClientSetup.RetiredPackageName);if((int)retired.StatusCode!=410)throw new Exception("Old new-profile ZIP remains available");
        using var missing=await http.GetAsync("http://127.0.0.1:25566/server/world/level.dat");if((int)missing.StatusCode!=404)throw new Exception("Arbitrary file route exposed");
        using var post=await http.PostAsync("http://127.0.0.1:25566/",new StringContent("test"));if((int)post.StatusCode!=405)throw new Exception("POST accepted");
        using var head=await http.SendAsync(new HttpRequestMessage(HttpMethod.Head,"http://127.0.0.1:25566/"+ClientSetup.HelperName));if(head.Content.Headers.ContentLength!=helper.Length||(await head.Content.ReadAsByteArrayAsync()).Length!=0)throw new Exception("HEAD invalid");
        var lan=await http.GetStringAsync(setup.Url);if(!lan.Contains("New world"))throw new Exception("LAN endpoint failed");
        checks.Add("Installer and helper transfers and HEAD work; retired profile ZIP, arbitrary paths and writes blocked");
        ServerManager.WriteJson(report,new{passed=true,checkedUtc=DateTime.UtcNow,checks,desktopJoinVerified=false});
    }
    public static byte[] VarInt(int value)
    {
        var b=new List<byte>();do{byte part=(byte)(value&127);value>>=7;if(value!=0)part|=128;b.Add(part);}while(value!=0);return b.ToArray();
    }
    static async Task<int> ReadVarInt(Stream stream,CancellationToken token)
    {
        int value=0;byte[] one=new byte[1];for(int i=0;i<5;i++){await stream.ReadExactlyAsync(one,token);value|=(one[0]&127)<<(7*i);if((one[0]&128)==0)return value;}throw new IOException("Invalid server response.");
    }
    static async Task<string> Ping()
    {
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var client=new TcpClient();await client.ConnectAsync("127.0.0.1",25565,cancel.Token);
        var stream=client.GetStream();var host=Encoding.UTF8.GetBytes("localhost");
        var handshake=new List<byte>();handshake.Add(0);handshake.AddRange(VarInt(767));handshake.AddRange(VarInt(host.Length));handshake.AddRange(host);handshake.AddRange(new byte[]{0x63,0xdd,1});
        await stream.WriteAsync(VarInt(handshake.Count).Concat(handshake).ToArray(),cancel.Token);await stream.WriteAsync(new byte[]{1,0},cancel.Token);
        int length=await ReadVarInt(stream,cancel.Token);if(length<1||length>8_000_000)throw new IOException("Unexpected packet size");
        int packet=await ReadVarInt(stream,cancel.Token);if(packet!=0)throw new IOException("Invalid status packet");
        int textLen=await ReadVarInt(stream,cancel.Token);if(textLen<1||textLen>8_000_000)throw new IOException("Invalid status length");
        var data=new byte[textLen];await stream.ReadExactlyAsync(data,cancel.Token);return Encoding.UTF8.GetString(data);
    }
    static async Task Verify(string root)
    {
        using var mutex=new Mutex(true,"Local\\MinecraftHarbor-ATM10",out var first);if(!first)throw new Exception("Close Harbor before running verification.");
        using var s=new ServerManager(root);var checks=new List<string>();
        try{
            await s.StartAsync();
            await Until(()=>s.State==ServerState.Running||s.State==ServerState.Crashed,600);
            if(s.State!=ServerState.Running)throw new Exception("Actual server failed startup; inspect logs.");
            checks.Add("Actual ATM10 server loaded the imported world");
            string? status=null;Exception? last=null;
            for(int attempt=0;attempt<12&&status==null;attempt++) {
                try{status=await Ping();}catch(Exception ex) when(ex is IOException or SocketException or OperationCanceledException){last=ex;await Task.Delay(1500);}
            }
            if(status==null)throw new IOException("Minecraft status did not respond after startup.",last);
            using var doc=JsonDocument.Parse(status);
            checks.Add("Minecraft protocol status handshake successful");
            s.Send("list");s.Send("save-all flush");await Task.Delay(2000);
            await s.StopAsync();checks.Add("Graceful shutdown completed and world backup verified");
            ServerManager.WriteJson(Path.Combine(root,"verification.json"),new{passed=true,checkedUtc=DateTime.UtcNow,checks,status=doc.RootElement.Clone(),lanAddress=ServerManager.LanAddress()+":25565",remoteClientJoinVerified=false});
        }finally{if(s.HasProcess)await s.StopAsync();}
    }
}

