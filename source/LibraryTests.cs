using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;

namespace MinecraftHarbor;
internal static class LibraryTests
{
    internal static void WriteWorld(string folder,string name="Fixture",string minecraft="1.21.1")
    {
        Directory.CreateDirectory(folder);using var file=File.Create(Path.Combine(folder,"level.dat"));using var gzip=new GZipStream(file,CompressionLevel.Optimal);using var data=new MemoryStream();
        void String(string s){var b=Encoding.UTF8.GetBytes(s);Span<byte> n=stackalloc byte[2];BinaryPrimitives.WriteUInt16BigEndian(n,(ushort)b.Length);data.Write(n);data.Write(b);}
        void Header(byte type,string name){data.WriteByte(type);String(name);}
        void Int(int value){Span<byte> b=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(b,value);data.Write(b);}
        Header(10,"");Header(10,"Data");Header(8,"LevelName");String(name);Header(3,"DataVersion");Int(3955);Header(10,"Version");Header(8,"Name");String(minecraft);data.WriteByte(0);
        Header(10,"Player");Header(11,"UUID");Int(4);Int(123);Int(456);Int(789);Int(1011);Header(8,"HarborInventoryTest");String("preserved-inventory-and-mod-data");data.WriteByte(0);data.WriteByte(0);data.WriteByte(0);data.Position=0;data.CopyTo(gzip);
    }
    public static async Task Run(string report)
    {
        var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"library-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var checks=new List<string>();
        void Assert(bool ok,string text){if(!ok)throw new Exception(text);}
        async Task Until(Func<bool> predicate){var sw=Stopwatch.StartNew();while(!predicate()){if(sw.Elapsed.TotalSeconds>15)throw new TimeoutException("Fixture server timed out");await Task.Delay(50);}}
        ProcessStartInfo Launcher(){var p=new ProcessStartInfo(Environment.ProcessPath!);p.ArgumentList.Add("--mock-server");return p;}
        WriteWorld(Path.Combine(root,"server","world"),"Original");File.WriteAllText(Path.Combine(root,"server","eula.txt"),"eula=true");
        var originalBytes=File.ReadAllBytes(Path.Combine(root,"server","world","level.dat"));
        using var manager=new ServerManager(root,Launcher);
        Assert(manager.ServerDir==Path.Combine(root,"server")&&File.ReadAllBytes(Path.Combine(manager.WorldDir,"level.dat")).SequenceEqual(originalBytes),"Legacy migration changed files");checks.Add("Existing server is registered in place; original world and settings remain in their existing folders");
        var profileRoot=Path.Combine(root,"curseforge","Fixture pack");Directory.CreateDirectory(profileRoot);Directory.CreateDirectory(Path.Combine(profileRoot,"mods"));
        File.WriteAllText(Path.Combine(profileRoot,"minecraftinstance.json"),JsonSerializer.Serialize(new{name="Second pack",gameVersion="1.21.1",baseModLoader=new{name="neoforge-21.1.249"},installedModpack=new{name="Second pack",addonID=123,installedFile=new{fileName="Second-pack-1.zip",serverPackFileId=456}}}));
        File.WriteAllText(Path.Combine(profileRoot,"manifest.json"),"{\"version\":\"1\"}");
        var source=CurseForgeProfiles.Read(profileRoot);_ = CurseForgeProfiles.Discover(new[]{Path.GetDirectoryName(profileRoot)!});
        Assert(!Directory.Exists(Path.Combine(root,"profiles")),"Discovery prepared servers without a click");checks.Add("CurseForge discovery reads metadata only; no server copies or downloads are created");
        Directory.CreateDirectory(Path.Combine(root,"runtime","bin"));File.WriteAllText(Path.Combine(root,"runtime","bin","java.exe"),"fixture-placeholder-not-executed");
        var zip=Path.Combine(root,"server-pack.zip");using(var z=ZipFile.Open(zip,ZipArchiveMode.Create)){
            using(var w=new StreamWriter(z.CreateEntry("pack/mods/fixture.jar").Open()))w.Write("fixture-mod");
            using(var w=new StreamWriter(z.CreateEntry("pack/libraries/net/neoforged/neoforge/21.1.249/win_args.txt").Open()))w.Write("fixture-arguments");
        }
        await manager.SetUpPackAsync(source,CancellationToken.None,zip);Assert(manager.Library.Data.Profiles.Count==2&&manager.Profile.LegacyLocation,"Preparing a pack activated it unexpectedly");
        var second=manager.Library.Data.Profiles.Single(p=>!p.LegacyLocation);Assert(File.Exists(Path.Combine(manager.Library.ProfileRoot(second),"server","mods","fixture.jar")),"Server package missing");checks.Add("Explicit setup imports a wrapped server ZIP into a separate ready server without changing the active pack");
        await manager.StartAsync();await Until(()=>manager.State==ServerState.Running);
        bool rejected=false;try{await manager.CreateWorldAsync("While playing");}catch(InvalidOperationException){rejected=true;}Assert(rejected,"Live world mutation allowed");
        await manager.SelectAsync(second.Id,"world");Assert(!manager.HasProcess&&manager.Profile.Id==second.Id&&Directory.GetFiles(Path.Combine(root,"backups"),"*.zip").Length>0,"Switch did not stop and back up old server");
        Assert(File.ReadAllBytes(Path.Combine(root,"server","world","level.dat")).SequenceEqual(originalBytes),"Switch changed original world");checks.Add("Switching a running server stops it, verifies a backup, preserves the old save, and selects the other pack without starting it");
        await manager.StartAsync();await Until(()=>manager.State==ServerState.Running);await manager.StopAsync();
        Assert(File.Exists(Path.Combine(manager.WorldDir,"level.dat")),"New world did not generate");
        await manager.CreateWorldAsync("Another world");var newWorld=second.Worlds.Last();await manager.SelectAsync(second.Id,newWorld.Folder);await manager.StartAsync();await Until(()=>manager.State==ServerState.Running);
        await manager.BackupAsync();int worldBackupCount=Directory.GetFiles(manager.BackupDir,"*.zip").Length;await manager.StopAsync();
        Assert(Directory.GetFiles(manager.BackupDir,"*.zip").Length==worldBackupCount,"Stopping added an unwanted world backup");
        Assert(manager.Properties()["level-name"]==newWorld.Folder&&Directory.GetFiles(manager.BackupDir,"*.zip").Length>0,"World selection or backup location wrong");checks.Add("Two worlds within one pack launch from distinct folders and keep separate backups");
        var foreign=Directory.GetFiles(Path.Combine(root,"backups"),"*.zip")[0];var cross=Path.Combine(manager.BackupDir,"cross-pack.zip");File.Copy(foreign,cross);
        rejected=false;try{await manager.RestoreAsync(cross);}catch(IOException){rejected=true;}Assert(rejected,"Cross-pack restore accepted");checks.Add("Restoring a backup from another pack is rejected");
        var save=Path.Combine(profileRoot,"saves","My save");WriteWorld(save,"My save");var bytes=File.ReadAllBytes(Path.Combine(save,"level.dat"));var info=WorldImport.Read(save);
        await manager.ImportWorldAsync(save,"Imported save");var imported=second.Worlds.Last();var copied=Path.Combine(manager.ServerDir,imported.Folder);
        Assert(File.ReadAllBytes(Path.Combine(save,"level.dat")).SequenceEqual(bytes)&&File.ReadAllBytes(Path.Combine(copied,"level.dat")).SequenceEqual(bytes),"Import changed the source or copied world metadata");
        using(var player=File.OpenRead(Path.Combine(copied,"playerdata",info.PlayerUuid+".dat")))using(var gzip=new GZipStream(player,CompressionMode.Decompress))using(var data=new MemoryStream()){gzip.CopyTo(data);Assert(data.ToArray().SequenceEqual(new byte[]{10,0,0}.Concat(info.PlayerPayload!)),"Embedded player data lost");}
        checks.Add("Singleplayer import preserves source bytes and carries the exact embedded player inventory and mod data into multiplayer playerdata");
        var wrong=Path.Combine(root,"wrong-version");WriteWorld(wrong,"Wrong","1.20.1");rejected=false;try{await manager.ImportWorldAsync(wrong,"Wrong");}catch(InvalidDataException){rejected=true;}Assert(rejected,"Wrong Minecraft version accepted");
        var malicious=Path.Combine(root,"unsafe.zip");using(var z=ZipFile.Open(malicious,ZipArchiveMode.Create)){using var w=new StreamWriter(z.CreateEntry("../escaped.txt").Open());w.Write("bad");}
        rejected=false;try{PackInstaller.Extract(malicious,Path.Combine(root,"unsafe-stage"),CancellationToken.None);}catch(InvalidDataException){rejected=true;}Assert(rejected&&!File.Exists(Path.Combine(root,"escaped.txt")),"Archive traversal allowed");checks.Add("Wrong-version worlds and archive path traversal are rejected");
        await manager.SelectAsync("original-atm10","world");Assert(manager.Config.WorldName=="New world"&&File.ReadAllBytes(Path.Combine(manager.WorldDir,"level.dat")).SequenceEqual(originalBytes),"Original server did not restore correctly");
        Assert(manager.SelectedWorldFolder(second)==newWorld.Folder,"A different pack's last selected world was forgotten");
        using(var reopened=new ServerManager(root,Launcher))Assert(reopened.Profile.Id=="original-atm10"&&reopened.Library.Data.Profiles.Count==2&&reopened.Library.Data.Profiles.Single(p=>p.Id==second.Id).Worlds.Count==3,"Library persistence failed");checks.Add("Pack and world selections persist across reopening Harbor, and switching back preserves the original save");
        Assert(PackInstaller.JavaMajor("1.12.2")==8&&PackInstaller.JavaMajor("1.17.1")==16&&PackInstaller.JavaMajor("1.20.1")==17&&PackInstaller.JavaMajor("1.21.1")==21&&PackInstaller.JavaMajor("26.1.2")==25,"Java mapping failed");
        ServerManager.WriteJson(report,new{passed=true,checkedUtc=DateTime.UtcNow,checks,fixtureRoot=root,realWorldModified=false});
    }
}
