using System.Text.Json;
using MinecraftHarbor;
namespace HarborAgent;

public sealed partial class ClientCore
{
    internal static void RequireMatchingLoader(string folder,LanSyncInfo info)
        =>ReadLoaderVersion(folder,info,true);

    internal static string ReadLoaderVersion(string folder,LanSyncInfo info,bool requireMatch)
    {
        using var metadata=JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(folder,"minecraftinstance.json")));
        var root=metadata.RootElement;
        static string Text(JsonElement e,string key)=>e.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
        string game=Text(root,"gameVersion");
        if(game.Length==0||game!=info.MinecraftVersion)throw new InvalidDataException("This server needs Minecraft "+info.MinecraftVersion+". Select the matching pack version in CurseForge before updating mods.");
        if(info.Loader.Length==0||info.LoaderVersion.Length==0)throw new InvalidDataException("The server did not provide its exact mod loader version. Update Harbor on the host before syncing.");
        if(!root.TryGetProperty("baseModLoader",out var loader)||loader.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("This pack does not identify its mod loader. Repair its profile in CurseForge before syncing.");
        string name=Text(loader,"name"),prefix=info.Loader+"-";
        if(!name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("This server uses "+DisplayLoader(info.Loader)+" "+info.LoaderVersion+". Select a profile using that loader in CurseForge.");
        string installed=name[prefix.Length..];
        if(info.Loader.Equals("forge",StringComparison.OrdinalIgnoreCase)&&installed.StartsWith(game+"-",StringComparison.Ordinal))installed=installed[(game.Length+1)..];
        if(installed.Length==0)throw new InvalidDataException("This profile has no mod loader version. Repair it in CurseForge first.");
        if(requireMatch&&installed!=info.LoaderVersion)throw new InvalidDataException("This pack uses "+DisplayLoader(info.Loader)+" "+installed+"; the server needs "+info.LoaderVersion+".\nIn CurseForge: Profile Options → Current Modloader Version, then retry.");
        return installed;
    }

    static string DisplayLoader(string loader)=>loader.ToLowerInvariant() switch{"neoforge"=>"NeoForge","forge"=>"Forge","fabric"=>"Fabric",_=>loader};

    internal static bool SameServerVersion(LanSyncInfo info,PackSyncManifest manifest)=>
        info.ModHash==manifest.Hash&&info.ProjectId==manifest.ProjectId&&info.MinecraftVersion==manifest.MinecraftVersion&&
        info.Loader==manifest.Loader&&info.LoaderVersion==manifest.LoaderVersion;
}
