using System.Text.Json;
namespace MinecraftHarbor;
internal static class AutoModpackTests
{
    internal static void Run(string report,string helper)
    {
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"connector-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string client=Path.Combine(root,"Client"),server=Path.Combine(root,"server"),setup=Path.Combine(root,"client-setup");Directory.CreateDirectory(client);Directory.CreateDirectory(server);Directory.CreateDirectory(setup);
        void Metadata(int file,int serverFile)=>File.WriteAllText(Path.Combine(client,"minecraftinstance.json"),JsonSerializer.Serialize(new{name="Renamed profile",gameVersion="1.21.1",baseModLoader=new{name="neoforge-21.1.249"},installedModpack=new{addonID=925200,name="ATM10",installedFile=new{id=file,fileName="ATM10-8.1.zip",serverPackFileId=serverFile}}}));
        void Check(bool result,string message){if(!result)throw new Exception(message);}
        Metadata(8764211,8764245);File.WriteAllText(Path.Combine(client,"manifest.json"),"{\"version\":\"8.1\"}");
        var installed=CurseForgeProfiles.Read(client);Check(installed.ClientFileId==8764211,"Installed release ID wasn't read");
        var profile=new ServerProfile{Name="ATM10",PackVersion="8.1",ProjectId=925200,ServerFileId=8764245,CurseForgePath=client,Loader="neoforge",MinecraftVersion="1.21.1"};
        string initial=JsonSerializer.Serialize(AutoModpackSetup.Requirement(profile));Check(profile.CurseForgeClientFileId==8764211&&initial.Contains("8764211"),"Required release wasn't pinned");
        Metadata(999,111);Check(JsonSerializer.Serialize(AutoModpackSetup.Requirement(profile))==initial,"Updating the host client silently changed the server's required release");
        File.Copy(helper,Path.Combine(setup,ClientSetup.HelperName));Directory.CreateDirectory(Path.Combine(server,"mods"));File.WriteAllText(Path.Combine(server,"mods","automodpack-mc1.21.1-neoforge-4.0.6.jar"),"old helper");File.WriteAllText(Path.Combine(server,"mods","custom.jar"),"custom mod");
        Directory.CreateDirectory(Path.Combine(server,"automodpack",".private"));string cert=Path.Combine(server,"automodpack",".private","cert.crt");File.WriteAllText(cert,"existing certificate");
        File.WriteAllText(Path.Combine(server,"automodpack","automodpack-server.json"),"{\"syncedFiles\":[\"/mods/*.jar\",\"/kubejs/**\"],\"allowEditsInFiles\":[\"/options.txt\",\"/config/**\"],\"bindPort\":25567}");
        AutoModpackSetup.Prepare(root,server,profile);AutoModpackSetup.Prepare(root,server,profile);
        using var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(server,"automodpack","automodpack-server.json")));var settings=config.RootElement;
        Check(settings.GetProperty("syncedFiles").EnumerateArray().Any(x=>x.GetString()=="/config/**"),"Configs aren't synchronized");Check(!settings.GetProperty("allowEditsInFiles").EnumerateArray().Any(x=>x.GetString()=="/config/**"),"Config updates disabled");Check(settings.GetProperty("bindPort").GetInt32()==25567,"Network configuration changed");
        Check(File.ReadAllText(cert)=="existing certificate"&&File.ReadAllText(Path.Combine(server,"mods","custom.jar"))=="custom mod","Certificate or custom mod changed");Check(Directory.GetFiles(Path.Combine(server,"mods"),"automodpack*.jar").Length==1,"Duplicate helper installed");
        Check(JsonSerializer.Serialize(AutoModpackSetup.Requirement(new(){Loader="vanilla"}))=="null","Vanilla got a CurseForge requirement");
        using var requirement=JsonDocument.Parse(File.ReadAllText(Path.Combine(server,"automodpack","harbor-requirements.json")));Check(requirement.RootElement.GetProperty("clientFileId").GetInt64()==8764211,"Required client file missing from metadata");
        ServerManager.WriteJson(report,new{passed=true,checks=new[]{"Exact CurseForge release ID read and pinned","Host client upgrades preserve the server release requirement","Configs sync and update; user settings excluded","Repeated setup installs one helper and preserves certificate, custom mods and network settings"},realServerTouched=false});
    }
}
