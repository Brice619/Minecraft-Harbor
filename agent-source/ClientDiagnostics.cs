namespace HarborAgent;
internal static class ClientDiagnostics
{
    internal static void Record(Exception error)
    {
        try{Directory.CreateDirectory(ClientConfig.Root);File.AppendAllText(Path.Combine(ClientConfig.Root,"update.log"),DateTime.UtcNow.ToString("O")+Environment.NewLine+error+Environment.NewLine);}
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
