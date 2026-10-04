using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
namespace MinecraftHarbor;

internal static class AutoModpackSetup
{
    internal static object? Requirement(ServerProfile profile)
    {
        if(profile.ProjectId<=0 || string.IsNullOrWhiteSpace(profile.PackVersion)) return null;
        long fileId=profile.CurseForgeClientFileId;
        string source=profile.ClientSyncPath.Length>0?profile.ClientSyncPath:profile.CurseForgePath;
        string metadata=Path.Combine(source,"minecraftinstance.json");
        if(fileId==0 && File.Exists(metadata)) {
            using var json=JsonDocument.Parse(File.ReadAllText(metadata));
            if(json.RootElement.TryGetProperty("installedModpack",out var pack) && pack.TryGetProperty("addonID",out var project) && project.TryGetInt64(out var id) && id==profile.ProjectId && pack.TryGetProperty("installedFile",out var file) && file.TryGetProperty("serverPackFileId",out var serverFile) && serverFile.TryGetInt64(out var serverId) && serverId==profile.ServerFileId && file.TryGetProperty("id",out var clientFile) && clientFile.TryGetInt64(out var clientId)) fileId=clientId;
        }
        if(fileId>0)profile.CurseForgeClientFileId=fileId;
        return new {name=profile.Name,version=profile.PackVersion,projectId=profile.ProjectId,clientFileId=fileId};
    }
    internal static void Prepare(string root,string serverDir,ServerProfile profile)
    {
        if(profile.Loader!="neoforge" || profile.MinecraftVersion!="1.21.1" || profile.ProjectId<=0) return;
        string helper=Path.Combine(root,"client-setup",ClientSetup.HelperName);
        if(!File.Exists(helper)) throw new FileNotFoundException("Official AutoModpack is missing. Reinstall Harbor.");
        ClientSetup.VerifyHelper(File.ReadAllBytes(helper));
        Directory.CreateDirectory(Path.Combine(serverDir,"automodpack"));
        // Retire the custom connector's enforced pack-version requirement.
        string retiredRequirement=Path.Combine(serverDir,"automodpack","harbor-requirements.json");
        if(File.Exists(retiredRequirement))File.Delete(retiredRequirement);
        Configure(root,serverDir,profile,profile.Name,profile.PackVersion);
        PreserveCertificate(serverDir);
        string mods=Path.Combine(serverDir,"mods");Directory.CreateDirectory(mods);
        string target=Path.Combine(mods,ClientSetup.HelperName);
        // Install the unmodified official release, retaining other mods and the certificate.
        foreach(var old in Directory.EnumerateFiles(mods,"automodpack*.jar").Where(p=>
            System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),@"^automodpack-(?:mc1\.21\.1-neoforge-|\d+\.)",System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            && !Path.GetFileName(p).Equals(ClientSetup.HelperName,StringComparison.OrdinalIgnoreCase))) File.Delete(old);
        if(!File.Exists(target)||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)))!=ClientSetup.HelperSha256)File.Copy(helper,target,true);
    }
    internal static void Configure(string root,string serverDir,ServerProfile profile,params string[] arguments)
    {
        string tools=Path.Combine(root,"server-tools"), helper=Path.Combine(root,"client-setup",ClientSetup.HelperName);
        if(!File.Exists(Path.Combine(tools,"configure-automodpack.jar"))) throw new FileNotFoundException("Server configuration files are missing. Reinstall Harbor.");
        var start=new ProcessStartInfo(profile.JavaPath){WorkingDirectory=serverDir,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"-cp",tools+Path.DirectorySeparatorChar+"*"+Path.PathSeparator+helper,"ConfigureAutoModpack",serverDir}.Concat(arguments))start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Could not prepare AutoModpack settings.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(30000)){process.Kill();throw new IOException("AutoModpack settings took too long to prepare.");}
        if(process.ExitCode!=0)throw new IOException("Could not prepare AutoModpack settings: "+output.GetAwaiter().GetResult()+error.GetAwaiter().GetResult());
    }
    static void PreserveCertificate(string serverDir)
    {
        string folder=Path.Combine(serverDir,"automodpack"),credentials=Path.Combine(folder,"credentials");
        string certificate=Path.Combine(credentials,"certificate.crt"),key=Path.Combine(credentials,"private-key.pem");
        if(File.Exists(certificate)&&File.Exists(key))return;
        string oldCert=Path.Combine(folder,".private","cert.crt"),oldKey=Path.Combine(folder,".private","key.pem");
        if(!File.Exists(oldCert)&&!File.Exists(oldKey))return;
        if(File.Exists(certificate)||File.Exists(key)||!File.Exists(oldCert)||!File.Exists(oldKey))throw new IOException("The AutoModpack certificate pair is incomplete. Restore it before starting the server.");
        Directory.CreateDirectory(credentials);
        File.Copy(oldCert,certificate);File.Copy(oldKey,key);
    }
}
