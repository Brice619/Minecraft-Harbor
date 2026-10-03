namespace MinecraftHarbor;
public sealed record LanSyncInfo(string ServerName,string Pack,string MinecraftVersion,bool CustomSync,string ModFile,string ModHash,string Fingerprint,long ProjectId=0,string Loader="",string LoaderVersion="",bool FullPack=false);
public sealed record LanPairRequest(string Code,string Id,string Name);
public sealed record LanPairResponse(string Token);
public sealed record LanHeartbeat(string Id,string Token,string ModHash,bool GameRunning,string Message);
public sealed class LanPc
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string Address {get;set;}="";
    public string TokenHash {get;set;}="";
    public DateTime LastSeenUtc {get;set;}
    public string ModHash {get;set;}="";
    public bool GameRunning {get;set;}
    public string Message {get;set;}="";
    public bool Online=>DateTime.UtcNow-LastSeenUtc<TimeSpan.FromSeconds(45);
}
