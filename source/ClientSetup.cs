using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace MinecraftHarbor;

// This listener serves only the setup page and the verified installer/helper.
// It deliberately has no filesystem route or server-control endpoint.
public sealed class ClientSetup : IDisposable
{
    public const int Port = 25566;
    public const string HelperName = "automodpack-mc1.21.1-neoforge-4.0.6-harbor14.jar";
    public const string InstallerName = "Minecraft-Harbor-Client-Setup.exe";
    public const string AgentName = "Minecraft-Harbor-Agent-Setup.exe";
    public const string RetiredPackageName = "Going-all-the-way-Harbor.zip";
    internal const string HelperSha256 = "EC3F05755D3CAE51C313C6DDFD660FAC681D59596F287107AC4380B233C270D8";
    readonly string root;
    readonly Func<ServerManager>? current;
    public PackPublisher? Publisher=>current==null?null:publisher??=new(current());
    PackPublisher? publisher;
    public bool PackSyncAvailable=>Publisher?.Available==true;
    string ServerDir=>current?.Invoke().ServerDir??Path.Combine(root,"server");
    bool CustomSync=>current?.Invoke().Profile.CustomUltimineSync??true;
    readonly CancellationTokenSource cancellation = new();
    readonly SemaphoreSlim connections = new(8);
    TcpListener? listener;
    readonly int requestedPort;
    public int ListeningPort=>listener?.LocalEndpoint is IPEndPoint endpoint?endpoint.Port:requestedPort;
    public string Status { get; private set; } = "Setup page is starting";
    public string Url => $"http://{ServerManager.LanAddress()}:{Port}/";
    public LanPcRegistry Pcs {get;}
    public ClientSetup(string root,Func<ServerManager>? current=null,int port=Port) { this.root = root;this.current=current;requestedPort=port;Pcs=new(root); }
    public LanSyncInfo Info(){var s=current?.Invoke();if(PackSyncAvailable){var manifest=Publisher!.Manifest();return new(s!.Profile.DisplayName,manifest.Name,manifest.MinecraftVersion,true,"",manifest.Hash,manifest.Hash,manifest.ProjectId,manifest.Loader,manifest.LoaderVersion,true);}var path=CustomModPath();string hash=CustomSync&&File.Exists(path)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))):"";return new(s?.Profile.DisplayName??"Minecraft Harbor",s?.Profile.Name??"All the Mods 10",s?.Profile.MinecraftVersion??"1.21.1",false,Path.GetFileName(path),hash,Fingerprint());}
    string CustomModPath()=>Path.Combine(ServerDir,"mods","ftb-ultimine-neoforge-2101.1.15.jar");
    public byte[] Agent(){var folder=Path.Combine(root,"client-setup");var bytes=File.ReadAllBytes(Path.Combine(folder,AgentName));var expected=File.ReadAllText(Path.Combine(folder,AgentName+".sha256")).Trim();if(expected.Length!=64||!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The Windows agent failed verification.");return bytes;}
    public void Start()
    {
        try {
            listener = new TcpListener(IPAddress.Any, requestedPort);
            listener.Start(16);
            Status = "Available on your home network";
            _ = Accept();
        } catch(Exception ex) { Status = "Setup page unavailable: " + ex.Message; }
    }
    async Task Accept()
    {
        try {
            while (!cancellation.IsCancellationRequested) {
                await connections.WaitAsync(cancellation.Token);
                TcpClient client;
                try { client = await listener!.AcceptTcpClientAsync(cancellation.Token); }
                catch { connections.Release(); throw; }
                _ = Serve(client);
            }
        } catch(Exception ex) when(ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    public static bool IsLocalPeer(IPAddress address)
    {
        if(address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if(IPAddress.IsLoopback(address)) return true;
        if(address.AddressFamily != AddressFamily.InterNetwork) return false;
        var peer = address.GetAddressBytes();
        foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            foreach(var item in nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)) {
                var local = item.Address.GetAddressBytes(); var mask = item.IPv4Mask.GetAddressBytes();
                if(mask.All(b => b == 0)) continue;
                if(Enumerable.Range(0,4).All(i => (peer[i] & mask[i]) == (local[i] & mask[i]))) return true;
            }
        return false;
    }
    async Task Serve(TcpClient client)
    {
        using(client)
        using(var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token)) {
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            try {
                if(client.Client.RemoteEndPoint is not IPEndPoint peer || !IsLocalPeer(peer.Address)) return;
                var stream = client.GetStream(); var bytes = new byte[8192]; int count = 0;
                while(count < bytes.Length) {
                    int read = await stream.ReadAsync(bytes.AsMemory(count,bytes.Length-count),deadline.Token);
                    if(read == 0) return;
                    count += read;
                    if(Encoding.ASCII.GetString(bytes,0,count).Contains("\r\n\r\n")) break;
                }
                string header = Encoding.ASCII.GetString(bytes,0,count);
                if(!header.Contains("\r\n\r\n")) { await Reply(stream,431,"text/plain",Encoding.UTF8.GetBytes("Request too large"),false,deadline.Token);return; }
                var request = header.Split("\r\n",2)[0].Split(' ');
                if(request.Length != 3 || request[0] is not ("GET" or "HEAD" or "POST")) { await Reply(stream,405,"text/plain",Encoding.UTF8.GetBytes("Use GET, HEAD or POST"),false,deadline.Token);return; }
                bool head = request[0] == "HEAD";
                if(request[0]=="POST"){
                    int split=header.IndexOf("\r\n\r\n",StringComparison.Ordinal)+4;string? lengthLine=header[..split].Split("\r\n").FirstOrDefault(l=>l.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase));
                    if(lengthLine==null||!int.TryParse(lengthLine[15..].Trim(),out int length)||length<1||split+length>bytes.Length){await Reply(stream,400,"text/plain",Encoding.UTF8.GetBytes("Invalid request size"),false,deadline.Token);return;}
                    while(count<split+length){int read=await stream.ReadAsync(bytes.AsMemory(count,split+length-count),deadline.Token);if(read==0)return;count+=read;}
                    if(length>4096){await Reply(stream,400,"text/plain",Encoding.UTF8.GetBytes("Request too large"),false,deadline.Token);return;}
                    string body=Encoding.UTF8.GetString(bytes,split,length);
                    try{
                        byte[] response;
                        if(request[1] is "/harbor/manifest" or "/harbor/file"){
                            var update=JsonSerializer.Deserialize<PackSyncRequest>(body,new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidDataException("Missing update request");Pcs.CheckToken(new(update.Id,update.Token,"",false,""));
                            if(request[1]=="/harbor/manifest")response=JsonSerializer.SerializeToUtf8Bytes(Publisher?.Manifest()??throw new InvalidOperationException("Client syncing is not configured."));
                            else{string path=Publisher?.FileFor(update.ManifestHash,update.Path)??throw new InvalidOperationException("Client syncing is not configured.");await ReplyFile(stream,path,deadline.Token);return;}
                        }
                        else if(request[1]=="/harbor/pair"){var pair=JsonSerializer.Deserialize<LanPairRequest>(body,new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidDataException("Missing PC details");response=JsonSerializer.SerializeToUtf8Bytes(Pcs.Pair(pair,peer.Address.ToString()));}
                        else if(request[1] is "/harbor/status" or "/harbor/mod"){
                            var message=JsonSerializer.Deserialize<LanHeartbeat>(body,new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidDataException("Missing status");Pcs.CheckToken(message);
                            if(request[1]=="/harbor/mod"){
                                if(!CustomSync)throw new InvalidOperationException("This pack does not have custom-mod syncing enabled.");response=File.ReadAllBytes(CustomModPath());if(Convert.ToHexString(SHA256.HashData(response))!=message.ModHash)throw new InvalidOperationException("Server build changed; refresh its details.");
                                await Reply(stream,200,"application/java-archive",response,false,deadline.Token);return;
                            }
                            Pcs.Heartbeat(message,peer.Address.ToString());response=JsonSerializer.SerializeToUtf8Bytes(new{ok=true});
                        }else{await Reply(stream,404,"text/plain",Encoding.UTF8.GetBytes("Not found"),false,deadline.Token);return;}
                        await Reply(stream,200,"application/json",response,false,deadline.Token);
                    }catch(Exception ex)when(ex is UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException){await Reply(stream,ex is UnauthorizedAccessException?403:400,"text/plain",Encoding.UTF8.GetBytes(ex.Message),false,deadline.Token);}
                }
                else if(request[1] == "/harbor/info")await Reply(stream,200,"application/json",JsonSerializer.SerializeToUtf8Bytes(Info()),head,deadline.Token);
                else if(request[1] == "/"+AgentName)await Reply(stream,200,"application/octet-stream",Agent(),head,deadline.Token,AgentName);
                else if(request[1] == "/") await Reply(stream,200,"text/html; charset=utf-8",Encoding.UTF8.GetBytes(Page()),head,deadline.Token);
                else if(request[1] == "/" + HelperName) await Reply(stream,200,"application/java-archive",Helper(),head,deadline.Token,HelperName);
                else if(CustomSync&&request[1] == "/" + InstallerName) await Reply(stream,200,"application/octet-stream",Installer(),head,deadline.Token,InstallerName);
                else if(request[1] == "/" + RetiredPackageName) await Reply(stream,410,"text/plain; charset=utf-8",Encoding.UTF8.GetBytes("The separate-profile ZIP has been retired. Open the setup page and add the sync helper to your EXISTING ATM10 profile."),head,deadline.Token);
                else await Reply(stream,404,"text/plain",Encoding.UTF8.GetBytes("Not found"),head,deadline.Token);
            } catch(Exception ex) when(ex is IOException or SocketException or OperationCanceledException or InvalidDataException or CryptographicException or UnauthorizedAccessException) { }
            finally { connections.Release(); }
        }
    }
    static async Task Reply(NetworkStream stream,int status,string type,byte[] body,bool head,CancellationToken token,string? download=null)
    {
        string reason = status switch {200=>"OK",400=>"Bad Request",403=>"Forbidden",404=>"Not Found",405=>"Method Not Allowed",410=>"Gone",_=>"Request Header Fields Too Large"};
        string header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; frame-ancestors 'none'\r\n";
        if(download != null) header += $"Content-Disposition: attachment; filename=\"{download}\"\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header+"\r\n"),token);
        if(!head) await stream.WriteAsync(body,token);
    }
    static async Task ReplyFile(NetworkStream stream,string path,CancellationToken token){using var file=File.OpenRead(path);string header=$"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {file.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n\r\n";await stream.WriteAsync(Encoding.ASCII.GetBytes(header),token);await file.CopyToAsync(stream,token);}
    public string Fingerprint()
    {
        var path = Path.Combine(ServerDir,"automodpack",".private","cert.crt");
        if(!File.Exists(path)) return "Available after the server starts";
        try { using var cert = X509Certificate2.CreateFromPem(File.ReadAllText(path)); return cert.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant(); }
        catch(CryptographicException) { return "Certificate could not be read"; }
    }
    public string PackSummary()
    {
        if(!CustomSync)return "Use the matching "+current!.Invoke().Profile+" client in CurseForge.";
        var file = Path.Combine(ServerDir,"automodpack","host-modpack","automodpack-content.json");
        if(!File.Exists(file)) return "The server will prepare downloads when it starts.";
        try {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var files = json.RootElement.GetProperty("list").EnumerateArray().ToArray();
            int mods = files.Count(f => f.GetProperty("file").GetString()!.EndsWith(".jar"));
            long size = files.Sum(f => long.Parse(f.GetProperty("size").GetString()!));
            return $"{mods} custom mod{(mods==1?"":"s")} to sync · {size / 1024d:N0} KB · Uses your existing ATM10 installation";
        } catch { return "The server is preparing the download list."; }
    }
    internal static void VerifyHelper(byte[] helper){if(Convert.ToHexString(SHA256.HashData(helper)) != HelperSha256)throw new InvalidDataException("The Harbor connector mod failed verification. Reinstall Harbor 1.4.");}
    public byte[] Helper()
    {
        var helper = File.ReadAllBytes(Path.Combine(root,"client-setup",HelperName));
        VerifyHelper(helper);
        return helper;
    }
    public byte[] Installer()
    {
        var folder=Path.Combine(root,"client-setup");
        var expected=File.ReadAllText(Path.Combine(folder,InstallerName+".sha256")).Trim();
        if(expected.Length!=64||expected.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("The installer checksum is missing or invalid.");
        var installer=File.ReadAllBytes(Path.Combine(folder,InstallerName));
        if(!Convert.ToHexString(SHA256.HashData(installer)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The client installer failed verification. Rebuild it before distributing it.");
        return installer;
    }
    string Page()
    {
        var s=current?.Invoke();string E(string value)=>WebUtility.HtmlEncode(value);
        return $$"""
        <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Minecraft Harbor Client</title><style>body{margin:0;background:#10171d;color:#e7eff1;font:18px/1.65 system-ui,sans-serif}main{max-width:850px;margin:60px auto;padding:0 28px}h1{font-size:48px;line-height:1.15}.card{padding:28px;background:#19232b;border:1px solid #2d3d46;border-radius:16px;margin:28px 0}.button{display:inline-block;background:#7de2b6;color:#10171d;padding:14px 22px;border-radius:8px;text-decoration:none;font-weight:700}.muted{color:#9fb1b7}li{padding:9px}code{overflow-wrap:anywhere}</style>
        <main><h1>Minecraft Harbor Connector</h1><p>Required pack: <strong>{{E(s?.Profile.Name??"modpack")}} {{E(s?.Profile.PackVersion??"")}}</strong></p>
        <div class="card"><h2>Connect through Minecraft</h2><a class="button" href="/{{HelperName}}">Download connector mod</a><ol>
        <li>Use the required version in your existing CurseForge profile.</li><li>Close Minecraft. Replace the old AutoModpack JAR in that profile’s mods folder with this connector.</li>
        <li>Launch the profile and join the server. The connector verifies the pack version before syncing mods, configs and scripts.</li></ol>
        <p class="muted">Minecraft 1.21.1 · NeoForge. Syncing stops if the CurseForge release does not match.</p></div>
        <div class="card"><h2>Server certificate</h2><code>{{E(Fingerprint())}}</code></div>
        <p>Join Minecraft at <strong>{{E(ServerManager.LanAddress())}}:25565</strong></p>
        <details><summary>Previous Windows client</summary><a href="/{{AgentName}}">Download existing installer</a></details></main></html>
        """;
    }
    public void Dispose(){cancellation.Cancel();listener?.Stop();}
}

