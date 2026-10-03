using System.Text.Json;
using System.Text.RegularExpressions;
namespace MinecraftHarbor;

internal sealed record VanillaVersion(string Id,string Url,DateTime Released)
{
    public Version Number=>Version.Parse(Id.Contains('.')?Id:Id+".0");
    public bool ModernRules=>Number>=new Version(1,21,11);
    public (string Era,string Description) Story=>VanillaCatalog.Story(Id);
}
internal static class VanillaCatalog
{
    internal const string Manifest="https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    static readonly HttpClient Http=new(){Timeout=TimeSpan.FromSeconds(25)};
    internal static async Task<List<VanillaVersion>> Load(string root)
    {
        var path=Path.Combine(root,"downloads","vanilla-versions.json");string json;
        try{json=await Http.GetStringAsync(Manifest);using var check=JsonDocument.Parse(json);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,json);}
        catch(Exception e)when(e is HttpRequestException or TaskCanceledException){if(!File.Exists(path))throw;json=File.ReadAllText(path);}
        using var doc=JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("versions").EnumerateArray().Where(e=>e.GetProperty("type").GetString()=="release")
            .Select(e=>new VanillaVersion(e.GetProperty("id").GetString()!,e.GetProperty("url").GetString()!,e.GetProperty("releaseTime").GetDateTime()))
            .Where(v=>Version.TryParse(v.Id,out var n)&&n>=new Version(1,2,5)).OrderByDescending(v=>v.Released).ToList();
    }
    internal static async Task<JsonDocument> Details(VanillaVersion version)=>JsonDocument.Parse(await Http.GetStringAsync(version.Url));
    internal static (string,string) Story(string id)
    {
        var v=Version.Parse(id);int n=v.Minor;
        if(v.Major==26)return n switch{3=>("Wilderness Bound era","Dappled forests, camps, and wilderness exploration."),2=>("Chaos Cubed era","Sulfur caves, sulfur cubes, and new building blocks."),1=>("Tiny Takeover era","Updated baby mobs and golden dandelions."),_=>("2026 game drops","Java Edition release "+id)};
        if(v.Major>26)return ("Modern Minecraft era","Java Edition release "+id);
        return n switch{
            21 when v.Build>=11=>("Mounts of Mayhem era","Spears, new mounts, and mounted combat."),
            21 when v.Build>=9=>("The Copper Age era","Copper golems, copper equipment, and copper storage."),
            21 when v.Build>=6=>("Chase the Skies era","Happy ghasts and adventures above the clouds."),
            21 when v.Build>=5=>("Spring to Life era","More variety in animals and the natural world."),
            21 when v.Build>=4=>("The Garden Awakens era","Pale gardens, creakings, and resin."),
            21=>("Tricky Trials era","Trial chambers, breezes, and the mace."),
            20=>("Trails & Tales era","Archaeology, cherry groves, camels, and bamboo."),
            19=>("The Wild Update era","The deep dark, the Warden, and mangrove swamps."),
            18=>("Caves & Cliffs Part II era","Deeper caves, taller mountains, and new terrain."),
            17=>("Caves & Cliffs Part I era","Copper, amethyst, axolotls, and goats."),
            16=>("Nether Update era","Nether biomes, piglins, and ancient debris."),
            15=>("Buzzy Bees era","Bees, honey, and beehives."),
            14=>("Village & Pillage era","New villages, professions, pillagers, and raids."),
            13=>("Update Aquatic era","Coral reefs, shipwrecks, and ocean exploration."),
            12=>("World of Color era","Concrete, parrots, and colorful building blocks."),
            11=>("Exploration Update era","Woodland mansions, illagers, llamas, and shulker boxes."),
            10=>("Frostburn Update era","Polar bears, husks, strays, and magma blocks."),
            9=>("Combat Update era","Shields, elytra, expanded End islands, and new combat."),
            8=>("Bountiful Update era","Ocean monuments, guardians, and slime blocks."),
            7=>("The Update that Changed the World era","New biomes, flowers, and varied landscapes."),
            6=>("Horse Update era","Horses, leads, hay bales, and carpets."),
            5=>("Redstone Update era","Hoppers, comparators, and advanced automation."),
            4=>("Pretty Scary Update era","The Wither, beacons, witches, and command blocks."),
            3=>("Early release era","Emerald trading, desert temples, and jungle temples."),
            _=>("Jungle Update era","Jungles, ocelots, iron golems, and taller worlds.")};
    }
    internal static Dictionary<string,string> Properties(VanillaVersion version)
    {
        var p=new Dictionary<string,string>{{"difficulty","normal"},{"gamemode","survival"},{"pvp","true"},{"online-mode","true"},{"hardcore","false"},{"spawn-protection","16"},{"allow-flight","false"}};
        if(version.Number>=new Version(1,4))p["enable-command-block"]="false";
        return p;
    }
    internal static Dictionary<string,string> Rules(VanillaVersion version)
    {
        var p=new Dictionary<string,string>();var v=version.Number;
        if(v<new Version(1,4))return p;
        foreach(var k in new[]{"keepInventory","doMobSpawning","doFireTick","mobGriefing"})p[k]=GameDefaults.Rules[k];
        if(v>=new Version(1,6))p["doDaylightCycle"]="true";
        if(v>=new Version(1,8))p["randomTickSpeed"]="3";
        if(v>=new Version(1,11)){p["doWeatherCycle"]="true";p["maxEntityCramming"]="24";}
        if(v>=new Version(1,12))p["announceAdvancements"]="true";
        return p;
    }
    internal static KeyValuePair<string,string> RuleCommand(string version,string key,string value)
    {
        if(Version.Parse(version)<new Version(1,21,11))return new(key,value);
        string name=key switch{"doDaylightCycle"=>"advance_time","doMobSpawning"=>"spawn_mobs","doWeatherCycle"=>"advance_weather","announceAdvancements"=>"show_advancement_messages","doFireTick"=>"fire_spread_radius_around_player",_=>Regex.Replace(key,"([a-z])([A-Z])","$1_$2").ToLowerInvariant()};
        return new("minecraft:"+name,key=="doFireTick"?(value=="true"?"128":"0"):value);
    }
}
