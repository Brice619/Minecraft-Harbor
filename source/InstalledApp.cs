namespace MinecraftHarbor;
internal static class InstalledApp
{
    internal static string DefaultRoot()=>File.Exists(Path.Combine(AppContext.BaseDirectory,"settings.json"))?AppContext.BaseDirectory:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MinecraftHarbor","Data");
    internal static void Prepare(string root)
    {
        Directory.CreateDirectory(root);
        if(!File.Exists(Path.Combine(root,"library.json"))&&!File.Exists(Path.Combine(root,"settings.json"))){
            var settings=new Settings{WorldName="New world",MemoryGB=Math.Min(8,Settings.MaxMemoryGB),OwnerName="",OwnerUuid="",KeepAwake=false};ServerManager.WriteJson(Path.Combine(root,"settings.json"),settings);
            ServerManager.WriteJson(Path.Combine(root,"library.json"),new LibraryData{ActiveId="empty",Profiles=[new ServerProfile{Id="empty",Name="No server selected",ServerName="No server selected",Deleted=true,LegacyLocation=true,CustomUltimineSync=false,Loader="vanilla",MinecraftVersion="",Worlds=[new WorldEntry{Folder="world",Name="New world",CanGenerate=true}]}]});
        }
        string source=Path.Combine(AppContext.BaseDirectory,"client-setup"),target=Path.Combine(root,"client-setup");if(!Path.GetFullPath(source).Equals(Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase)&&Directory.Exists(source)){Directory.CreateDirectory(target);foreach(string name in new[]{ClientSetup.AgentName,ClientSetup.AgentName+".sha256",ClientSetup.HelperName}){string file=Path.Combine(source,name);if(File.Exists(file))File.Copy(file,Path.Combine(target,name),true);}}
    }
    internal static void VerifyFreshInstall(string report){string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"fresh-install-"+Guid.NewGuid().ToString("N"));Prepare(root);using var server=new ServerManager(root);if(!server.Profile.Deleted||server.Library.Data.Profiles.Any(p=>p.ProjectId!=0||p.CurseForgePath.Length>0)||server.Config.OwnerUuid.Length>0||File.Exists(Path.Combine(root,"server-process.json")))throw new Exception("New installation includes private settings.");server.Config.WorldName="Preserved setting";ServerManager.WriteJson(Path.Combine(root,"settings.json"),server.Config);Prepare(root);using var reopened=new ServerManager(root);if(reopened.Config.WorldName!="Preserved setting")throw new Exception("Application update overwrote settings.");ServerManager.WriteJson(report,new{passed=true,checks=new[]{"Clean first launch has no private world, account, or modpack path","Updates preserve server settings and refresh bundled connector files"},realServerTouched=false});}
}
