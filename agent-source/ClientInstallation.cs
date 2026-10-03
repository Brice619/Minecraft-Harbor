using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
namespace HarborAgent;

internal static class ClientInstallation
{
    internal static bool Exists(string root)=>File.Exists(Path.Combine(root,"Minecraft Harbor Client.exe"));
    static string Hash(string file){using var input=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(input));}
    static string Target(string root,string relative)
    {
        string full=Path.GetFullPath(Path.Combine(root,relative));
        if(!full.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid client installation path.");
        return full;
    }
    internal static void Install(string source,string root)
    {
        if(!File.Exists(source))throw new IOException("The client installer is missing.");
        HarborUpdates.UpdateShutdown.CloseApplication(Path.Combine(root,"Minecraft Harbor Client.exe"),TimeSpan.FromMinutes(5));
        Directory.CreateDirectory(root);
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("The client installation folder cannot be a link.");
        string staged=Path.Combine(root,".harbor-install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staged);
        try
        {
            using var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("Harbor.ClientPayload.zip")??throw new InvalidDataException("The installer is missing its compiled client files.");
            using var expectedStream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Harbor.ClientPayload.sha256")??throw new InvalidDataException("The installer checksum is missing.");
            using var reader=new StreamReader(expectedStream);string expected=reader.ReadToEnd().Trim();
            string package=Path.Combine(staged,"payload.zip");using(var output=File.Create(package))payload.CopyTo(output);
            if(Hash(package)!=expected)throw new InvalidDataException("The compiled client package failed verification.");
            string extracted=Path.Combine(staged,"files");Directory.CreateDirectory(extracted);
            var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            using(var zip=ZipFile.OpenRead(package))
                foreach(var entry in zip.Entries)
                {
                    if(entry.FullName.EndsWith('/'))continue;
                    string target=Target(extracted,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(target)!);entry.ExtractToFile(target);files.Add(entry.FullName,Hash(target));
                }
            foreach(string required in new[]{"Minecraft Harbor Client.exe","Minecraft Harbor Client.dll","Minecraft Harbor Client.runtimeconfig.json","Minecraft Harbor Client.deps.json","System.Management.dll","hostfxr.dll","coreclr.dll"})
                if(!files.ContainsKey(required))throw new InvalidDataException("The installer is missing "+required);
            // Check all destinations before replacing files. Connection settings are outside the payload.
            foreach(string relative in files.Keys)
            {
                string target=Target(root,relative);
                if(File.Exists(target))using(File.Open(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
            }
            foreach(var file in files)
            {
                string target=Target(root,file.Key),incoming=Target(extracted,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if(File.Exists(target))File.Replace(incoming,target,null);else File.Move(incoming,target);
                if(Hash(target)!=file.Value)throw new IOException("An installed client file failed verification: "+file.Key);
            }
            File.WriteAllText(Path.Combine(root,"installed-files.json"),JsonSerializer.Serialize(new{version="1.2.0",files=files.Keys}));
        }
        catch(IOException ex){throw new IOException("Client update failed: "+ex.Message,ex);}
        finally{if(Directory.Exists(staged))Directory.Delete(staged,true);}
    }
}
