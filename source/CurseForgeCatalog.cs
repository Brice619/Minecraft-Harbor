using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MinecraftHarbor;

internal sealed record CfProject(long Id,string Name,string Summary,string Logo,string Website,double Downloads,List<CfFile> Latest)
{
    internal string ModrinthId {get;init;}="";
    internal string Source=>ModrinthId.Length>0?"Modrinth":"CurseForge";
    internal string ServerSide {get;init;}="";
}
internal sealed record CfFile(long Id,long ModId,string Name,string DisplayName,long Length,string? DownloadUrl,List<string> Versions,long ServerPack,List<CfDependency> Dependencies,string? Sha1)
{
    internal string ModrinthId {get;init;}="";
    internal string Sha512 {get;init;}="";
    internal string ModrinthProject {get;init;}="";
}
internal sealed record CfDependency(long ModId,int Relation)
{
    internal string ModrinthProject {get;init;}="";
    internal string ModrinthVersion {get;init;}="";
}
internal sealed record CfPage(List<CfProject> Items,int Total){internal int ProviderTotal {get;init;} internal int PagingTotal=>ProviderTotal>0?ProviderTotal:Total;}
internal sealed record CfIncluded(long ProjectId,long FileId);
internal sealed class ModpackDraft
{
    internal required CfProject Project;
    internal required CfFile Release;
    internal required CurseForgeProfile Source;
    internal required string Archive;
    internal List<CfIncluded> Included=new();
    internal Dictionary<long,CfProject> Mods=new();
    internal Dictionary<long,CfFile> ModFiles=new();
    internal HashSet<long> Removed=new();
    internal Dictionary<long,CfFile> Added=new();
    internal Dictionary<long,LocalMod> LocalMods=new();
    internal List<PackConfigValue> Config=new();
    internal string ModrinthProject="";
    internal bool MetadataResolved;
    internal Dictionary<long,long> Aliases=new();
}
internal sealed record LocalMod(string File,string Name,string ModId);

internal sealed class CurseForgeCatalog
{
    readonly string root;
    readonly HttpClient http;
    readonly string? testKey;
    internal readonly ModrinthCatalog Modrinth;
    static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};
    internal CurseForgeCatalog(string root,HttpClient? http=null,string? testKey=null){this.root=root;this.http=http??new HttpClient{Timeout=TimeSpan.FromMinutes(15)};this.testKey=testKey;Modrinth=new(root,http);}
    string KeyPath=>Path.Combine(root,"connections","curseforge.key");
    internal bool Connected=>testKey!=null||File.Exists(KeyPath);
    string Key=>testKey??(File.Exists(KeyPath)?Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyPath),null,DataProtectionScope.CurrentUser)):throw new InvalidOperationException("Connect CurseForge to browse and download its catalog."));
    internal async Task Connect(string key,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(key)||key.Any(char.IsWhiteSpace))throw new ArgumentException("Enter the developer API key issued by CurseForge.");
        using var check=new HttpRequestMessage(HttpMethod.Get,"https://api.curseforge.com/v1/mods/search?gameId=432&classId=4471&pageSize=1");check.Headers.Add("x-api-key",key);
        using var response=await http.SendAsync(check,token);await Check(response);
        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);File.WriteAllBytes(KeyPath,ProtectedData.Protect(Encoding.UTF8.GetBytes(key),null,DataProtectionScope.CurrentUser));
    }
    static async Task Check(HttpResponseMessage response)
    {
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new InvalidOperationException("CurseForge did not accept this connection. Check your approved developer API key.");
        if((int)response.StatusCode==429)throw new IOException("CurseForge is receiving too many requests. Please try again shortly.");
        await Task.CompletedTask;response.EnsureSuccessStatusCode();
    }
    async Task<JsonDocument> Request(string path,CancellationToken token,object? body=null)
    {
        using var request=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,"https://api.curseforge.com/v1/"+path);request.Headers.Add("x-api-key",Key);
        if(body!=null)request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(request,token);await Check(response);return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }
    static string Text(JsonElement e,string k)=>e.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()!:"";
    static long Number(JsonElement e,string k)=>e.TryGetProperty(k,out var v)&&v.TryGetInt64(out var n)?n:0;
    internal static CfFile FileInfo(JsonElement e)=>new(Number(e,"id"),Number(e,"modId"),Text(e,"fileName"),Text(e,"displayName"),Number(e,"fileLength"),e.TryGetProperty("downloadUrl",out var u)&&u.ValueKind==JsonValueKind.String?u.GetString():null,
        e.GetProperty("gameVersions").EnumerateArray().Select(v=>v.GetString()!).ToList(),Number(e,"serverPackFileId"),e.TryGetProperty("dependencies",out var d)?d.EnumerateArray().Select(v=>new CfDependency(Number(v,"modId"),(int)Number(v,"relationType"))).ToList():new(),
        e.TryGetProperty("hashes",out var h)?h.EnumerateArray().Where(v=>Number(v,"algo")==1).Select(v=>Text(v,"value")).FirstOrDefault():null);
    internal static CfProject ProjectInfo(JsonElement e)=>new(Number(e,"id"),Text(e,"name"),Text(e,"summary"),e.TryGetProperty("logo",out var logo)&&logo.ValueKind==JsonValueKind.Object?Text(logo,"url"):"",e.TryGetProperty("links",out var links)?Text(links,"websiteUrl"):"",e.TryGetProperty("downloadCount",out var count)?count.GetDouble():0,e.TryGetProperty("latestFiles",out var f)?f.EnumerateArray().Select(FileInfo).ToList():new());
    internal static int LoaderType(string loader)=>loader.ToLowerInvariant() switch{"forge"=>1,"fabric"=>4,"neoforge"=>6,_=>throw new InvalidDataException("This mod loader is not supported yet.")};
    internal async Task<CfPage> Search(string text,int sort,int page,bool mods,CancellationToken token,string? minecraft=null,string? loader=null)
    {
        string query=$"mods/search?gameId=432&classId={(mods?6:4471)}&pageSize=20&index={Math.Clamp(page,0,499)*20}&sortField={sort}&sortOrder={(sort==4?"asc":"desc")}&searchFilter={Uri.EscapeDataString(text)}";
        if(minecraft!=null)query+="&gameVersion="+Uri.EscapeDataString(minecraft);if(loader!=null)query+="&modLoaderType="+LoaderType(loader);
        using var doc=await Request(query,token);return new(doc.RootElement.GetProperty("data").EnumerateArray().Select(ProjectInfo).ToList(),(int)Number(doc.RootElement.GetProperty("pagination"),"totalCount"));
    }
    internal async Task<List<CfFile>> Files(long project,CancellationToken token,string? minecraft=null,string? loader=null)
    {
        string query=$"mods/{project}/files?pageSize=50";if(minecraft!=null)query+="&gameVersion="+Uri.EscapeDataString(minecraft);if(loader!=null)query+="&modLoaderType="+LoaderType(loader);
        using var doc=await Request(query,token);return doc.RootElement.GetProperty("data").EnumerateArray().Select(FileInfo).ToList();
    }
    internal async Task<CfFile> GetFile(long project,long file,CancellationToken token){using var d=await Request($"mods/{project}/files/{file}",token);var result=FileInfo(d.RootElement.GetProperty("data"));if(result.Id!=file||result.ModId!=project)throw new InvalidDataException("CurseForge returned a different file.");return result;}
    internal async Task<Dictionary<long,CfProject>> GetProjects(IEnumerable<long> ids,CancellationToken token)
    {
        var result=new Dictionary<long,CfProject>();foreach(var batch in ids.Where(id=>id>0).Distinct().Chunk(50)){using var d=await Request("mods",token,new{modIds=batch});foreach(var p in d.RootElement.GetProperty("data").EnumerateArray().Select(ProjectInfo))result[p.Id]=p;}return result;
    }
    internal async Task<CfPage> CombinedSearch(string text,int sort,int page,bool mods,CancellationToken token,string? minecraft=null,string? loader=null)
    {
        var mr=Modrinth.Search(text,sort,page,mods,token,minecraft,loader);if(!Connected)return await mr;
        var cf=Search(text,sort,page,mods,token,minecraft,loader);await Task.WhenAll(mr,cf);return new((await mr).Items.Concat((await cf).Items).ToList(),(await mr).Total+(await cf).Total){ProviderTotal=Math.Max((await mr).Total,(await cf).Total)};
    }
    internal async Task<string> Download(CfFile file,CancellationToken token,Action<string>? progress=null)
    {
        if(file.ModrinthId.Length>0)return await Modrinth.Download(file,token,progress);
        if(file.Id<=0||file.ModId<=0||Path.GetFileName(file.Name)!=file.Name||file.Name.Contains(':')||file.Length<=0)throw new InvalidDataException("Invalid CurseForge download metadata.");
        string path=Path.Combine(root,"downloads","curseforge",$"{file.ModId}-{file.Id}",file.Name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        async Task<bool> Valid(string target){if(!System.IO.File.Exists(target)||new System.IO.FileInfo(target).Length!=file.Length)return false;using var stream=System.IO.File.OpenRead(target);return file.Sha1!=null&&Convert.ToHexString(await SHA1.HashDataAsync(stream,token)).Equals(file.Sha1,StringComparison.OrdinalIgnoreCase);}
        if(await Valid(path)){progress?.Invoke("Using the cached "+file.DisplayName+"…");return path;}
        string? url=file.DownloadUrl;if(string.IsNullOrEmpty(url)){using var d=await Request($"mods/{file.ModId}/files/{file.Id}/download-url",token);url=d.RootElement.GetProperty("data").GetString();}
        if(string.IsNullOrEmpty(url))throw new InvalidOperationException("The author has not enabled downloads for third-party tools. Open this project's CurseForge page to download it.");
        var uri=new Uri(url);if(uri.Scheme!="https"||!(uri.Host.Equals("forgecdn.net")||uri.Host.EndsWith(".forgecdn.net",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Unexpected CurseForge download address.");
        using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.Add("x-api-key",Key);using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);await Check(response);
        if(new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace<file.Length+512L*1024*1024)throw new IOException("Not enough free space for this download.");
        string partial=path+".partial";try{
            using(var input=await response.Content.ReadAsStreamAsync(token))using(var output=System.IO.File.Create(partial)){byte[] buffer=new byte[131072];long total=0;int n;var watch=System.Diagnostics.Stopwatch.StartNew();while((n=await input.ReadAsync(buffer,token))>0){total+=n;if(total>file.Length||total>20L*1024*1024*1024)throw new IOException("The download exceeds its declared size.");await output.WriteAsync(buffer.AsMemory(0,n),token);if(watch.ElapsedMilliseconds>700){progress?.Invoke($"Downloading {total/1048576d:N0} / {file.Length/1048576d:N0} MB…");watch.Restart();}}}
            if(!await Valid(partial))throw new InvalidDataException("The CurseForge download failed its checksum check.");System.IO.File.Move(partial,path,true);return path;
        }finally{if(System.IO.File.Exists(partial))System.IO.File.Delete(partial);}
    }
    internal async Task<string> Artwork(CfProject project,CancellationToken token)
    {
        string path=Path.Combine(root,"artwork","curseforge",project.Id+".png");if(CatalogArtwork.Valid(path))return path;
        if(!Uri.TryCreate(project.Logo,UriKind.Absolute,out var url)||url.Scheme!="https"||!(url.Host.EndsWith(".forgecdn.net",StringComparison.OrdinalIgnoreCase)||url.Host=="cdn.modrinth.com"))return "";
        using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();using var input=await response.Content.ReadAsStreamAsync(token);using var data=new MemoryStream();byte[] buffer=new byte[16384];int n;while((n=await input.ReadAsync(buffer,token))>0){if(data.Length+n>10*1024*1024)throw new IOException("Artwork is too large.");data.Write(buffer,0,n);}data.Position=0;
        await Task.Run(()=>CatalogArtwork.SavePng(data.ToArray(),path,token),token);return path;
    }
    internal async Task<ModpackDraft> Inspect(CfProject project,CfFile release,CancellationToken token,Action<string> progress)
    {
        if(project.ModrinthId.Length>0)return await Modrinth.Inspect(project,release,token,progress);
        if(release.ServerPack<=0)throw new InvalidOperationException("This release does not include an author-provided server pack. Choose another release.");
        var client=await Download(release,token,progress);using var zip=ZipFile.OpenRead(client);var manifest=zip.GetEntry("manifest.json")??throw new InvalidDataException("This pack has no CurseForge manifest.");if(manifest.Length>8*1024*1024)throw new InvalidDataException("The pack manifest is too large.");using var reader=new StreamReader(manifest.Open());using var json=JsonDocument.Parse(await reader.ReadToEndAsync(token));var j=json.RootElement;
        string minecraft=Text(j.GetProperty("minecraft"),"version");var loaders=j.GetProperty("minecraft").GetProperty("modLoaders").EnumerateArray().ToList();var primary=loaders.FirstOrDefault(l=>l.TryGetProperty("primary",out var p)&&p.GetBoolean());if(primary.ValueKind==JsonValueKind.Undefined)primary=loaders.First();string loader=Text(primary,"id");int split=loader.IndexOf('-');if(split<=0)throw new InvalidDataException("The pack doesn't identify its loader.");
        var serverFile=await GetFile(project.Id,release.ServerPack,token);string archive=await Download(serverFile,token,progress);
        var draft=new ModpackDraft{Project=project,Release=release,Archive=archive,Source=new("",project.Name,Text(j,"version"),minecraft,loader[..split].ToLowerInvariant(),loader[(split+1)..],project.Id,serverFile.Id),Included=j.GetProperty("files").EnumerateArray().Select(f=>new CfIncluded(Number(f,"projectID"),Number(f,"fileID"))).ToList()};
        _=LoaderType(draft.Source.Loader);draft.Config=PackConfiguration.Discover(archive);return draft;
    }
    internal async Task ResolveIncluded(ModpackDraft draft,CancellationToken token)
    {
        draft.Mods=await GetProjects(draft.Included.Select(f=>f.ProjectId),token);
        using var zip=ZipFile.OpenRead(draft.Archive);var names=zip.Entries.Select(e=>Path.GetFileName(e.FullName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var included in draft.Included){var file=await GetFile(included.ProjectId,included.FileId,token);if(names.Contains(file.Name))draft.ModFiles[included.ProjectId]=file;}
        try{await Modrinth.IdentifyIncluded(draft,token);}catch(Exception ex)when(ex is HttpRequestException or IOException){}
    }
    internal static bool Matches(CfFile file,CurseForgeProfile pack)=>file.Versions.Contains(pack.MinecraftVersion)&&file.Versions.Any(v=>v.Equals(pack.Loader,StringComparison.OrdinalIgnoreCase));
    internal async Task<Dictionary<long,CfFile>> ResolveAddition(ModpackDraft draft,CfProject project,CancellationToken token)
    {
        if(project.ModrinthId.Length>0)return await Modrinth.ResolveAddition(draft,project,token);
        var result=new Dictionary<long,CfFile>();var visiting=new HashSet<long>();
        async Task Visit(long id){long known=draft.Aliases.GetValueOrDefault(id,id);if(result.ContainsKey(id)||draft.Added.ContainsKey(known)||draft.ModFiles.ContainsKey(known)&&!draft.Removed.Contains(known))return;if(!visiting.Add(id))return;
            var file=(await Files(id,token,draft.Source.MinecraftVersion,draft.Source.Loader)).FirstOrDefault(f=>Matches(f,draft.Source))??throw new InvalidOperationException("A required mod has no file for this Minecraft version and loader.");
            if(!file.Name.EndsWith(".jar",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("This project does not provide a mod JAR.");
            result[id]=file;foreach(var d in file.Dependencies.Where(d=>d.Relation==3))await Visit(d.ModId);visiting.Remove(id);
        }
        await Visit(project.Id);foreach(var id in result.Keys.ToArray())result[id]=result[id] with{Dependencies=result[id].Dependencies.Select(d=>d with{ModId=draft.Aliases.GetValueOrDefault(d.ModId,d.ModId)}).ToList()};var all=draft.ModFiles.Values.Where(f=>!draft.Removed.Contains(f.ModId)).Concat(draft.Added.Values).Concat(result.Values).ToList();var ids=all.Select(f=>draft.Aliases.GetValueOrDefault(f.ModId,f.ModId)).ToHashSet();if(all.Any(f=>f.Dependencies.Any(d=>d.Relation==5&&ids.Contains(draft.Aliases.GetValueOrDefault(d.ModId,d.ModId)))))throw new InvalidOperationException("CurseForge lists an incompatibility with a selected mod. Review your selection.");
        foreach(var id in result.Keys.ToArray()){var original=result[id];try{var match=await Modrinth.MatchHash(original.Sha1,token);if(match!=null&&Matches(match,draft.Source)){result[id]=original with{DownloadUrl=match.DownloadUrl,ModrinthId=match.ModrinthId,ModrinthProject=match.ModrinthProject,Sha512=match.Sha512};draft.Aliases[match.ModId]=id;}}catch(Exception ex)when(ex is HttpRequestException or IOException){} }
        return result;
    }
}
