using System.Text.Json;

namespace MinecraftHarbor;
internal sealed record ProcessReading(int Pid,DateTime StartedUtc,double CpuMilliseconds,long MemoryBytes);
internal sealed record PerformanceSample(DateTime Utc,double? CpuPercent,double MemoryGB,bool Online);
internal sealed class PerformanceHistory
{
    public readonly List<PerformanceSample> Samples=new();
    string? file;
    DateTime lastSample,lastPersist;
    ProcessReading? previous;
    public double? CpuPercent {get;private set;}
    public double MemoryGB {get;private set;}
    public void Select(string profileRoot,string world,DateTime now)
    {
        var path=Path.Combine(profileRoot,"performance",world+".json");if(path==file)return;
        Save();file=path;Samples.Clear();previous=null;lastSample=default;lastPersist=now;CpuPercent=null;MemoryGB=0;
        try{
            if(File.Exists(path)&&new FileInfo(path).Length<1024*1024){
                var items=JsonSerializer.Deserialize<List<PerformanceSample>>(File.ReadAllText(path))??new();
                Samples.AddRange(items.Where(x=>x.Utc>=now.AddHours(-1)&&x.Utc<=now&&double.IsFinite(x.MemoryGB)&&x.MemoryGB>=0&&(x.CpuPercent==null||double.IsFinite(x.CpuPercent.Value)&&x.CpuPercent>=0&&x.CpuPercent<=100)).OrderBy(x=>x.Utc).TakeLast(721));
            }
        }catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){}
    }
    internal static double? CalculateCpu(ProcessReading? before,ProcessReading? after,double elapsedSeconds,int processors)
    {
        if(after==null)return 0;
        if(before==null||before.Pid!=after.Pid||before.StartedUtc!=after.StartedUtc||elapsedSeconds<=0||processors<1)return null;
        var delta=after.CpuMilliseconds-before.CpuMilliseconds;if(delta<0)return null;
        return Math.Clamp(delta/(elapsedSeconds*1000*processors)*100,0,100);
    }
    public bool Sample(DateTime now,ProcessReading? reading)
    {
        Samples.RemoveAll(x=>x.Utc<now.AddHours(-1)||x.Utc>now);
        if(lastSample!=default&&(now-lastSample).TotalSeconds<5)return false;
        CpuPercent=CalculateCpu(previous,reading,(now-lastSample).TotalSeconds,Environment.ProcessorCount);
        MemoryGB=reading==null?0:reading.MemoryBytes/1073741824d;
        Samples.Add(new(now,CpuPercent,MemoryGB,reading!=null));previous=reading;lastSample=now;
        if((now-lastPersist).TotalSeconds>=60){Save();lastPersist=now;}return true;
    }
    public void Save()
    {
        if(file==null||Samples.Count==0)return;
        try{Directory.CreateDirectory(Path.GetDirectoryName(file)!);ServerManager.WriteJson(file,Samples);}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
    }
}
