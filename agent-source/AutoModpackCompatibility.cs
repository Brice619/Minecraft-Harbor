using System.Text.Json;
namespace HarborAgent;
public sealed partial class ClientCore
{
    internal static bool AutoModpackManaged(string folder)
    {
        string config=Path.Combine(folder,"automodpack","automodpack-client.json");
        if(!File.Exists(config)||!Directory.Exists(Path.Combine(folder,"mods"))||!Directory.EnumerateFiles(Path.Combine(folder,"mods"),"automodpack*.jar").Any(p=>new FileInfo(p).Length>0))return false;
        try{using var json=JsonDocument.Parse(File.ReadAllText(config));var root=json.RootElement;if(!root.TryGetProperty("selectedModpack",out var selected)||selected.ValueKind!=JsonValueKind.String)return false;string name=selected.GetString()??"";if(name.Length==0||name is "." or ".."||Path.GetFileName(name)!=name||name.IndexOfAny(['/', '\\', ':'])>=0)return false;return root.TryGetProperty("updateSelectedModpackOnLaunch",out var enabled)&&enabled.ValueKind==JsonValueKind.True&&Directory.Exists(Path.Combine(folder,"automodpack","modpacks",name));}
        catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){return false;}
    }
}
