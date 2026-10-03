using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MinecraftHarbor;

internal sealed class ModrinthCatalog
{
    readonly string root;readonly HttpClient http;
    internal ModrinthCatalog(string root,HttpClient? http=null){this.root=root;this.http=http??new HttpClient{Timeout=TimeSpan.FromMinutes(15)};}
    internal static long Id(string value)=>-1000000-(BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(value)))&0x3fffffffffffffff);
    static string Text(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()!:"";
    static long Number(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.TryGetInt64(out var n)?n:0;
    static List<string> Strings(JsonElement e,string key)=>e.TryGetProperty(key,out var a)&&a.ValueKind==JsonValueKind.Array?a.EnumerateArray().Select(v=>v.GetString()!).ToList():new();
    async Task<JsonDocument> Request(string path,CancellationToken token,object? body=null)
    {
        using var request=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,"https://api.modrinth.com/v2/"+path);request.Headers.UserAgent.ParseAdd("MinecraftHarbor/1.2 (personal LAN server manager)");
        if(body!=null)request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(request,token);if((int)response.StatusCode==429)throw new IOException("Modrinth is receiving too many requests. Try again shortly.");response.EnsureSuccessStatusCode();return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }
    static CfProject ProjectInfo(JsonElement e)
    {
        string id=Text(e,"project_id");if(id.Length==0)id=Text(e,"id");string title=Text(e,"title"),type=Text(e,"project_type"),slug=Text(e,"slug");
        return new(Id(id),title,Text(e,"description"),Text(e,"icon_url"),"https://modrinth.com/"+(type=="modpack"?"modpack":"mod")+"/"+slug,Number(e,"downloads"),new()){ModrinthId=id,ServerSide=Text(e,"server_side")};
    }
    internal static CfFile VersionInfo(JsonElement e)
    {
        string id=Text(e,"id"),project=Text(e,"project_id");var files=e.GetProperty("files").EnumerateArray().ToList();var primary=files.FirstOrDefault(f=>f.TryGetProperty("primary",out var p)&&p.GetBoolean());if(primary.ValueKind==JsonValueKind.Undefined)primary=files.FirstOrDefault();if(primary.ValueKind==JsonValueKind.Undefined)throw new InvalidDataException("This release has no files.");
        var dependencies=new List<CfDependency>();foreach(var dep in e.GetProperty("dependencies").EnumerateArray()){string p=Text(dep,"project_id"),v=Text(dep,"version_id");string type=Text(dep,"dependency_type");dependencies.Add(new(p.Length>0?Id(p):0,type=="required"?3:type=="incompatible"?5:1){ModrinthProject=p,ModrinthVersion=v});}
        string name=Text(primary,"filename");return new(Id(id),Id(project),name,Text(e,"name"),Number(primary,"size"),Text(primary,"url"),Strings(e,"game_versions").Concat(Strings(e,"loaders")).ToList(),name.EndsWith(".mrpack",StringComparison.OrdinalIgnoreCase)?1:0,dependencies,Text(primary.GetProperty("hashes"),"sha1")){ModrinthId=id,ModrinthProject=project,Sha512=Text(primary.GetProperty("hashes"),"sha512")};
    }
    internal async Task<CfPage> Search(string query,int sort,int page,bool mods,CancellationToken token,string? minecraft=null,string? loader=null)
    {
        var facets=new List<string[]>{new[]{"project_type:"+(mods?"mod":"modpack")}};if(minecraft!=null)facets.Add(new[]{"versions:"+minecraft});if(loader!=null)facets.Add(new[]{"categories:"+loader.ToLowerInvariant()});
        string index=sort switch{3=>"updated",11=>"newest",6=>"downloads",_=>"relevance"};using var json=await Request($"search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}&index={index}&offset={Math.Max(page,0)*20}&limit=20",token);
        var items=json.RootElement.GetProperty("hits").EnumerateArray().Select(ProjectInfo).Where(p=>p.ServerSide!="unsupported").ToList();if(sort==4)items=items.OrderBy(p=>p.Name).ToList();return new(items,(int)Number(json.RootElement,"total_hits"));
    }
    internal async Task<List<CfFile>> Files(string project,CancellationToken token,string? minecraft=null,string? loader=null)
    {
        string query="project/"+Uri.EscapeDataString(project)+"/version";var filters=new List<string>();if(minecraft!=null)filters.Add("game_versions="+Uri.EscapeDataString(JsonSerializer.Serialize(new[]{minecraft})));if(loader!=null)filters.Add("loaders="+Uri.EscapeDataString(JsonSerializer.Serialize(new[]{loader.ToLowerInvariant()})));if(filters.Count>0)query+="?"+string.Join('&',filters);
        using var json=await Request(query,token);return json.RootElement.EnumerateArray().Where(v=>v.GetProperty("files").GetArrayLength()>0).Select(VersionInfo).ToList();
    }
    internal async Task<CfProject> Project(string id,CancellationToken token){using var json=await Request("project/"+Uri.EscapeDataString(id),token);return ProjectInfo(json.RootElement);}
    async Task<CfFile> Version(string id,CancellationToken token){using var json=await Request("version/"+Uri.EscapeDataString(id),token);return VersionInfo(json.RootElement);}
    internal async Task<Dictionary<long,CfProject>> Projects(IEnumerable<string> ids,CancellationToken token)
    {
        var result=new Dictionary<long,CfProject>();foreach(var batch in ids.Distinct().Chunk(100)){using var json=await Request("projects?ids="+Uri.EscapeDataString(JsonSerializer.Serialize(batch)),token);foreach(var p in json.RootElement.EnumerateArray().Select(ProjectInfo))result[p.Id]=p;}return result;
    }
    internal async Task<CfFile?> MatchHash(string? hash,CancellationToken token)
    {
        if(string.IsNullOrEmpty(hash))return null;using var json=await Request("version_files",token,new{hashes=new[]{hash.ToLowerInvariant()},algorithm="sha1"});if(!json.RootElement.EnumerateObject().Any())return null;var version=json.RootElement.EnumerateObject().First().Value;var found=VersionInfo(version);
        // A release can have multiple files. Match the exact published bytes, not merely the release name.
        var file=version.GetProperty("files").EnumerateArray().FirstOrDefault(f=>Text(f.GetProperty("hashes"),"sha1").Equals(hash,StringComparison.OrdinalIgnoreCase));if(file.ValueKind==JsonValueKind.Undefined)return null;
        return found with{Name=Text(file,"filename"),Length=Number(file,"size"),DownloadUrl=Text(file,"url"),Sha1=Text(file.GetProperty("hashes"),"sha1"),Sha512=Text(file.GetProperty("hashes"),"sha512")};
    }
    internal async Task<string> Download(CfFile file,CancellationToken token,Action<string>? progress=null)
    {
        if(Path.GetFileName(file.Name)!=file.Name||file.Name.IndexOfAny(new[]{':','\\','/'})>=0||file.Length<=0||file.Length>20L*1024*1024*1024||file.Sha1==null||file.Sha1.Length!=40)throw new InvalidDataException("Invalid Modrinth download metadata.");
        string directory=Path.Combine(root,"downloads","modrinth",file.Sha1.ToLowerInvariant());Directory.CreateDirectory(directory);string path=Path.Combine(directory,file.Name);
        async Task<bool> Valid(string name){if(!File.Exists(name)||new FileInfo(name).Length!=file.Length)return false;using var stream=File.OpenRead(name);if(!Convert.ToHexString(await SHA1.HashDataAsync(stream,token)).Equals(file.Sha1,StringComparison.OrdinalIgnoreCase))return false;if(!string.IsNullOrEmpty(file.Sha512)){stream.Position=0;return Convert.ToHexString(await SHA512.HashDataAsync(stream,token)).Equals(file.Sha512,StringComparison.OrdinalIgnoreCase);}return true;}
        if(await Valid(path)){progress?.Invoke("Using cached "+file.DisplayName+"…");return path;}
        if(!Uri.TryCreate(file.DownloadUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!new[]{"cdn.modrinth.com","github.com","raw.githubusercontent.com","gitlab.com"}.Contains(uri.Host))throw new InvalidDataException("This pack uses an unsupported download location.");
        using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.UserAgent.ParseAdd("MinecraftHarbor/1.2 (personal LAN server manager)");using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
        if(new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace<file.Length+512L*1024*1024)throw new IOException("Not enough free space for this download.");string partial=path+".partial";
        try{using(var input=await response.Content.ReadAsStreamAsync(token))using(var output=File.Create(partial)){byte[] buffer=new byte[131072];long count=0;int n;var watch=System.Diagnostics.Stopwatch.StartNew();while((n=await input.ReadAsync(buffer,token))>0){count+=n;if(count>file.Length)throw new InvalidDataException("The download exceeds its declared size.");await output.WriteAsync(buffer.AsMemory(0,n),token);if(watch.ElapsedMilliseconds>600){progress?.Invoke($"Downloading {file.DisplayName} · {count/1048576d:N0} / {file.Length/1048576d:N0} MB…");watch.Restart();}}}if(!await Valid(partial))throw new InvalidDataException("The Modrinth download failed its checksum check.");File.Move(partial,path,true);return path;}finally{if(File.Exists(partial))File.Delete(partial);}
    }
    static string Relative(string path)
    {
        path=path.Replace('\\','/');if(path.StartsWith('/')||path.Contains(':')||path.Split('/').Any(p=>p==".."||p=="."||p.EndsWith(' ')||p.EndsWith('.'))||path.IndexOfAny(Path.GetInvalidPathChars())>=0)throw new InvalidDataException("This pack contains an invalid file path.");return path;
    }
    internal async Task<ModpackDraft> Inspect(CfProject project,CfFile release,CancellationToken token,Action<string> progress)
    {
        if(project.ServerSide=="unsupported")throw new InvalidOperationException("This pack is marked as client-only. Choose a pack that supports dedicated servers.");
        string mrpack=await Download(release,token,progress);using var zip=ZipFile.OpenRead(mrpack);var entry=zip.GetEntry("modrinth.index.json")??throw new InvalidDataException("This download has no Modrinth pack manifest.");if(entry.Length>16*1024*1024)throw new InvalidDataException("The pack manifest is too large.");using var reader=new StreamReader(entry.Open());using var json=JsonDocument.Parse(await reader.ReadToEndAsync(token));var j=json.RootElement;
        if(Number(j,"formatVersion")!=1||Text(j,"game")!="minecraft")throw new InvalidDataException("This Modrinth pack format is not supported.");var dependencies=j.GetProperty("dependencies");string minecraft=Text(dependencies,"minecraft"),loader="",loaderVersion="";foreach(var name in new[]{"neoforge","forge","fabric-loader"})if(dependencies.TryGetProperty(name,out var version)){if(loader.Length>0)throw new InvalidDataException("This pack declares more than one mod loader.");loader=name=="fabric-loader"?"fabric":name;loaderVersion=version.GetString()!;}
        if(loader.Length==0||minecraft.Length==0)throw new InvalidDataException("This pack requires a loader Harbor does not support yet.");
        string archive=Path.Combine(Path.GetDirectoryName(mrpack)!,"server.zip"),receipt=archive+".sha256";var source=new CurseForgeProfile("",project.Name,release.DisplayName,minecraft,loader,loaderVersion,project.Id,release.Id);
        bool valid=false;if(File.Exists(archive)&&File.Exists(receipt)){using var cached=File.OpenRead(archive);valid=Convert.ToHexString(await SHA256.HashDataAsync(cached,token))==File.ReadAllText(receipt);}
        if(!valid){
            string stage=Path.Combine(Path.GetDirectoryName(mrpack)!,"building-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);var files=j.GetProperty("files").EnumerateArray().ToList();if(files.Count>10000)throw new InvalidDataException("This pack lists too many files.");var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long expanded=0;
            foreach(var f in files){token.ThrowIfCancellationRequested();if(f.TryGetProperty("env",out var env)&&Text(env,"server")=="unsupported")continue;string relative=Relative(Text(f,"path"));if(relative.Length==0||!paths.Add(relative))throw new InvalidDataException("This pack lists duplicate or empty file paths.");var hashes=f.GetProperty("hashes");string hash=Text(hashes,"sha1");var urls=Strings(f,"downloads");var download=new CfFile(Id(hash),0,Path.GetFileName(relative),Path.GetFileName(relative),Number(f,"fileSize"),urls.FirstOrDefault(u=>Uri.TryCreate(u,UriKind.Absolute,out var address)&&address.Host=="cdn.modrinth.com")??urls.FirstOrDefault(),new(),0,new(),hash){ModrinthId=release.ModrinthId,Sha512=Text(hashes,"sha512")};
                expanded+=download.Length;if(expanded>20L*1024*1024*1024)throw new InvalidDataException("This pack is too large.");string cached=await Download(download,token,progress),target=Path.Combine(stage,relative.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(cached,target);
            }
            foreach(string layer in new[]{"overrides/","server-overrides/"})foreach(var item in zip.Entries.Where(e=>e.FullName.StartsWith(layer,StringComparison.Ordinal)&&e.Name.Length>0)){token.ThrowIfCancellationRequested();string relative=Relative(item.FullName[layer.Length..]);expanded+=item.Length;if(expanded>20L*1024*1024*1024)throw new InvalidDataException("This pack's overrides are too large.");string target=Path.Combine(stage,relative.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(target)!);using var input=item.Open();using var output=File.Create(target);await input.CopyToAsync(output,token);}
            // Client override files are intentionally excluded from a dedicated server.
            if(!Directory.Exists(Path.Combine(stage,"mods"))||!Directory.EnumerateFiles(Path.Combine(stage,"mods"),"*.jar").Any())throw new InvalidDataException("This pack has no server mods. Choose a pack that supports dedicated servers.");
            string partial=archive+".partial";try{progress("Preparing the reusable server pack…");await Task.Run(()=>ZipFile.CreateFromDirectory(stage,partial,CompressionLevel.Fastest,false),token);token.ThrowIfCancellationRequested();File.Move(partial,archive,true);using var checkedZip=File.OpenRead(archive);File.WriteAllText(receipt,Convert.ToHexString(await SHA256.HashDataAsync(checkedZip,token)));}finally{if(File.Exists(partial))File.Delete(partial);}
            string resolvedStage=Path.GetFullPath(stage),expectedParent=Path.GetFullPath(Path.GetDirectoryName(mrpack)!)+Path.DirectorySeparatorChar;if(resolvedStage.StartsWith(expectedParent,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolvedStage).StartsWith("building-",StringComparison.Ordinal))Directory.Delete(resolvedStage,true);
        }
        var draft=new ModpackDraft{Project=project,Release=release,Source=source,Archive=archive,ModrinthProject=project.ModrinthId,Config=PackConfiguration.Discover(archive)};await IdentifyIncluded(draft,token);return draft;
    }
    internal async Task IdentifyIncluded(ModpackDraft draft,CancellationToken token)
    {
        var hashes=new Dictionary<string,(string Name,long Length)>();
        if(draft.Prepared!=null){
            foreach(string file in Directory.EnumerateFiles(Path.Combine(draft.Prepared.Directory,"mods"),"*.jar")){
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked mod files are not supported.");
                using var stream=File.OpenRead(file);hashes[Convert.ToHexString(await SHA1.HashDataAsync(stream,token)).ToLowerInvariant()]=(Path.GetFileName(file),stream.Length);
            }
        }else{
            using var zip=ZipFile.OpenRead(draft.Archive);
            foreach(var entry in zip.Entries.Where(e=>e.FullName.Replace('\\','/').Contains("mods/")&&e.Name.EndsWith(".jar",StringComparison.OrdinalIgnoreCase))){using var stream=entry.Open();hashes[Convert.ToHexString(await SHA1.HashDataAsync(stream,token)).ToLowerInvariant()]=(entry.Name,entry.Length);}
        }
        var versions=new Dictionary<string,CfFile>();foreach(var batch in hashes.Keys.Chunk(100)){using var json=await Request("version_files",token,new{hashes=batch,algorithm="sha1"});foreach(var item in json.RootElement.EnumerateObject()){var file=VersionInfo(item.Value);if(hashes.TryGetValue(item.Name,out var entry))versions[item.Name]=file with{Name=entry.Name,Length=entry.Length,Sha1=item.Name};}}
        var projects=await Projects(versions.Values.Select(v=>v.ModrinthProject),token);var previous=draft.ModFiles.Values.GroupBy(f=>f.Name,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);var previousProjects=new Dictionary<long,CfProject>(draft.Mods);draft.ModFiles.Clear();draft.Mods.Clear();
        int local=-1;foreach(var pair in hashes){if(versions.TryGetValue(pair.Key,out var f)&&projects.TryGetValue(f.ModId,out var p)&&!draft.ModFiles.ContainsKey(p.Id)){draft.ModFiles[p.Id]=f;draft.Mods[p.Id]=p;if(previous.TryGetValue(f.Name,out var old))draft.Aliases[old.ModId]=p.Id;}else{var e=pair.Value;if(previous.TryGetValue(e.Name,out var old)&&previousProjects.TryGetValue(old.ModId,out var oldProject)){draft.ModFiles[old.ModId]=old;draft.Mods[old.ModId]=oldProject;}else{while(draft.ModFiles.ContainsKey(local))local--;draft.Mods[local]=new(local,Path.GetFileNameWithoutExtension(e.Name),"Included in the server pack","","",0,new());draft.ModFiles[local]=new(local,local,e.Name,e.Name,e.Length,null,new(){draft.Source.MinecraftVersion,draft.Source.Loader},0,new(),pair.Key);local--;}}}draft.MetadataResolved=true;
    }
    async Task<string> ProjectId(string version,CancellationToken token){using var json=await Request("version/"+Uri.EscapeDataString(version),token);return Text(json.RootElement,"project_id");}
    internal async Task<Dictionary<long,CfFile>> ResolveAddition(ModpackDraft draft,CfProject project,CancellationToken token,CfFile? exact=null)
    {
        if(project.ServerSide=="unsupported")throw new InvalidOperationException("This mod is client-only and cannot be installed on a dedicated server.");var result=new Dictionary<long,CfFile>();var visiting=new HashSet<long>();
        async Task Visit(CfProject p,CfFile? pinned){long known=draft.Aliases.GetValueOrDefault(p.Id,p.Id);if(draft.ModFiles.ContainsKey(known)&&!draft.Removed.Contains(known)||draft.Added.ContainsKey(known)||result.ContainsKey(p.Id))return;if(!visiting.Add(p.Id))return;if(p.ServerSide=="unsupported")throw new InvalidOperationException(p.Name+" is client-only.");var f=pinned??(await Files(p.ModrinthId,token,draft.Source.MinecraftVersion,draft.Source.Loader)).FirstOrDefault(v=>CurseForgeCatalog.Matches(v,draft.Source))??throw new InvalidOperationException(p.Name+" has no file for this Minecraft version and loader.");if(f.ModId!=p.Id||!CurseForgeCatalog.Matches(f,draft.Source)||!f.Name.EndsWith(".jar",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("A required mod file does not match this server.");var identical=draft.ModFiles.Values.Where(v=>!draft.Removed.Contains(v.ModId)).Concat(draft.Added.Values).FirstOrDefault(v=>v.Sha1!=null&&v.Sha1.Equals(f.Sha1,StringComparison.OrdinalIgnoreCase));if(identical!=null){draft.Aliases[p.Id]=identical.ModId;return;}result[p.Id]=f;draft.Mods[p.Id]=p;
            foreach(var d in f.Dependencies.Where(d=>d.Relation==3)){CfFile? version=d.ModrinthVersion.Length>0?await Version(d.ModrinthVersion,token):null;string id=d.ModrinthProject.Length>0?d.ModrinthProject:version!=null?await ProjectId(version.ModrinthId,token):throw new InvalidDataException("A required dependency does not identify its project.");await Visit(await Project(id,token),version);}visiting.Remove(p.Id);}
        await Visit(project,exact);foreach(var id in result.Keys.ToArray())result[id]=result[id] with{Dependencies=result[id].Dependencies.Select(d=>d with{ModId=draft.Aliases.GetValueOrDefault(d.ModId,d.ModId)}).ToList()};var all=draft.ModFiles.Values.Where(f=>!draft.Removed.Contains(f.ModId)).Concat(draft.Added.Values).Concat(result.Values).ToList();var selected=all.Select(f=>draft.Aliases.GetValueOrDefault(f.ModId,f.ModId)).ToHashSet();if(all.Any(f=>f.Dependencies.Any(d=>d.Relation==5&&(selected.Contains(draft.Aliases.GetValueOrDefault(d.ModId,d.ModId))||d.ModrinthVersion.Length>0&&all.Any(v=>v.ModrinthId==d.ModrinthVersion)))))throw new InvalidOperationException("A selected mod declares an incompatibility. Review your selection.");return result;
    }
}
