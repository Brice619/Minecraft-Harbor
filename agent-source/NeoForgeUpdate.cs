using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MinecraftHarbor;
namespace HarborAgent;

internal sealed record LoaderProfileUpdate(string Before,string After,string? CachePath=null,string? CacheBefore=null,string? CacheAfter=null);

internal static class NeoForgeUpdate
{
    internal static void RequireLauncherClosed()
    {
        foreach(var process in HarborUpdates.UpdateShutdown.CurseForgeProcesses())
        {
            using(process)if(!process.HasExited)
                throw new InvalidOperationException("CurseForge reopened during the update. Try again.");
        }
    }

    internal static string InstallRoot(string folder)
    {
        var parent=Directory.GetParent(Path.GetFullPath(folder));
        if(parent?.Name.Equals("Instances",StringComparison.OrdinalIgnoreCase)!=true||parent.Parent==null)
            throw new InvalidDataException("Harbor could not locate this profile's CurseForge installation. Select its folder inside CurseForge's Instances folder.");
        string install=Path.Combine(parent.Parent.FullName,"Install");
        if(!Directory.Exists(Path.Combine(install,"versions")))throw new InvalidDataException("This CurseForge installation is incomplete. Launch this profile once through CurseForge first.");
        return install;
    }

    internal static bool HasCurseForgeLaunchFiles(string folder,LanSyncInfo info)=>HasLaunchFiles(InstallRoot(folder),info);

    internal static bool HasLaunchFiles(string install,LanSyncInfo info)
    {
        string id="neoforge-"+info.LoaderVersion;
        string gameJar=Path.Combine(install,"versions",id,id+".jar"),vanillaJar=Path.Combine(install,"versions",info.MinecraftVersion,info.MinecraftVersion+".jar");
        string patch=Path.Combine(install,"libraries","net","neoforged","neoforge",info.LoaderVersion,"neoforge-"+info.LoaderVersion+"-clientdata.lzma");
        return File.Exists(gameJar)&&File.Exists(vanillaJar)&&File.Exists(patch)&&new FileInfo(patch).Length>0&&new FileInfo(gameJar).Length==new FileInfo(vanillaJar).Length&&Hash(gameJar)==Hash(vanillaJar);
    }

    internal static async Task<LoaderProfileUpdate> Prepare(string folder,LanSyncInfo info,Action<string>? progress,Action<string> check,Action? launcherCheck=null,string? testJava=null)
    {
        launcherCheck??=RequireLauncherClosed;check(folder);launcherCheck();
        if(!Regex.IsMatch(info.LoaderVersion,@"^\d+\.\d+\.\d+(?:-beta)?$")||!Regex.IsMatch(info.MinecraftVersion,@"^1\.\d+(?:\.\d+)?$"))
            throw new InvalidDataException("Unsupported NeoForge version from the server.");
        string install=InstallRoot(folder),metadata=Path.Combine(folder,"minecraftinstance.json"),before=File.ReadAllText(metadata);
        string java=testJava??FindJava(install);
        string stage=Path.Combine(install,"harbor-loader-staging",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var token=deadline.Token;
            using var http=new HttpClient{Timeout=TimeSpan.FromMinutes(3)};
            string url=$"https://maven.neoforged.net/releases/net/neoforged/neoforge/{info.LoaderVersion}/neoforge-{info.LoaderVersion}-installer.jar";
            progress?.Invoke("Downloading NeoForge "+info.LoaderVersion+"…");
            string expected=(await http.GetStringAsync(url+".sha256",token)).Trim();
            if(expected.Length!=64||!expected.All(Uri.IsHexDigit))throw new InvalidDataException("NeoForge's installer checksum is invalid.");
            string installer=Path.Combine(stage,"installer.jar");
            using(var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token))
            {
                response.EnsureSuccessStatusCode();
                using var input=await response.Content.ReadAsStreamAsync(token);using var output=File.Create(installer);
                byte[] buffer=new byte[65536];long total=0;int count;
                while((count=await input.ReadAsync(buffer,token))>0){total+=count;if(total>64*1024*1024)throw new InvalidDataException("NeoForge installer is too large.");await output.WriteAsync(buffer.AsMemory(0,count),token);}
            }
            if(!Hash(installer).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("NeoForge installer failed verification.");
            string version,profile;
            using(var archive=ZipFile.OpenRead(installer))
            {
                string Read(string name){var entry=archive.GetEntry(name)??throw new InvalidDataException("NeoForge installer is incomplete.");if(entry.Length>4*1024*1024)throw new InvalidDataException("NeoForge metadata is too large.");using var reader=new StreamReader(entry.Open());return reader.ReadToEnd();}
                version=Read("version.json");profile=Read("install_profile.json");
            }
            var update=CurseForgeProfileCache.Attach(folder,BuildProfile(before,info,version,profile));
            if(CanReuseInstalled(install,info,version,true))
            {
                check(folder);launcherCheck();
                if(File.ReadAllText(metadata)!=before)throw new InvalidOperationException("The CurseForge profile changed during the update. Try again.");
                progress?.Invoke("Verified installed NeoForge "+info.LoaderVersion+". Preparing the profile update…");
                return update;
            }
            string runtime=Path.Combine(stage,"runtime");Directory.CreateDirectory(runtime);
            File.WriteAllText(Path.Combine(runtime,"launcher_profiles.json"),"{\"profiles\":{}}");
            // Reuse the existing vanilla jar; the official installer prepares the loader libraries and runs its processors.
            foreach(string extension in new[]{"jar","json"})
            {
                string source=Path.Combine(install,"versions",info.MinecraftVersion,info.MinecraftVersion+"."+extension);
                if(File.Exists(source)){string destination=Path.Combine(runtime,"versions",info.MinecraftVersion,Path.GetFileName(source));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(source,destination);}
            }
            progress?.Invoke("Installing NeoForge "+info.LoaderVersion+"…");
            await RunInstaller(java,installer,runtime,stage,token,progress);
            // CurseForge needs a vanilla game-JAR alias and the client patch at its Maven path.
            // The standard launcher uses inheritance and the official installer discards the patch.
            string loaderVersionFolder=Path.Combine(runtime,"versions","neoforge-"+info.LoaderVersion);
            File.Copy(Path.Combine(runtime,"versions",info.MinecraftVersion,info.MinecraftVersion+".jar"),Path.Combine(loaderVersionFolder,"neoforge-"+info.LoaderVersion+".jar"));
            string patch=Path.Combine(runtime,"libraries","net","neoforged","neoforge",info.LoaderVersion,"neoforge-"+info.LoaderVersion+"-clientdata.lzma");
            Directory.CreateDirectory(Path.GetDirectoryName(patch)!);
            using(var archive=ZipFile.OpenRead(installer))(archive.GetEntry("data/client.lzma")??throw new InvalidDataException("NeoForge client patch is missing.")).ExtractToFile(patch);
            VerifyInstalled(runtime,info);
            if(!HasLaunchFiles(runtime,info))throw new InvalidDataException("CurseForge's NeoForge launch files are incomplete.");
            check(folder);launcherCheck();
            if(File.ReadAllText(metadata)!=before)throw new InvalidOperationException("The CurseForge profile changed during the update. Try again.");
            // Versioned libraries may be shared. Never replace different bytes that another profile might use.
            foreach(string relative in new[]{"libraries","versions"})
            {
                string sourceRoot=Path.Combine(runtime,relative);
                if(!Directory.Exists(sourceRoot))continue;
                foreach(string source in Directory.EnumerateFiles(sourceRoot,"*",SearchOption.AllDirectories))
                {
                    string target=Path.Combine(install,relative,Path.GetRelativePath(sourceRoot,source));
                    // Preserve existing vanilla files; publish missing base files from the verified installer.
                    if(relative=="versions"&&!Path.GetRelativePath(sourceRoot,source).StartsWith("neoforge-"+info.LoaderVersion+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&File.Exists(target))continue;
                    if(File.Exists(target))
                    {
                        if(relative=="versions"&&Path.GetExtension(target).Equals(".json",StringComparison.OrdinalIgnoreCase))
                        {
                            var existing=JsonNode.Parse(File.ReadAllText(target));
                            if(existing?["id"]?.GetValue<string>()!="neoforge-"+info.LoaderVersion||existing?["inheritsFrom"]?.GetValue<string>()!=info.MinecraftVersion)
                                throw new IOException("The existing NeoForge launch profile is inconsistent. Repair it in CurseForge first.");
                            continue;
                        }
                        if(Hash(source)!=Hash(target))
                        {
                            throw new IOException("An existing loader file differs: "+Path.GetFileName(target)+". Repair this profile in CurseForge before retrying.");
                        }
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    string temporary=target+".harbor-"+Guid.NewGuid().ToString("N");
                    try{File.Copy(source,temporary);File.Move(temporary,target);}finally{if(File.Exists(temporary))File.Delete(temporary);}
                }
            }
            progress?.Invoke("NeoForge installed. Preparing the pack update…");
            return update;
        }
        catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException)
        {throw new InvalidOperationException("NeoForge could not finish downloading or installing. Your profile has not been switched. Try Update & Launch again. "+ex.Message,ex);}
        finally{try{Directory.Delete(stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }

    internal static LoaderProfileUpdate BuildProfile(string before,LanSyncInfo info,string version,string profile)
    {
        var versionData=JsonNode.Parse(version)??throw new InvalidDataException("Missing NeoForge version metadata.");
        var installData=JsonNode.Parse(profile)??throw new InvalidDataException("Missing NeoForge install metadata.");
        string id="neoforge-"+info.LoaderVersion;
        if(versionData["id"]?.GetValue<string>()!=id||versionData["inheritsFrom"]?.GetValue<string>()!=info.MinecraftVersion||installData["version"]?.GetValue<string>()!=id||installData["minecraft"]?.GetValue<string>()!=info.MinecraftVersion)
            throw new InvalidDataException("NeoForge installer does not match the server's Minecraft and loader versions.");
        var root=JsonNode.Parse(before)!.AsObject();
        // CurseForge's native processor runner expects explicit client-side data and does
        // not filter server-only processors from the official combined installer manifest.
        installData["path"]="net.neoforged:neoforge:"+info.LoaderVersion;
        if(installData["data"] is JsonObject data)data["SIDE"]=new JsonObject{["client"]="client",["server"]="server"};
        if(installData["processors"] is JsonArray processors)
            for(int index=processors.Count-1;index>=0;index--)if(processors[index]?["sides"] is JsonArray sides&&!sides.Any(side=>side?.GetValue<string>()=="client"))processors.RemoveAt(index);
        root["baseModLoader"]=new JsonObject{
            ["forgeVersion"]=info.LoaderVersion,["name"]=id,["type"]=6,["downloadUrl"]="",["filename"]=id+".jar",
            ["installMethod"]=6,["latest"]=false,["recommended"]=false,["versionJson"]=version,
            ["librariesInstallLocation"]="{0}//libraries//net//neoforged//neoforge//"+info.LoaderVersion,
            ["minecraftVersion"]=info.MinecraftVersion,["installProfileJson"]=installData.ToJsonString()
        };
        return new(before,root.ToJsonString());
    }

    internal static void VerifyInstalled(string runtime,LanSyncInfo info)
    {
        string id="neoforge-"+info.LoaderVersion;
        var version=JsonNode.Parse(File.ReadAllText(Path.Combine(runtime,"versions",id,id+".json")))!;
        if(version["id"]?.GetValue<string>()!=id||version["inheritsFrom"]?.GetValue<string>()!=info.MinecraftVersion||version["libraries"] is not JsonArray {Count:>0})throw new InvalidDataException("Installed NeoForge version is wrong or incomplete.");
        foreach(var library in version["libraries"]!.AsArray())
        {
            var artifact=library?["downloads"]?["artifact"];string? path=artifact?["path"]?.GetValue<string>();
            if(path==null)continue;
            string file=Path.GetFullPath(Path.Combine(runtime,"libraries",path.Replace('/',Path.DirectorySeparatorChar)));
            string allowed=Path.GetFullPath(Path.Combine(runtime,"libraries"))+Path.DirectorySeparatorChar;
            if(!file.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!File.Exists(file)||new FileInfo(file).Length==0)throw new InvalidDataException("NeoForge did not install all required libraries.");
            string? expected=artifact?["sha1"]?.GetValue<string>();
            if(!string.IsNullOrEmpty(expected)){using var stream=File.OpenRead(file);if(!Convert.ToHexString(SHA1.HashData(stream)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("An installed NeoForge library failed verification.");}
        }
    }

    internal static bool CanReuseInstalled(string install,LanSyncInfo info,string officialVersion,bool requireLauncherFiles=false)
    {
        try
        {
            string id="neoforge-"+info.LoaderVersion;
            if(requireLauncherFiles&&!HasLaunchFiles(install,info))return false;
            var installed=JsonNode.Parse(File.ReadAllText(Path.Combine(install,"versions",id,id+".json")))!;
            var expected=JsonNode.Parse(officialVersion)!;
            // Verify against the downloaded official manifest, not a potentially incomplete local list.
            if(!JsonNode.DeepEquals(installed["libraries"],expected["libraries"])||!JsonNode.DeepEquals(installed["mainClass"],expected["mainClass"])||!JsonNode.DeepEquals(installed["arguments"],expected["arguments"]))return false;
            VerifyInstalled(install,info);
            return true;
        }
        catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException){return false;}
    }

    static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    static string FindJava(string install)
    {
        string root=Path.Combine(install,"java");
        if(Directory.Exists(root))foreach(string java in Directory.EnumerateFiles(root,"java.exe",SearchOption.AllDirectories).OrderByDescending(p=>p.Contains("Jre_21",StringComparison.OrdinalIgnoreCase)))
        {
            string release=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(java))!,"release");
            if(File.Exists(release)&&Regex.IsMatch(File.ReadAllText(release),"JAVA_VERSION=\"(?:21|2[2-9])\\."))return java;
        }
        throw new InvalidOperationException("Java 21 is missing from CurseForge. Launch your Minecraft 1.21 profile in CurseForge once, then retry.");
    }

    static async Task RunInstaller(string java,string installer,string runtime,string working,CancellationToken token,Action<string>? progress)
    {
        using var process=new Process{StartInfo=new(java){WorkingDirectory=working,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(string arg in new[]{"-jar",installer,"--installClient",runtime})process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        var started=Stopwatch.StartNew();
        var exited=process.WaitForExitAsync(token);
        try
        {
            while(!exited.IsCompleted)
            {
                if(await Task.WhenAny(exited,Task.Delay(TimeSpan.FromSeconds(5),token))==exited)break;
                token.ThrowIfCancellationRequested();
                progress?.Invoke($"Installing NeoForge… {started.Elapsed:mm\\:ss} elapsed. Preparing Minecraft libraries; this can take several minutes.");
            }
            await exited;
        }
        catch{if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();throw;}
        string log=await stdout+"\n"+await stderr;
        if(process.ExitCode!=0)throw new InvalidOperationException("NeoForge installation failed. "+string.Join(" ",log.Split('\n').Where(l=>l.Contains("error",StringComparison.OrdinalIgnoreCase)||l.Contains("fail",StringComparison.OrdinalIgnoreCase)).TakeLast(3)));
    }
}

