using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using MinecraftHarbor;
namespace HarborAgent;
public sealed class ClientConfig
{
    public string Id {get;set;}=Guid.NewGuid().ToString();public string Host {get;set;}="";public string Token {get;set;}="";public string Profile {get;set;}="";
    public static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MinecraftHarborClient");
    public static string FilePath=>Path.Combine(Root,"connection.json");
    public void Save(){Directory.CreateDirectory(Root);File.WriteAllText(FilePath,JsonSerializer.Serialize(this));}
    public static ClientConfig Load(){try{return File.Exists(FilePath)?JsonSerializer.Deserialize<ClientConfig>(File.ReadAllText(FilePath))??new():new();}catch(Exception ex)when(ex is IOException or JsonException){return new();}}
}
public sealed record ClientProfile(string Folder,string Name,string Author,long ProjectId,string Guid)
{
    public static ClientProfile Read(string folder){folder=Path.GetFullPath(folder);if(!Directory.Exists(Path.Combine(folder,"mods")))throw new InvalidDataException("Choose an existing modpack folder containing mods.");string file=Path.Combine(folder,"minecraftinstance.json");if(!File.Exists(file))throw new InvalidDataException("Choose a CurseForge profile folder so Harbor can launch it through CurseForge.");using var document=JsonDocument.Parse(File.ReadAllText(file));var j=document.RootElement;var pack=j.GetProperty("installedModpack");string name=pack.GetProperty("name").GetString()??"";long id=pack.TryGetProperty("addonID",out var project)?project.GetInt64():pack.TryGetProperty("id",out project)?project.GetInt64():0;string author=pack.TryGetProperty("authors",out var authors)?string.Join(',',authors.EnumerateArray().Select(a=>a.TryGetProperty("id",out var aid)?aid.ToString():a.GetProperty("name").GetString())):"";string guid=j.GetProperty("guid").GetString()??"";if(!System.Guid.TryParse(guid,out _)||name.Length==0)throw new InvalidDataException("This CurseForge profile is incomplete.");return new(folder,name,author,id,guid);}
    public bool SamePack(ClientProfile other)=>ProjectId>0&&other.ProjectId>0?ProjectId==other.ProjectId:Name==other.Name&&Author.Length>0&&Author==other.Author;
    public string LaunchUri=>"curseforge://launch-game?instanceId="+Uri.EscapeDataString(Guid)+"&gameId=432";
}
public sealed partial class ClientCore:IDisposable
{
    readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)};
    public ClientConfig Config {get;}
    public Action<string>? Progress {get;set;}
    public ClientCore(ClientConfig config){Config=config;}
    public static string NormalizeHost(string text){if(!text.Contains("://"))text="http://"+text;if(!Uri.TryCreate(text,UriKind.Absolute,out var uri)||uri.Scheme!="http"||uri.UserInfo.Length>0||uri.AbsolutePath!="/"||uri.Query.Length>0||uri.Fragment.Length>0)throw new InvalidDataException("Enter Harbor's connection address, such as http://192.168.1.100:25566/");if(!IPAddress.TryParse(uri.Host,out var ip)||!LocalIp(ip))throw new InvalidDataException("Use your Harbor PC's local IPv4 address.");return new UriBuilder(uri){Port=uri.IsDefaultPort?25566:uri.Port}.Uri.ToString();}
    static bool LocalIp(IPAddress ip){var b=ip.GetAddressBytes();return ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&(IPAddress.IsLoopback(ip)||b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1]>=16&&b[1]<=31||b[0]==169&&b[1]==254);}
    public async Task<LanSyncInfo> Info(){using var response=await http.GetAsync(Config.Host+"harbor/info");response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<LanSyncInfo>()??throw new InvalidDataException("Harbor returned no server details.");}
    async Task<HttpResponseMessage> Post<T>(string uri,T value){using var request=new HttpRequestMessage(HttpMethod.Post,uri){Content=new StringContent(JsonSerializer.Serialize(value),System.Text.Encoding.UTF8,"application/json")};return await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);}
    public async Task Pair(string code){var response=await Post(Config.Host+"harbor/pair",new LanPairRequest(code.Trim(),Config.Id,Environment.MachineName));using(response){if(!response.IsSuccessStatusCode)throw new InvalidOperationException(await response.Content.ReadAsStringAsync());Config.Token=(await response.Content.ReadFromJsonAsync<LanPairResponse>())?.Token??throw new InvalidDataException("Missing connection token");Config.Save();}}
    public static bool IsRunning(string folder){using var search=new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name = 'java.exe' OR Name = 'javaw.exe'");using var items=search.Get();foreach(ManagementObject item in items)using(item){string? command=item["CommandLine"]?.ToString();if(command==null)return true;if(command.Contains(folder,StringComparison.OrdinalIgnoreCase)&&command.Contains("--gameDir",StringComparison.OrdinalIgnoreCase))return true;}return false;}
    public static void RequireGameClosed(string folder){if(IsRunning(folder))throw new InvalidOperationException("Close Minecraft before updating this pack.");}
    public Task<string> Sync(string folder,LanSyncInfo info,Action<string>? check=null)
    {
        if(!info.FullPack)throw new InvalidOperationException("The host has not selected a client pack folder. Set it in Harbor LAN PCs before updating.");
        return SyncPack(folder,info,check);
    }
    public async Task Report(string folder,LanSyncInfo info,string message){if(Config.Token.Length==0)return;using var response=await Post(Config.Host+"harbor/status",new LanHeartbeat(Config.Id,Config.Token,info.FullPack?await LocalPackHash(folder):"",IsRunning(folder),message));response.EnsureSuccessStatusCode();}
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,(long Ticks,long Size,string Hash)> localHashes=new(StringComparer.OrdinalIgnoreCase);
    static Task<string> LocalPackHash(string folder)=>Task.Run(()=>{string stateFile=Path.Combine(folder,".harbor-sync-state.json");if(!File.Exists(stateFile))return "";var state=JsonSerializer.Deserialize<PackSyncState>(File.ReadAllText(stateFile),PackJson);if(state==null)return "";var known=(state.LocalFiles??[]).ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase);var paths=state.Files.Where(PackSyncPaths.Allowed).Concat(PackSyncPaths.Enumerate(folder).Where(p=>p.StartsWith("mods/",StringComparison.OrdinalIgnoreCase)&&p.EndsWith(".jar",StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase);return PackSyncPaths.HashList(paths.Select(p=>{string file=PackSyncPaths.Resolve(folder,p);if(!File.Exists(file))return new PackSyncFile(p,"",0);var f=new FileInfo(file);if(known.TryGetValue(p,out var observed)&&observed.Size==f.Length&&observed.WrittenUtcTicks==f.LastWriteTimeUtc.Ticks)return new PackSyncFile(p,observed.Hash,f.Length);if(!localHashes.TryGetValue(file,out var cached)||cached.Ticks!=f.LastWriteTimeUtc.Ticks||cached.Size!=f.Length){cached=(f.LastWriteTimeUtc.Ticks,f.Length,PackSyncPaths.HashFile(file));localHashes[file]=cached;}return new PackSyncFile(p,cached.Hash,f.Length);}));});
    public void Dispose()=>http.Dispose();
}


