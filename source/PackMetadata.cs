using System.IO.Compression;
using System.Text.RegularExpressions;
namespace MinecraftHarbor;
internal static class PackMetadata
{
    internal static (string Minecraft,string Loader,string Version) Detect(string archive)
    {
        using var zip=ZipFile.OpenRead(archive);string minecraft="",loader="neoforge",version="";
        foreach(var entry in zip.Entries){string path=entry.FullName.Replace('\\','/');var args=Regex.Match(path,@"libraries/net/(neoforged/neoforge|minecraftforge/forge)/([^/]+)/win_args\.txt$");if(args.Success){loader=args.Groups[1].Value.StartsWith("neoforged")?"neoforge":"forge";version=args.Groups[2].Value;if(loader=="forge"){int split=version.IndexOf('-');if(split>0){minecraft=version[..split];version=version[(split+1)..];}}}
            if(entry.Length>131072||!(entry.Name.EndsWith(".bat")||entry.Name.Equals("variables.txt")||entry.Name.EndsWith(".properties")))continue;using var reader=new StreamReader(entry.Open());string text=reader.ReadToEnd();
            string Read(string key){var m=Regex.Match(text,@"(?im)^\s*(?:set\s+)?"+key+@"\s*=\s*[""']?([\w.\-+]+)");return m.Success?m.Groups[1].Value:"";}
            var mc=Read("MINECRAFT_VERSION");if(mc.Length>0)minecraft=mc;foreach(string name in new[]{"NEOFORGE","FORGE","FABRIC"}){string candidate=Read(name+"_VERSION");if(candidate.Length>0){loader=name.ToLowerInvariant();version=candidate;}}
        }
        if(minecraft.Length==0&&loader=="neoforge"&&version.StartsWith("21.1."))minecraft="1.21.1";
        return(minecraft,loader,version);
    }
}
