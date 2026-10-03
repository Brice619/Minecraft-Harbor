using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace MinecraftHarbor;
internal static class BackupTests
{
    public static async Task Run(string report)
    {
        var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"backup-fixture-"+Guid.NewGuid().ToString("N"));
        LibraryTests.WriteWorld(Path.Combine(root,"server","world"));File.WriteAllText(Path.Combine(root,"server","eula.txt"),"eula=true");
        var now=new DateTime(2026,9,30,12,0,0,DateTimeKind.Utc);var checks=new List<string>();
        ProcessStartInfo Launcher(){var p=new ProcessStartInfo(Environment.ProcessPath!);p.ArgumentList.Add("--mock-server");return p;}
        using var s=new ServerManager(root,Launcher,()=>now,TimeSpan.FromSeconds(1));
        void Assert(bool pass,string text){if(!pass)throw new Exception(text);}
        async Task Until(Func<bool> predicate){var sw=Stopwatch.StartNew();while(!predicate()){if(sw.Elapsed.TotalSeconds>10)throw new TimeoutException("Mock server did not start");await Task.Delay(20);}}
        string Fixture(string name,string reason,int hours,string? profile=null){
            var file=Path.Combine(s.BackupDir,name+".zip");using var zip=ZipFile.Open(file,ZipArchiveMode.Create);
            zip.CreateEntryFromFile(Path.Combine(s.WorldDir,"level.dat"),"world/level.dat");
            using var writer=new StreamWriter(zip.CreateEntry("harbor-backup.json").Open());writer.Write(JsonSerializer.Serialize(new{reason,createdUtc=now.AddHours(-hours),profileId=profile??s.Profile.Id,worldFolder=s.Config.WorldFolder}));return file;
        }
        try{
            var original=Fixture("original-singleplayer-important","manual",24);
            var expiredManual=Fixture("old-manual","manual",7);var expiredAuto=Fixture("old-hourly","hourly",11);
            var recent=Fixture("recent-manual","manual",2);var foreign=Fixture("foreign-pack","hourly",24,"another-pack");
            var malformed=Path.Combine(s.BackupDir,"foreign-malformed.zip");using(var zip=ZipFile.Open(malformed,ZipArchiveMode.Create)){using var w=new StreamWriter(zip.CreateEntry("harbor-backup.json").Open());w.Write("{\"reason\":4}");}
            await s.StartAsync();await Until(()=>s.State==ServerState.Running);var process=File.ReadAllText(Path.Combine(root,"server-process.json"));
            now=now.AddMinutes(59);Assert(!await s.TryAutomaticBackupAsync(),"Backup ran before one hour");
            now=now.AddMinutes(1);Assert(await s.TryAutomaticBackupAsync(),"Hourly backup did not run");
            Assert(s.State==ServerState.Running&&File.ReadAllText(Path.Combine(root,"server-process.json"))==process,"Hourly backup restarted or stopped server");
            Assert(File.ReadAllText(Path.Combine(s.ServerDir,"mock-saving.txt"))=="on"&&!s.SavingNeedsResume,"Saving was not resumed");
            Assert(File.ReadAllLines(Path.Combine(s.ServerDir,"mock-commands.log")).SequenceEqual(new[]{"save-off","save-all flush","save-on"}),"Save protocol order incorrect");
            Assert(!await s.TryAutomaticBackupAsync()&&s.NextAutomaticBackupUtc==now.AddHours(1),"Backup duplicated or schedule incorrect");
            var hourly=Directory.GetFiles(s.BackupDir,"*_hourly.zip").Single();using(var zip=ZipFile.OpenRead(hourly)){using var reader=new StreamReader(zip.GetEntry("world/flushed-state.txt")!.Open());Assert(reader.ReadToEnd()=="written-by-confirmed-flush","Archive preceded save acknowledgement");}
            checks.Add("At one hour, saves are confirmed before copying; the server PID stays unchanged, normal saving resumes, and the next backup is scheduled one hour later");
            Assert(!File.Exists(expiredManual)&&!File.Exists(expiredAuto)&&File.Exists(original)&&File.Exists(recent)&&File.Exists(foreign)&&File.Exists(malformed),"Retention removed protected files or retained expired ordinary backups");
            checks.Add("Five-hour retention covers manual and automatic backups; original, recent, foreign and unrecognized archives are preserved");
            var expiredFailure=Fixture("expired-kept-until-success","manual",8);int before=Directory.GetFiles(s.BackupDir,"*.zip").Length;
            File.WriteAllText(Path.Combine(s.ServerDir,"mock-reject-flush"),"test");
            bool rejected=false;try{await s.BackupAsync();}catch(TimeoutException){rejected=true;}File.Delete(Path.Combine(s.ServerDir,"mock-reject-flush"));
            Assert(rejected&&File.Exists(expiredFailure)&&Directory.GetFiles(s.BackupDir,"*.zip").Length==before&&!s.SavingNeedsResume,"Failed save published/pruned backups or left saving disabled");
            checks.Add("Missing save confirmation and a spoofed chat message abort the backup, preserve prior backups, and restore saving");
            var locked=Path.Combine(s.WorldDir,"locked.dat");File.WriteAllText(locked,"cannot-copy");
            using(var held=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
                rejected=false;try{await s.BackupAsync();}catch(IOException){rejected=true;}
                Assert(rejected&&!s.SavingNeedsResume&&File.Exists(expiredFailure),"Copy failure left saving disabled or pruned previous backups");
            }
            File.Delete(locked);checks.Add("A world-copy failure re-enables saving, publishes no incomplete ZIP, and keeps old recovery points");
            File.WriteAllText(Path.Combine(s.ServerDir,"mock-reject-save-on"),"test");
            rejected=false;try{await s.BackupAsync();}catch(TimeoutException){rejected=true;}
            Assert(rejected&&s.SavingNeedsResume&&Directory.GetFiles(s.BackupDir,"*.zip").Length==before,"Unconfirmed save-on reported success");
            File.Delete(Path.Combine(s.ServerDir,"mock-reject-save-on"));now=now.AddSeconds(11);await s.TryAutomaticBackupAsync();
            Assert(!s.SavingNeedsResume&&s.State==ServerState.Running,"Automatic save-on recovery failed");checks.Add("Unconfirmed save-on is surfaced and retried automatically without stopping the server");
            await s.BackupAsync();Assert(!File.Exists(expiredFailure)&&File.ReadAllText(Path.Combine(root,"server-process.json"))==process,"Manual online backup or cleanup failed");
            Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(s.ProfileRoot,"backup-staging")).Any(),"Temporary copies were left behind");
            var archives=Directory.GetFiles(s.BackupDir,"*.zip").Order().ToArray();
            await s.StopAsync();Assert(!s.HasProcess&&s.State==ServerState.Stopped,"Stop did not finish gracefully");
            Assert(Directory.GetFiles(s.BackupDir,"*.zip").Order().SequenceEqual(archives),"Stopping created an unwanted backup");
            var commands=File.ReadAllText(Path.Combine(s.ServerDir,"mock-commands.log"));
            var world=File.ReadAllBytes(Path.Combine(s.WorldDir,"level.dat"));var modified=File.GetLastWriteTimeUtc(Path.Combine(s.WorldDir,"level.dat"));
            await s.CloseAsync();await s.StopAsync();
            Assert(File.ReadAllText(Path.Combine(s.ServerDir,"mock-commands.log"))==commands&&File.ReadAllBytes(Path.Combine(s.WorldDir,"level.dat")).SequenceEqual(world)&&File.GetLastWriteTimeUtc(Path.Combine(s.WorldDir,"level.dat"))==modified&&Directory.GetFiles(s.BackupDir,"*.zip").Order().SequenceEqual(archives),"Offline close or stop touched the world, sent commands, or created backups");
            checks.Add("Server stop is graceful without an extra backup; closing or stopping an already stopped server leaves world files, commands and archives untouched");
            ServerManager.WriteJson(report,new{passed=true,checkedUtc=DateTime.UtcNow,checks,fixtureRoot=root,realWorldModified=false});
        }finally{if(s.HasProcess)await s.CloseAsync();}
    }
}
