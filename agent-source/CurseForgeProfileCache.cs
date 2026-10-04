using System.Text.Json.Nodes;
namespace HarborAgent;

internal static class CurseForgeProfileCache
{
    // Overwolf persists the authoritative instance list separately from minecraftinstance.json.
    internal static string FilePath=>TestPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Overwolf","Curse","GameInstances","MinecraftGameInstance.json");
    internal static string? TestPath {get;set;}

    internal static LoaderProfileUpdate Attach(string folder,LoaderProfileUpdate update)
    {
        string path=FilePath;
        if(!File.Exists(path))return update;
        string before=File.ReadAllText(path);
        string? after=Build(before,folder,update.After);
        return after==null?update:update with{CachePath=path,CacheBefore=before,CacheAfter=after};
    }

    internal static string? Build(string before,string folder,string metadata)
    {
        var profile=JsonNode.Parse(metadata)!.AsObject();
        string guid=profile["guid"]?.GetValue<string>()??throw new InvalidDataException("Profile identity is missing.");
        var instances=JsonNode.Parse(before) as JsonArray??throw new InvalidDataException("CurseForge's profile cache is invalid. Repair CurseForge before retrying.");
        var matches=instances.Where(node=>string.Equals(node?["guid"]?.GetValue<string>(),guid,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length==0)return null;
        if(matches.Length!=1)throw new InvalidDataException("CurseForge's profile cache contains duplicate profile identities.");
        var cached=matches[0]!.AsObject();
        string cachedFolder=cached["installPath"]?.GetValue<string>()??"";
        if(cachedFolder.Length==0||!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(cachedFolder)),Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)),StringComparison.OrdinalIgnoreCase)||cached["gameVersion"]?.GetValue<string>()!=profile["gameVersion"]?.GetValue<string>())
            throw new InvalidDataException("CurseForge's cached profile does not match the selected pack folder.");
        cached["baseModLoader"]=profile["baseModLoader"]!.DeepClone();
        return instances.ToJsonString();
    }

    internal static void ValidatePath(string path)
    {
        if(!string.Equals(Path.GetFullPath(path),Path.GetFullPath(FilePath),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid CurseForge cache recovery location.");
    }
}
