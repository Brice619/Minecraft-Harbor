using System.Security.Cryptography;
using System.Text;
namespace MinecraftHarbor;
public sealed record PackSyncFile(string Path,string Hash,long Size);
public sealed record PackSyncManifest(string ServerId,long ProjectId,string Name,string MinecraftVersion,string Loader,string LoaderVersion,string Hash,PackSyncFile[] Files);
public sealed record PackSyncRequest(string Id,string Token,string ManifestHash,string Path="");
public static class PackSyncPaths
{
    // Launch synchronization is limited to mod JARs.
    public static readonly string[] Roots=["mods"];
    public static bool Allowed(string path){var parts=path.Split('/');return path.Length<=240&&parts.Length>=2&&Roots.Contains(parts[0],StringComparer.OrdinalIgnoreCase)&&System.IO.Path.GetExtension(path).Equals(".jar",StringComparison.OrdinalIgnoreCase)&&parts.All(p=>p.Length>0&&p is not "." and not ".."&&!p.StartsWith('.')&&!p.EndsWith('.')&&!p.EndsWith(' ')&&!p.Any(c=>char.IsControl(c)||"\\:*?\"<>|".Contains(c)))&&!new[]{".exe",".dll",".bat",".cmd",".ps1",".lnk",".url"}.Contains(System.IO.Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);}
    public static string Resolve(string root,string path){if(!Allowed(path))throw new InvalidDataException("Invalid pack content path.");root=System.IO.Path.GetFullPath(root);string target=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,path.Replace('/',System.IO.Path.DirectorySeparatorChar)));if(!target.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Pack path escapes its folder.");string? cursor=target;while(cursor!=null&&cursor.Length>=root.Length){if((File.Exists(cursor)||Directory.Exists(cursor))&&(File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked folders cannot be synced.");cursor=System.IO.Path.GetDirectoryName(cursor);}return target;}
    public static IEnumerable<string> Enumerate(string root){foreach(string dir in Roots){string start=Resolve(root,dir+"/placeholder.jar");start=System.IO.Path.GetDirectoryName(start)!;if(!Directory.Exists(start))continue;var pending=new Stack<string>();pending.Push(start);while(pending.TryPop(out var folder)){foreach(var entry in Directory.EnumerateFileSystemEntries(folder)){if((File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked folders cannot be synced.");string path=System.IO.Path.GetRelativePath(root,entry).Replace('\\','/');if(System.IO.Path.GetFileName(entry).StartsWith('.'))continue;if(Directory.Exists(entry)){pending.Push(entry);continue;}if(Allowed(path))yield return path;}}}}
    public static string HashFile(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    public static string HashList(IEnumerable<PackSyncFile> files)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',files.OrderBy(f=>f.Path,StringComparer.Ordinal).Select(f=>f.Path+"|"+f.Hash+"|"+f.Size)))));
}
