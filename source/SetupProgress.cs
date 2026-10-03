namespace MinecraftHarbor;
public sealed record SetupProgress(int Step,double Fraction,string Detail,bool Complete=false,bool WorldDeferred=false)
{
    public int Percent=>Complete?100:Math.Clamp((int)Math.Round((Step+Math.Clamp(Fraction,0,1))*20),0,99);
}
internal static class SetupFiles
{
    internal static async Task FinishDirectory(string source,string destination,CancellationToken token)
    {
        for(int attempt=0;;attempt++){
            token.ThrowIfCancellationRequested();
            try{Directory.Move(source,destination);return;}
            catch(IOException)when(attempt<12&&!Directory.Exists(destination)){await Task.Delay(Math.Min(2000,300*(attempt+1)),token);}
            catch(UnauthorizedAccessException)when(attempt<12&&!Directory.Exists(destination)){await Task.Delay(Math.Min(2000,300*(attempt+1)),token);}
        }
    }
}
