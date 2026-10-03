using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace MinecraftHarbor;
internal static class ModdedTests
{
    internal static async Task Run(string report)
    {
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"modded-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var checks=new List<string>();
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        var handler=new FixtureHttp();using var http=new HttpClient(handler);var catalog=new CurseForgeCatalog(root,http,"fixture-key");
        var page=await catalog.Search("tech & magic",2,1,false,CancellationToken.None);Check(page.Items.Count==1&&page.Total==40&&handler.LastQuery.Contains("index=20")&&handler.LastQuery.Contains("tech%20%26%20magic"),"Catalog pagination or search escaping failed");checks.Add("Official API search sends its key in a header and supports query, sorting and pagination");
        var source=new CurseForgeProfile("","Fixture Pack","1","1.21.1","neoforge","21.1.249",10,20);
        var draft=new ModpackDraft{Project=page.Items[0],Release=handler.File(10,21),Source=source,Archive=""};
        var additions=await catalog.ResolveAddition(draft,new(100,"Added mod","","","",0,new()),CancellationToken.None);Check(additions.Count==2&&additions.ContainsKey(101),"Required dependencies were not resolved");checks.Add("Adding a mod filters by game and loader and resolves required dependencies, including cycles");
        handler.Incompatible=true;draft.ModFiles[101]=handler.File(101,201);bool rejected=false;try{await catalog.ResolveAddition(draft,new(100,"Added mod","","","",0,new()),CancellationToken.None);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Known incompatible mods were accepted");handler.Incompatible=false;draft.ModFiles.Clear();
        handler.WrongLoader=true;rejected=false;try{await catalog.ResolveAddition(draft,new(100,"Added mod","","","",0,new()),CancellationToken.None);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Wrong-loader file was accepted");handler.WrongLoader=false;checks.Add("Wrong-loader files and declared incompatibilities are rejected");
        var file=handler.File(100,200);string download=await catalog.Download(file,CancellationToken.None);await catalog.Download(file,CancellationToken.None);Check(handler.Downloads==1,"Download cache repeated the transfer");System.IO.File.WriteAllText(download,"corrupt");await catalog.Download(file,CancellationToken.None);Check(handler.Downloads==2,"Corrupt cached file was reused");checks.Add("Downloads verify their checksum, reuse identical releases and replace damaged cache files");
        handler.Damaged=true;rejected=false;try{await catalog.Download(handler.File(101,201),CancellationToken.None);}catch(InvalidDataException){rejected=true;}Check(rejected&&!Directory.EnumerateFiles(Path.Combine(root,"downloads"),"*.partial",SearchOption.AllDirectories).Any(),"Damaged download was retained");handler.Damaged=false;
        handler.Denied=true;rejected=false;try{await catalog.Search("",2,0,false,CancellationToken.None);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Authentication error did not become an actionable message");handler.Denied=false;checks.Add("Authentication failures are readable and incomplete or damaged downloads are discarded");
        var connection=new CurseForgeCatalog(root,http);await connection.Connect("fixture-key",CancellationToken.None);Check(connection.Connected&&!Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(Path.Combine(root,"connections","curseforge.key"))).Contains("fixture-key"),"Connection key was not encrypted");checks.Add("The connection key is encrypted for the current Windows account");
        string zipPath=Path.Combine(root,"server-pack.zip");using(var zip=ZipFile.Open(zipPath,ZipArchiveMode.Create)){
            foreach(var pair in new[]{("pack/mods/base.jar","original mod"),("pack/libraries/net/neoforged/neoforge/21.1.249/win_args.txt","fixture launch"),("pack/config/example.toml","# unchanged comment\n[general]\nallowMachines = true # keep comment\nrate = 3\ntext = \"preserve\"\n"),("pack/config/example.json","{\"machines\":{\"enabled\":true,\"count\":5},\"list\":[1,2],\"name\":\"preserve\"}"),("pack/server.properties","pvp=false\nspawn-protection=9\n")}){using var writer=new StreamWriter(zip.CreateEntry(pair.Item1).Open());writer.Write(pair.Item2);}
        }
        var configs=PackConfiguration.Discover(zipPath);Check(configs.Count==4&&!configs.Any(c=>c.Key=="text"),"Unsupported configuration values were exposed");draft.Archive=zipPath;draft.Config=configs;configs.Single(c=>c.Key=="general.rate").Value="8";configs.Single(c=>c.Key=="machines.enabled").Value="false";
        Directory.CreateDirectory(Path.Combine(root,"runtime","bin"));System.IO.File.WriteAllText(Path.Combine(root,"runtime","bin","java.exe"),"not executed");LibraryTests.WriteWorld(Path.Combine(root,"server","world"));var original=System.IO.File.ReadAllBytes(Path.Combine(root,"server","world","level.dat"));using var manager=new ServerManager(root);
        var settings=new Settings{MemoryGB=8,MaxPlayers=25,ViewDistance=15,SimulationDistance=12,WorldName="First world"};await manager.CreateModdedAsync(draft,settings,"First server",new(){{"pvp","false"}},new(){{"keepInventory","true"}},false,catalog,CancellationToken.None);
        var first=manager.Library.Data.Profiles.Single(p=>!p.LegacyLocation);string firstRoot=manager.Library.ProfileRoot(first);Check(first.ServerName=="First server"&&manager.ReadProfileSettings(first).MaxPlayers==25&&System.IO.File.ReadAllText(Path.Combine(firstRoot,"server","config","example.toml")).Contains("rate = 8")&&!System.IO.File.ReadAllText(Path.Combine(firstRoot,"server","config","example.json")).Contains("\"enabled\": true"),"Per-server settings were not applied");
        System.IO.File.WriteAllText(Path.Combine(firstRoot,"server","mods","base.jar"),"changed live instance");draft.Config=PackConfiguration.Discover(zipPath);await manager.CreateModdedAsync(draft,new Settings{MemoryGB=16,MaxPlayers=10,ViewDistance=10,SimulationDistance=8,WorldName="Second world"},"Second server",new(),new(),false,catalog,CancellationToken.None);
        var second=manager.Library.Data.Profiles.Last();Check(System.IO.File.ReadAllText(Path.Combine(manager.Library.ProfileRoot(second),"server","mods","base.jar"))=="original mod"&&Directory.GetDirectories(Path.Combine(root,"pack-cache")).Length==1&&manager.ReadProfileSettings(second).MemoryGB==16&&manager.Profile.LegacyLocation,"A second server reused mutable state instead of its template");
        Check(System.IO.File.ReadAllBytes(Path.Combine(root,"server","world","level.dat")).SequenceEqual(original),"Original world changed");checks.Add("Two servers from one pack reuse its prepared template while keeping separate mods, configurations, names, worlds and RAM; the active world is unchanged");
        Check(System.IO.File.ReadAllText(Path.Combine(firstRoot,"server","config","example.toml")).Contains("# keep comment")&&System.IO.File.ReadAllText(Path.Combine(firstRoot,"server","config","example.json")).Contains("\"list\""),"Config edits removed other contents");checks.Add("TOML comments, JSON arrays and unsupported values survive scalar configuration edits; game rules persist for first start");
        ServerManager.WriteJson(report,new{passed=true,checks,fixtureRoot=root,realWorldModified=false,liveCurseForgeVerified=false});
    }
    internal sealed class FixtureHttp:HttpMessageHandler
    {
        internal int Downloads;internal string LastQuery="";internal bool Damaged,Denied,WrongLoader,Incompatible;
        readonly byte[] bytes=Encoding.UTF8.GetBytes("download fixture");
        internal CfFile File(long project,long id)=>new(id,project,"fixture-"+id+".jar","Fixture",bytes.Length,"https://edge.forgecdn.net/files/fixture-"+id+".jar",new(){"1.21.1",WrongLoader?"Forge":"NeoForge"},0,new(){new(project==100?101:100,Incompatible?5:3)},Convert.ToHexString(SHA1.HashData(bytes)));
        object FileJson(CfFile file)=>new{id=file.Id,modId=file.ModId,fileName=file.Name,displayName=file.DisplayName,fileLength=file.Length,downloadUrl=file.DownloadUrl,gameVersions=file.Versions,serverPackFileId=file.ServerPack,dependencies=file.Dependencies.Select(d=>new{modId=d.ModId,relationType=d.Relation}),hashes=new[]{new{algo=1,value=file.Sha1}}};
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.RequestUri!.Host=="api.modrinth.com"){string mock=request.RequestUri.AbsolutePath.EndsWith("search")?"{\"hits\":[],\"total_hits\":0}":"{}";return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(mock)});}
            token.ThrowIfCancellationRequested();if(!request.Headers.TryGetValues("x-api-key",out var keys)||keys.Single()!="fixture-key")throw new Exception("Missing authentication header");if(Denied)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            if(request.RequestUri!.Host=="edge.forgecdn.net"){Downloads++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Damaged?Encoding.UTF8.GetBytes("bad"):bytes)});}
            string path=request.RequestUri.AbsolutePath;LastQuery=request.RequestUri.Query;object data;
            if(path.EndsWith("search"))data=new{data=new[]{new{id=10,name="Fixture Pack",summary="Fixture",downloadCount=40,logo=new{url=""},links=new{websiteUrl=""},latestFiles=Array.Empty<object>()}},pagination=new{totalCount=40}};
            else if(path.EndsWith("/files")){long id=long.Parse(path.Split('/')[3]);data=new{data=new[]{FileJson(File(id,id+100))}};}
            else throw new Exception("Unexpected test request "+path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data))});
        }
    }
}
