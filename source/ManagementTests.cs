using System.IO.Compression;
namespace MinecraftHarbor;
internal static class ManagementTests
{
    internal static void Run(string source,string report)
    {
        var results=new List<string>();string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"management-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"server","world"));Directory.CreateDirectory(Path.Combine(root,"backups"));
        void Check(bool ok,string message){if(!ok)throw new Exception(message);results.Add(message);}
        var file=Path.Combine(root,"server","world","level.dat");File.Copy(source,file);var before=File.ReadAllBytes(source);var info=WorldImport.Read(Path.GetDirectoryName(source)!);
        ServerManager.WriteJson(Path.Combine(root,"settings.json"),new Settings{WorldName=info.Name,MemoryGB=80});
        using var manager=new ServerManager(root+Path.DirectorySeparatorChar);Check(manager.Root==root,"Installed-app paths with a trailing separator normalize correctly");File.WriteAllText(Path.Combine(root,"server","server.properties"),"difficulty=normal\npvp=false\nonline-mode=true\nallow-flight=true\n");
        var rules=manager.ReadGameRules(manager.Profile);Check(rules.Count>0&&rules.ContainsKey("keepInventory"),"Existing world game rules are read");
        var second=new ServerProfile{Id="second-test",Name="Test pack",Worlds=new(){new(){Name="Second world"}}};manager.Library.Data.Profiles.Add(second);Directory.CreateDirectory(Path.Combine(root,"profiles",second.Id,"server"));ServerManager.WriteJson(Path.Combine(root,"profiles",second.Id,"settings.json"),new Settings{WorldName="Second world",MemoryGB=12});manager.Library.Save();
        var cfg=manager.ReadProfileSettings(second);cfg.MemoryGB=24;cfg.WorldName="Renamed second";manager.SaveProfile(second,cfg,"Second server");Check(manager.Config.MemoryGB==80&&manager.Config.WorldName==info.Name&&manager.ReadProfileSettings(second).MemoryGB==24,"Inactive edits preserve the active configuration");
        var active=manager.ReadProfileSettings(manager.Profile);active.WorldName="Test rename";manager.SaveProfile(manager.Profile,active,"Test server");Check(WorldImport.Read(Path.GetDirectoryName(file)!).Name=="Test rename","World rename updates Minecraft metadata");
        var changed=new Dictionary<string,string>(rules){["keepInventory"]=rules["keepInventory"]=="true"?"false":"true"};manager.SaveGameSettings(manager.Profile,new(){["pvp"]="true",["online-mode"]="false",["allow-flight"]="false"},changed);
        manager.SaveSettings(manager.ReadProfileSettings(manager.Profile));Check(manager.ReadGameProperties(manager.Profile)["online-mode"]=="false"&&manager.ReadGameProperties(manager.Profile)["allow-flight"]=="false","General settings preserve game options");
        Check(manager.ReadGameRules(manager.Profile)["keepInventory"]==changed["keepInventory"],"Game rules persist in level.dat");
        new WorldMetadata(file).Save(file,info.Name,rules);
        byte[] Unzip(byte[] bytes){using var input=new MemoryStream(bytes);using var zipped=new GZipStream(input,CompressionMode.Decompress);using var output=new MemoryStream();zipped.CopyTo(output);return output.ToArray();}
        Check(Unzip(before).SequenceEqual(Unzip(File.ReadAllBytes(file))),"Unedited mod and player metadata remains byte-for-byte intact");Check(File.ReadAllBytes(source).SequenceEqual(before),"Original world is untouched");
        var invalid=new Dictionary<string,string>(rules){["keepInventory"]="invalid"};bool rejected=false;try{manager.SaveGameSettings(manager.Profile,new(),invalid);}catch(ArgumentException){rejected=true;}Check(rejected,"Invalid rules are rejected");
        manager.SelectAsync(second.Id,"world").GetAwaiter().GetResult();Check(manager.Profile.Id==second.Id&&manager.Config.MemoryGB==24&&manager.Config.WorldName=="Renamed second","Activation loads the selected server configuration");
        // Retain fixture files outside server folders; exercise deletion state without recycling user data.
        string secondDir=Path.Combine(root,"profiles",second.Id,"server");Directory.Move(secondDir,Path.Combine(root,"retained-second"));
        manager.DeleteProfile(second);Check(manager.Profile.Id=="original-atm10"&&second.Deleted,"Deleting active selection chooses another saved server");
        rejected=false;try{manager.DeleteProfile(new ServerProfile{Id="../outside",Worlds=new(){new()}});}catch(IOException){rejected=true;}Check(rejected,"Deletion rejects an unregistered profile without touching files");
        Directory.Move(Path.Combine(root,"server"),Path.Combine(root,"retained-original"));manager.DeleteProfile(manager.Profile);
        Check(manager.Library.Data.Profiles.All(p=>p.Deleted),"Deleting the last server leaves an empty library");
        using(var reopened=new ServerManager(root)){rejected=false;try{reopened.StartAsync().GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}Check(rejected,"Deleted servers cannot start after reopening Harbor");}
        ServerManager.WriteJson(report,new{passed=true,results,fixture=root});
    }
}
