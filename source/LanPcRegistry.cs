using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace MinecraftHarbor;
public sealed class LanPcRegistry
{
    readonly string file;
    readonly object gate=new();
    readonly List<LanPc> pcs;
    string code="";DateTime codeExpiry;
    readonly Dictionary<string,(DateTime Start,int Count)> attempts=new();
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public LanPcRegistry(string root){file=Path.Combine(root,"lan-pcs.json");try{pcs=File.Exists(file)?JsonSerializer.Deserialize<List<LanPc>>(File.ReadAllText(file))??new():new();foreach(var pc in pcs)pc.LastSeenUtc=DateTime.MinValue;}catch(Exception ex)when(ex is IOException or JsonException){pcs=new();}}
    public string NewCode(){lock(gate){code=RandomNumberGenerator.GetInt32(100000,1000000).ToString();codeExpiry=DateTime.UtcNow.AddMinutes(10);return code;}}
    public IReadOnlyList<LanPc> Snapshot(){lock(gate)return pcs.Select(p=>new LanPc{Id=p.Id,Name=p.Name,Address=p.Address,LastSeenUtc=p.LastSeenUtc,ModHash=p.ModHash,GameRunning=p.GameRunning,Message=p.Message}).ToArray();}
    public LanPairResponse Pair(LanPairRequest request,string address)
    {
        lock(gate){var entry=attempts.GetValueOrDefault(address);if(DateTime.UtcNow-entry.Start>TimeSpan.FromMinutes(1))entry=(DateTime.UtcNow,0);attempts[address]=(entry.Start,entry.Count+1);if(entry.Count>=10)throw new InvalidOperationException("Too many pairing attempts. Wait one minute.");
            if(code.Length==0||DateTime.UtcNow>codeExpiry||request.Code!=code)throw new UnauthorizedAccessException("Pairing code is invalid or expired.");
            if(!Guid.TryParse(request.Id,out _)||string.IsNullOrWhiteSpace(request.Name)||request.Name.Length>80||request.Name.Any(char.IsControl))throw new InvalidDataException("Invalid PC details.");
            var pc=pcs.Find(p=>p.Id==request.Id);if(pc==null){if(pcs.Count>=64)throw new InvalidOperationException("The paired-PC limit is reached.");pc=new(){Id=request.Id};pcs.Add(pc);}
            string token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));pc.Name=request.Name;pc.Address=address;pc.TokenHash=Hash(token);pc.LastSeenUtc=DateTime.UtcNow;pc.ModHash="";pc.Message="Connected";ServerManager.WriteJson(file,pcs);code="";return new(token);
        }
    }
    public void Heartbeat(LanHeartbeat request,string address)
    {
        lock(gate){var pc=Authorize(request);if(request.ModHash==null||request.Message==null||request.ModHash.Length>64||request.Message.Length>160||request.Message.Any(char.IsControl))throw new InvalidDataException("Invalid status.");pc.Address=address;pc.LastSeenUtc=DateTime.UtcNow;pc.ModHash=request.ModHash;pc.GameRunning=request.GameRunning;pc.Message=request.Message;ServerManager.WriteJson(file,pcs);}
    }
    LanPc Authorize(LanHeartbeat request){var pc=pcs.Find(p=>p.Id==request.Id);if(pc==null||request.Token==null||request.Token.Length!=64||!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(pc.TokenHash),Encoding.ASCII.GetBytes(Hash(request.Token))))throw new UnauthorizedAccessException("Pair this PC with Harbor again.");return pc;}
    public void CheckToken(LanHeartbeat request){lock(gate)Authorize(request);}
    public void Forget(string id){lock(gate){pcs.RemoveAll(p=>p.Id==id);ServerManager.WriteJson(file,pcs);}}
}
