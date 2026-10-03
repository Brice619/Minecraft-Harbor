using System.IO.Compression;
using System.Management;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HarborClientSetup;

public sealed record Profile(string Path, string Name)
{
    public override string ToString() => Name;
}
public sealed record InstallResult(bool AlreadyInstalled, string? BackupPath);

public static class Installer
{
    public const string HelperName = "automodpack-mc1.21.1-neoforge-4.0.6.jar";
    public const string HelperHash = "E76570A113AC9CD7FECC85FC6D2323EF2E04318E4B7615D34519843ADFE838F5";
    public static string Hash(string path) { using var s=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }
    static string Scalar(JsonElement json, params string[] keys)
    {
        foreach(var key in keys) if(json.ValueKind!=JsonValueKind.Object || !json.TryGetProperty(key,out json)) return "";
        return json.ValueKind==JsonValueKind.String ? json.GetString()! : "";
    }
    public static Profile Validate(string folder)
    {
        folder=System.IO.Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var mods=System.IO.Path.Combine(folder,"mods");
        if(!Directory.Exists(mods)) throw new InvalidDataException("Choose the existing ATM10 profile folder containing its mods folder. In CurseForge: profile → ⋮ → Open Folder.");
        string name="",version="",mc="",loader="";
        var manifest=System.IO.Path.Combine(folder,"manifest.json");
        var instance=System.IO.Path.Combine(folder,"minecraftinstance.json");
        if(File.Exists(manifest)) {
            using var doc=JsonDocument.Parse(File.ReadAllText(manifest)); var j=doc.RootElement;
            name=Scalar(j,"name");version=Scalar(j,"version");mc=Scalar(j,"minecraft","version");
            if(j.TryGetProperty("minecraft",out var m)&&m.TryGetProperty("modLoaders",out var ls))
                loader=ls.EnumerateArray().Select(l=>Scalar(l,"id")).FirstOrDefault(l=>l.StartsWith("neoforge-"))??"";
        } else if(File.Exists(instance)) {
            using var doc=JsonDocument.Parse(File.ReadAllText(instance));var j=doc.RootElement;
            name=Scalar(j,"installedModpack","name");mc=Scalar(j,"gameVersion");loader=Scalar(j,"baseModLoader","name");
            var file=Scalar(j,"installedModpack","installedFile","fileName");
            if(file.Equals("All the Mods 10-8.1.zip",StringComparison.OrdinalIgnoreCase)) version="8.1";
        }
        if(!(name.Contains("All the Mods 10",StringComparison.OrdinalIgnoreCase)||name.Contains("ATM10",StringComparison.OrdinalIgnoreCase))
            || version!="8.1" || mc!="1.21.1" || loader!="neoforge-21.1.249")
            throw new InvalidDataException("This server needs All the Mods 10 version 8.1, Minecraft 1.21.1 and NeoForge 21.1.249. Choose that existing profile; setup does not change your pack version.");
        if(!Directory.EnumerateFiles(mods,"ftb-ultimine*.jar").Any())
            throw new InvalidDataException("This profile is missing FTB Ultimine. Finish installing ATM10 8.1 in CurseForge, then try again.");
        if(File.Exists(instance)) {using var doc=JsonDocument.Parse(File.ReadAllText(instance));var label=Scalar(doc.RootElement,"name");if(label.Length>0)name=label;}
        return new Profile(folder,name+" · 8.1");
    }
    public static List<Profile> Discover()
    {
        var user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots=new[]{System.IO.Path.Combine(user,"curseforge","minecraft","Instances"),System.IO.Path.Combine(user,"Twitch","Minecraft","Instances"),System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Curse","Minecraft","Instances")};
        var found=new List<Profile>();
        foreach(var root in roots.Distinct(StringComparer.OrdinalIgnoreCase)) {
            if(!Directory.Exists(root))continue;
            try {foreach(var folder in Directory.EnumerateDirectories(root))try{found.Add(Validate(folder));}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException){} }
            catch(UnauthorizedAccessException){}catch(IOException){}
        }
        return found.DistinctBy(p=>p.Path,StringComparer.OrdinalIgnoreCase).ToList();
    }
    internal static bool IsGameProcess(string name,string? command,string folder)
    {
        if(string.IsNullOrWhiteSpace(command))return true;
        return command.Contains(folder,StringComparison.OrdinalIgnoreCase)
            || command.Contains("neoforgeclient",StringComparison.OrdinalIgnoreCase)
            || command.Contains("net.minecraft.client",StringComparison.OrdinalIgnoreCase)
            || command.Contains("--gameDir",StringComparison.OrdinalIgnoreCase)
            || (name.Equals("javaw.exe",StringComparison.OrdinalIgnoreCase)&&!command.Contains("server",StringComparison.OrdinalIgnoreCase));
    }
    public static void RequireGameClosed(string folder)
    {
        try {
            using var search=new ManagementObjectSearcher("SELECT Name, CommandLine FROM Win32_Process WHERE Name = 'java.exe' OR Name = 'javaw.exe'");
            using var processes=search.Get();
            foreach(ManagementObject p in processes) using(p) {
                if(IsGameProcess(p["Name"]?.ToString()??"",p["CommandLine"]?.ToString(),folder))
                    throw new InvalidOperationException("Close Minecraft completely before enabling sync, then try again. You can leave CurseForge open.");
            }
        } catch(ManagementException ex){throw new InvalidOperationException("Windows could not check whether Minecraft is closed. Close the game and reopen this installer.",ex);}
    }
    static bool IsHelper(string path)
    {
        if(System.IO.Path.GetFileName(path).StartsWith("automodpack",StringComparison.OrdinalIgnoreCase))return true;
        try {
            using var zip=ZipFile.OpenRead(path);
            foreach(var name in new[]{"META-INF/neoforge.mods.toml","META-INF/mods.toml"}){
                var e=zip.GetEntry(name);if(e==null||e.Length>131072)continue;
                using var reader=new StreamReader(e.Open());
                if(Regex.IsMatch(reader.ReadToEnd(),"(?m)^\\s*modId\\s*=\\s*[\"']automodpack[\"']"))return true;
            }
        }catch(InvalidDataException){}
        return false;
    }
    public static InstallResult Install(string folder) => InstallCore(folder,RequireGameClosed);
    // A separate check function allows tests to use isolated fixtures while a real server is running.
    internal static InstallResult InstallCore(string folder,Action<string> gameCheck,Action? afterRemoval=null)
    {
        var profile=Validate(folder);folder=profile.Path;
        using var mutex=new Mutex(false,"Local\\HarborClientSetup-"+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(folder.ToUpperInvariant()))));
        bool owned=false;
        try {
            try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
            if(!owned)throw new InvalidOperationException("Another installer is already updating this profile.");
            gameCheck(folder);
            var mods=System.IO.Path.Combine(folder,"mods");
            var old=Directory.EnumerateFiles(mods,"*.jar").Where(IsHelper).ToArray();
            var target=System.IO.Path.Combine(mods,HelperName);
            if(old.Length==1&&old[0].Equals(target,StringComparison.OrdinalIgnoreCase)&&Hash(target)==HelperHash)return new(true,null);
            foreach(var jar in old)using(File.Open(jar,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
            using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("Harbor.SyncHelper.jar")??throw new InvalidDataException("The bundled helper is missing.");
            var staged=System.IO.Path.Combine(mods,".harbor-"+Guid.NewGuid().ToString("N")+".tmp");
            string? backup=null;bool installed=false;var removed=new List<string>();
            try {
                using(var output=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None)){resource.CopyTo(output);output.Flush(true);}
                if(Hash(staged)!=HelperHash)throw new InvalidDataException("The bundled helper did not pass its integrity check. Download the installer again from Harbor.");
                if(old.Length>0){
                    backup=System.IO.Path.Combine(folder,"harbor-sync-backups",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..8]);
                    Directory.CreateDirectory(backup);
                    foreach(var jar in old){var copy=System.IO.Path.Combine(backup,System.IO.Path.GetFileName(jar));File.Copy(jar,copy);if(Hash(jar)!=Hash(copy))throw new IOException("Backup verification failed. Your helper has not been replaced.");}
                }
                gameCheck(folder);
                foreach(var jar in old){File.Delete(jar);removed.Add(jar);}
                afterRemoval?.Invoke();
                File.Move(staged,target);installed=true;
                if(Hash(target)!=HelperHash)throw new IOException("The installed helper did not pass verification.");
                return new(false,backup);
            } catch(Exception failure) {
                try{if(installed)File.Delete(target);foreach(var jar in removed)File.Copy(System.IO.Path.Combine(backup!,System.IO.Path.GetFileName(jar)),jar,false);}
                catch(Exception rollback){throw new IOException("Setup could not finish restoring the previous helper. Your copies are in "+backup+". Close Minecraft and restore those JARs into mods before launching.",new AggregateException(failure,rollback));}
                throw;
            } finally {if(File.Exists(staged))File.Delete(staged);}
        }finally{if(owned)mutex.ReleaseMutex();}
    }
}
