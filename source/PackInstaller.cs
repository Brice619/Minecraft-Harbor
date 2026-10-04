using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MinecraftHarbor;

public static class PackInstaller
{
    static readonly HttpClient Http=new(){Timeout=Timeout.InfiniteTimeSpan};
    public static async Task<ServerProfile> PrepareAsync(string root,CurseForgeProfile source,Action<string> progress,CancellationToken token,string? serverZip=null,Action<SetupProgress>? setup=null,PreparedPackSource? prepared=null)
    {
        if(source.Loader is not ("neoforge" or "forge" or "fabric"))throw new InvalidDataException("Harbor currently supports NeoForge, Forge and Fabric server packs.");
        if(!Regex.IsMatch(source.LoaderVersion,@"^[a-zA-Z0-9.\-+_]+$")||!Regex.IsMatch(source.MinecraftVersion,@"^[a-zA-Z0-9.\-+_]+$"))throw new InvalidDataException("Invalid game or loader version in the CurseForge profile.");
        var profile=new ServerProfile{Name=source.Name,PackVersion=source.PackVersion,MinecraftVersion=source.MinecraftVersion,Loader=source.Loader,LoaderVersion=source.LoaderVersion,CurseForgePath=source.Path,ProjectId=source.ProjectId,CurseForgeClientFileId=source.ClientFileId,ServerFileId=source.ServerFileId};
        var stage=Path.Combine(root,"profiles",".preparing-"+profile.Id);Directory.CreateDirectory(stage);
        try {
            setup?.Invoke(new(0,0,"Downloading and preparing "+source.Name+"…",WorldDeferred:true));
            var server=Path.Combine(stage,"server");
            string template="",receipt="";
            if(prepared!=null){
                if(!InstalledPackSelection.Matches(prepared.Profile,source))throw new InvalidDataException("The prepared server does not match this pack release.");
                progress("Using the prepared server files for this pack…");
                await Task.Run(()=>InstalledPackSelection.CopyPack(prepared,server,token),token);
                profile.LaunchFile=prepared.Profile.LaunchFile;profile.UsesArgumentFile=prepared.Profile.UsesArgumentFile;
            }else{
            string archive=serverZip??await DownloadServerPack(root,source,progress,token);
            using var archiveStream=File.OpenRead(archive);string archiveHash=Convert.ToHexString(await SHA256.HashDataAsync(archiveStream,token));
            template=Path.Combine(root,"pack-cache",source.ProjectId+"-"+source.ServerFileId+"-"+archiveHash+"-"+source.Loader+"-"+source.LoaderVersion);
            receipt=Path.Combine(template,"template.json");
            if(File.Exists(receipt)){
                var saved=JsonSerializer.Deserialize<ServerProfile>(File.ReadAllText(receipt))!;
                if(saved.Loader!=source.Loader||saved.LoaderVersion!=source.LoaderVersion||saved.MinecraftVersion!=source.MinecraftVersion)throw new InvalidDataException("The cached pack does not match this release.");
                progress("Using the prepared server files for this pack…");await Task.Run(()=>CopyTree(Path.Combine(template,"server"),server,token),token);
                profile.LaunchFile=saved.LaunchFile;profile.UsesArgumentFile=saved.UsesArgumentFile;
            }else{
                progress("Unpacking the server files…");var extracted=Path.Combine(stage,"unpacked");await Task.Run(()=>Extract(archive,extracted,token),token);
                string packRoot=FindPackRoot(extracted);await SetupFiles.FinishDirectory(packRoot,server,token);
            }
            }
            if(!Directory.Exists(Path.Combine(server,"mods"))||!Directory.EnumerateFiles(Path.Combine(server,"mods"),"*.jar").Any())throw new InvalidDataException("This download does not contain a ready server mod list. Choose the pack author's server ZIP, not a client export or downloader-only package.");
            // The version comes from the chosen installed client; a conflicting server script is rejected.
            ValidateScriptVersions(server,source);
            profile.JavaPath=await EnsureJava(root,JavaMajor(source.MinecraftVersion),progress,token);
            if(prepared==null&&!File.Exists(receipt)){
                await PrepareLoader(server,profile,progress,token);
                string cacheStage=template+".building-"+Guid.NewGuid().ToString("N");await Task.Run(()=>CopyTree(server,Path.Combine(cacheStage,"server"),token),token);
                ServerManager.WriteJson(Path.Combine(cacheStage,"template.json"),profile);Directory.CreateDirectory(Path.GetDirectoryName(template)!);await SetupFiles.FinishDirectory(cacheStage,template,token);
            }
            var world=new WorldEntry{Folder="world",Name="New world",CanGenerate=true};
            if(File.Exists(Path.Combine(server,"world","level.dat"))){var info=WorldImport.Read(Path.Combine(server,"world"));world.Name=info.Name;world.CanGenerate=false;}
            setup?.Invoke(new(1,0,world.CanGenerate?"Preparing the world for its first start…":"Checking the included world…",WorldDeferred:world.CanGenerate));
            profile.Worlds.Add(world);
            setup?.Invoke(new(2,0,"Applying this modpack's server settings…",WorldDeferred:world.CanGenerate));
            // New servers inherit the owner's existing agreement and allowlist, but have independent state.
            foreach(var name in new[]{"eula.txt","whitelist.json","ops.json"}){
                var existing=Path.Combine(root,"server",name);if(File.Exists(existing))File.Copy(existing,Path.Combine(server,name),true);
            }
            var inherited=File.Exists(Path.Combine(root,"settings.json"))?JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(root,"settings.json")))??new():new Settings();
            inherited.WorldName=world.Name;inherited.WorldFolder=world.Folder;inherited.PackVersion=source.PackVersion;inherited.NeoForgeVersion=source.LoaderVersion;
            ServerManager.WriteJson(Path.Combine(stage,"settings.json"),inherited);
            setup?.Invoke(new(3,1,"Java and the mod loader are ready.",WorldDeferred:world.CanGenerate));
            Directory.CreateDirectory(Path.Combine(stage,"backups"));
            setup?.Invoke(new(4,0,"Finalizing the server folder…",WorldDeferred:world.CanGenerate));await SetupFiles.FinishDirectory(stage,Path.Combine(root,"profiles",profile.Id),token);return profile;
        } catch {
            // Failed preparation is isolated. Keep it for diagnosis; it is never made active.
            progress("Pack preparation did not finish. Your current server is unchanged.");throw;
        }
    }
    internal static void CopyTree(string source,string target,CancellationToken token)
    {
        Directory.CreateDirectory(target);foreach(var file in Directory.EnumerateFiles(source)){token.ThrowIfCancellationRequested();if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked pack files are not supported.");File.Copy(file,Path.Combine(target,Path.GetFileName(file)),false);}
        foreach(var dir in Directory.EnumerateDirectories(source)){token.ThrowIfCancellationRequested();if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked pack folders are not supported.");CopyTree(dir,Path.Combine(target,Path.GetFileName(dir)),token);}
    }
    static void ValidateScriptVersions(string server,CurseForgeProfile source)
    {
        foreach(var script in Directory.EnumerateFiles(server,"*.bat")){
            var content=File.ReadAllText(script);
            var pattern=source.Loader=="neoforge"?@"(?im)^\s*set\s+NEOFORGE_VERSION=([\w.\-]+)":@"(?im)^\s*set\s+FORGE_VERSION=([\w.\-]+)";
            var match=Regex.Match(content,pattern);
            if(match.Success&&match.Groups[1].Value!=source.LoaderVersion&&match.Groups[1].Value!=source.MinecraftVersion+"-"+source.LoaderVersion)
                throw new InvalidDataException("The server ZIP's loader version differs from this installed CurseForge profile. Choose the matching server release.");
        }
    }
    static string FindPackRoot(string root)
    {
        if(Directory.Exists(Path.Combine(root,"mods")))return root;
        var children=Directory.GetDirectories(root);var candidates=children.Where(d=>Directory.Exists(Path.Combine(d,"mods"))).ToArray();
        if(candidates.Length==1&&!Path.GetFileName(candidates[0]).Equals("overrides",StringComparison.OrdinalIgnoreCase))return candidates[0];
        throw new InvalidDataException("Could not find the server's mods folder in this ZIP. Download the matching server pack from CurseForge.");
    }
    internal static async Task<string> DownloadServerPack(string root,CurseForgeProfile source,Action<string> progress,CancellationToken token)
    {
        if(source.ProjectId<=0||source.ServerFileId<=0)throw new InvalidDataException("This installed profile has no linked CurseForge server pack. Use Choose server ZIP to supply its matching server files.");
        var catalog=new CurseForgeCatalog(root);
        string? existing=InstalledPackSelection.FindArchive(root,source);if(existing!=null){using var check=ZipFile.OpenRead(existing);progress("Using your downloaded server pack…");return existing;}
        if(!catalog.Connected)throw new InvalidOperationException("Connect CurseForge for automatic downloads, or choose the matching server ZIP downloaded from its website.");
        progress("Finding this pack's matching CurseForge server download…");var file=await catalog.GetFile(source.ProjectId,source.ServerFileId,token);if(!file.Name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The linked server pack is not a ZIP archive.");return await catalog.Download(file,token,progress);
    }
    public static async Task Download(string url,string target,long? expectedSize,Action<string> progress,CancellationToken token,string? sha256=null,Action<double>? fraction=null)
    {
        var uri=new Uri(url);if(uri.Scheme!="https")throw new InvalidDataException("Downloads must use HTTPS.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var response=await Http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
        long? size=expectedSize??response.Content.Headers.ContentLength;
        if(size.HasValue&&new DriveInfo(Path.GetPathRoot(target)!).AvailableFreeSpace<size.Value+512L*1024*1024)throw new IOException("Not enough free space for this download.");
        using(var input=await response.Content.ReadAsStreamAsync(token))using(var output=File.Create(target+".partial")){
            byte[] buffer=new byte[131072];long count=0;var timer=Stopwatch.StartNew();int read;
            while((read=await input.ReadAsync(buffer,token))>0){await output.WriteAsync(buffer.AsMemory(0,read),token);count+=read;if(size>0)fraction?.Invoke(Math.Min(1,count/(double)size.Value));if(count>20L*1024*1024*1024)throw new IOException("Download is too large.");if(timer.ElapsedMilliseconds>700){progress($"Downloading: {count/1048576d:N0} MB"+(size.HasValue?$" / {size/1048576d:N0} MB":"")+"…");timer.Restart();}}
            if(size.HasValue&&count!=size.Value)throw new IOException("The download was incomplete. Try again.");
        }
        if(sha256!=null){using var input=File.OpenRead(target+".partial");if(!Convert.ToHexString(await SHA256.HashDataAsync(input,token)).Equals(sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The download checksum does not match.");}
        File.Move(target+".partial",target,true);
    }
    public static void Extract(string archive,string destination,CancellationToken token)
    {
        destination=Path.GetFullPath(destination);Directory.CreateDirectory(destination);using var zip=ZipFile.OpenRead(archive);
        long total=0;foreach(var entry in zip.Entries){total=checked(total+entry.Length);if(total>64L*1024*1024*1024)throw new InvalidDataException("Server ZIP is too large.");}
        if(new DriveInfo(Path.GetPathRoot(destination)!).AvailableFreeSpace<total+512L*1024*1024)throw new IOException("Not enough free space to unpack this server.");
        foreach(var entry in zip.Entries){
            token.ThrowIfCancellationRequested();var name=entry.FullName.Replace('\\','/');var path=Path.GetFullPath(Path.Combine(destination,name));
            if(name.Contains(':')||!path.StartsWith(destination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("An unsafe path or link was found in the ZIP.");
            if(name.EndsWith('/')){Directory.CreateDirectory(path);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path,false);
        }
    }
    public static int JavaMajor(string minecraft)
    {
        var parts=minecraft.Split('.');if(!int.TryParse(parts[0],out var major))throw new InvalidDataException("Unsupported Minecraft version.");
        if(major>=26)return 25;
        if(major!=1||parts.Length<2||!int.TryParse(parts[1],out var minor))throw new InvalidDataException("Unsupported Minecraft version.");
        int patch=parts.Length>2&&int.TryParse(parts[2],out var p)?p:0;
        return minor>=21||minor==20&&patch>=5?21:minor>=18?17:minor==17?16:8;
    }
    internal static async Task<string> EnsureJava(string root,int major,Action<string> progress,CancellationToken token)
    {
        var bundled=Path.Combine(root,"runtime","bin","java.exe");if(major==21&&File.Exists(bundled))return bundled;
        var cache=Path.Combine(root,"runtimes","java-"+major);var existing=Directory.Exists(cache)?Directory.EnumerateFiles(cache,"java.exe",SearchOption.AllDirectories).FirstOrDefault():null;if(existing!=null)return existing;
        progress($"Preparing Java {major} for this pack…");
        var jsonText=await Http.GetStringAsync($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse",token);
        using var json=JsonDocument.Parse(jsonText);if(json.RootElement.GetArrayLength()==0)throw new InvalidDataException($"Java {major} is unavailable from Adoptium.");
        var pkg=json.RootElement[0].GetProperty("binary").GetProperty("package");string url=pkg.GetProperty("link").GetString()!;string checksum=pkg.GetProperty("checksum").GetString()!;
        var archive=Path.Combine(root,"downloads","java-"+major+".zip");await Download(url,archive,pkg.GetProperty("size").GetInt64(),progress,token,checksum);
        var stage=cache+"-"+Guid.NewGuid().ToString("N");await Task.Run(()=>Extract(archive,stage,token),token);
        var java=Directory.EnumerateFiles(stage,"java.exe",SearchOption.AllDirectories).SingleOrDefault()??throw new IOException("Java archive has no launcher.");var relative=Path.GetRelativePath(stage,java);
        // Windows scanners can briefly hold newly extracted executable files open.
        for(int attempt=0;;attempt++){try{Directory.Move(stage,cache);break;}catch(IOException)when(attempt<8){await Task.Delay(350*(attempt+1),token);}}
        return Path.Combine(cache,relative);
    }
    static async Task PrepareLoader(string server,ServerProfile profile,Action<string> progress,CancellationToken token)
    {
        var version=profile.LoaderVersion;var minecraft=profile.MinecraftVersion;
        if(profile.Loader=="fabric"){
            using var versions=JsonDocument.Parse(await Http.GetStringAsync("https://meta.fabricmc.net/v2/versions/installer",token));var installer=versions.RootElement.EnumerateArray().First(e=>e.GetProperty("stable").GetBoolean()).GetProperty("version").GetString()!;
            profile.LaunchFile="harbor-fabric-server.jar";profile.UsesArgumentFile=false;
            await Download($"https://meta.fabricmc.net/v2/versions/loader/{minecraft}/{version}/{installer}/server/jar",Path.Combine(server,profile.LaunchFile),null,progress,token);return;
        }
        string coordinate=profile.Loader=="forge"?(version.StartsWith(minecraft+"-")?version:minecraft+"-"+version):version;
        profile.LaunchFile=profile.Loader=="neoforge"?$"libraries/net/neoforged/neoforge/{version}/win_args.txt":$"libraries/net/minecraftforge/forge/{coordinate}/win_args.txt";
        if(File.Exists(Path.Combine(server,profile.LaunchFile)))return;
        string url=profile.Loader=="neoforge"?$"https://maven.neoforged.net/releases/net/neoforged/neoforge/{version}/neoforge-{version}-installer.jar":$"https://maven.minecraftforge.net/net/minecraftforge/forge/{coordinate}/forge-{coordinate}-installer.jar";
        var installerJar=Path.Combine(server,"harbor-loader-installer.jar");await Download(url,installerJar,null,progress,token);
        progress("Installing "+profile.Loader+" "+version+"…");
        using var process=new Process{StartInfo=new ProcessStartInfo(profile.JavaPath){WorkingDirectory=server,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        process.StartInfo.ArgumentList.Add("-jar");process.StartInfo.ArgumentList.Add(installerJar);process.StartInfo.ArgumentList.Add("--installServer");
        var log=Path.Combine(server,"harbor-loader-install.log");object logLock=new();
        void Line(object _,DataReceivedEventArgs e){if(e.Data!=null)lock(logLock)File.AppendAllText(log,e.Data+Environment.NewLine);}
        process.OutputDataReceived+=Line;process.ErrorDataReceived+=Line;process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromMinutes(15));
        try{await process.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw;}
        if(process.ExitCode!=0)throw new IOException("The mod loader installer failed. See "+log);
        if(!File.Exists(Path.Combine(server,profile.LaunchFile))&&profile.Loader=="forge"){
            var jar=Directory.GetFiles(server,"forge-*.jar").Where(f=>!Path.GetFileName(f).Contains("installer")).OrderByDescending(f=>new FileInfo(f).Length).FirstOrDefault();
            if(jar!=null){profile.LaunchFile=Path.GetFileName(jar);profile.UsesArgumentFile=false;return;}
        }
        if(!File.Exists(Path.Combine(server,profile.LaunchFile)))throw new IOException("The loader did not create a supported server launcher. Check the pack's server instructions.");
    }
}
