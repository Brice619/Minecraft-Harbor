using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        var requirement=Requirement(profile);
        if(requirement==null) return;
        string helper=Path.Combine(root,"client-setup",ClientSetup.HelperName);
        if(!File.Exists(helper)) throw new FileNotFoundException("The Harbor connector mod is missing. Reinstall Harbor 1.4.");
        ClientSetup.VerifyHelper(File.ReadAllBytes(helper));
        Directory.CreateDirectory(Path.Combine(serverDir,"automodpack"));
        ServerManager.WriteJson(Path.Combine(serverDir,"automodpack","harbor-requirements.json"),requirement);
        string configPath=Path.Combine(serverDir,"automodpack","automodpack-server.json");
        var config=File.Exists(configPath)?JsonNode.Parse(File.ReadAllText(configPath))!.AsObject():new JsonObject();
        var synced=config["syncedFiles"]?.AsArray().Select(n=>n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal)??new(){"/mods/*.jar","/kubejs/**","!/kubejs/server_scripts/**","/emotes/*"};
        synced.Add("/config/**");synced.Add("!/config/fancymenu/user_variables.db");
        config["syncedFiles"]=new JsonArray(synced.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray());
        var editable=config["allowEditsInFiles"]?.AsArray().Select(n=>n!.GetValue<string>()).Where(s=>s!="/config/**").ToArray()??["/options.txt"];
        config["allowEditsInFiles"]=new JsonArray(editable.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray());
        config["DO_NOT_CHANGE_IT"]=2;config["modpackHost"]=true;config["generateModpackOnStart"]=true;config["requireAutoModpackOnClient"]=true;config["selfUpdater"]=false;
        File.WriteAllText(configPath,config.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
        string mods=Path.Combine(serverDir,"mods");Directory.CreateDirectory(mods);
        string target=Path.Combine(mods,ClientSetup.HelperName);
        // Replace the packaged helper, retaining all other mods and the server certificate.
        foreach(var old in Directory.EnumerateFiles(mods,"automodpack-mc1.21.1-neoforge-*.jar").Where(p=>!Path.GetFileName(p).Equals(ClientSetup.HelperName,StringComparison.OrdinalIgnoreCase))) File.Delete(old);
        if(!File.Exists(target)||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)))!=ClientSetup.HelperSha256)File.Copy(helper,target,true);
    }
}
