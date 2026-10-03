using System.Text.Json;

namespace MinecraftHarbor;
internal sealed class AppPreferences
{
    public bool Animations {get;set;}=true;
    public int FadeMilliseconds {get;set;}=500;
    public bool PlayerHeads {get;set;}=true;
    public bool RememberPage {get;set;}
    public string LastPage {get;set;}="Home";
    public bool MinimizeToTray {get;set;}
    public bool ConfirmStop {get;set;}
    public bool KeepAwake {get;set;}=true;
    public float ConsoleFontSize {get;set;}=10.5f;
    public bool ConsoleFollow {get;set;}=true;
    public int ConsoleHistoryCharacters {get;set;}=300000;
    public bool NotifyServer {get;set;}
    public bool NotifyErrors {get;set;}
    public bool NotifyBackups {get;set;}
    internal AppPreferences Copy()=>(AppPreferences)MemberwiseClone();
    internal void Normalize(){FadeMilliseconds=Math.Clamp(FadeMilliseconds,250,1000);ConsoleFontSize=Math.Clamp(ConsoleFontSize,9,16);ConsoleHistoryCharacters=Math.Clamp(ConsoleHistoryCharacters,100000,1000000);}
    internal static AppPreferences Load(string root,bool keepAwake=true)
    {
        try{var file=Path.Combine(root,"app-settings.json");var value=File.Exists(file)?JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(file))??new():new(){KeepAwake=keepAwake};value.Normalize();return value;}
        catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){return new(){KeepAwake=keepAwake};}
    }
    internal void Save(string root){Normalize();ServerManager.WriteJson(Path.Combine(root,"app-settings.json"),this);}
}
