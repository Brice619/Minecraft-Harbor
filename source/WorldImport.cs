using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MinecraftHarbor;

public sealed record WorldInfo(string Name,string MinecraftVersion,int DataVersion,string? PlayerUuid,byte[]? PlayerPayload);
public static class WorldImport
{
    sealed record Tag(byte Type,int Start,int End,object? Value);
    public static WorldInfo Read(string folder)
    {
        using var input=File.OpenRead(Path.Combine(folder,"level.dat"));using var gzip=new GZipStream(input,CompressionMode.Decompress);using var data=new MemoryStream();
        var buffer=new byte[65536];int n;while((n=gzip.Read(buffer))>0){if(data.Length+n>64*1024*1024)throw new InvalidDataException("World metadata is too large.");data.Write(buffer,0,n);}
        var bytes=data.ToArray();int pos=0;
        void Need(int count){if(count<0||pos>bytes.Length-count)throw new InvalidDataException("World metadata is damaged.");}
        byte Byte(){Need(1);return bytes[pos++];}
        int Int(){Need(4);int v=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos,4));pos+=4;return v;}
        string String(){Need(2);int length=BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos,2));pos+=2;Need(length);var s=Encoding.UTF8.GetString(bytes,pos,length);pos+=length;return s;}
        Tag Payload(byte type,int depth){
            if(depth>64)throw new InvalidDataException("World metadata is nested too deeply.");int start=pos;object? value=null;
            switch(type){
                case 1: value=(int)Byte();break;
                case 2: Need(2);pos+=2;break;
                case 3: value=Int();break;
                case 4:Need(8);value=BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(pos,8));pos+=8;break;
                case 6:Need(8);pos+=8;break;
                case 5:Need(4);pos+=4;break;
                case 7:{int count=Int();Need(count);pos+=count;break;}
                case 8:value=String();break;
                case 9:{byte sub=Byte();int count=Int();if(count<0||count>1_000_000)throw new InvalidDataException("Invalid world list.");for(int i=0;i<count;i++)Payload(sub,depth+1);break;}
                case 10:{var map=new Dictionary<string,Tag>();byte child;while((child=Byte())!=0){var name=String();map[name]=Payload(child,depth+1);}value=map;break;}
                case 11:{int count=Int();if(count<0||count>bytes.Length/4)throw new InvalidDataException("Invalid world array.");var values=new int[count];for(int i=0;i<count;i++)values[i]=Int();value=values;break;}
                case 12:{int count=Int();if(count<0||count>bytes.Length/8)throw new InvalidDataException("Invalid world array.");Need(count*8);pos+=count*8;break;}
                default:throw new InvalidDataException("Unknown world metadata tag.");
            }
            return new(type,start,pos,value);
        }
        if(Byte()!=10)throw new InvalidDataException("The selected save is not a Minecraft Java world.");String();
        var root=Payload(10,0).Value as Dictionary<string,Tag>??throw new InvalidDataException("Invalid world metadata.");
        if(!root.TryGetValue("Data",out var d)||d.Value is not Dictionary<string,Tag> world)throw new InvalidDataException("The save has no world data.");
        string name=world.TryGetValue("LevelName",out var t)?t.Value?.ToString()??"":Path.GetFileName(folder);
        string version=world.TryGetValue("Version",out t)&&t.Value is Dictionary<string,Tag> v&&v.TryGetValue("Name",out var ver)?ver.Value?.ToString()??"":"";
        int dataVersion=world.TryGetValue("DataVersion",out t)&&t.Value is int i?i:0;
        string? uuid=null;byte[]? player=null;
        if(world.TryGetValue("Player",out t)&&t.Value is Dictionary<string,Tag> p){
            byte[]? id=null;
            if(p.TryGetValue("UUID",out var u)&&u.Value is int[] ints&&ints.Length==4){id=new byte[16];for(int j=0;j<4;j++)BinaryPrimitives.WriteInt32BigEndian(id.AsSpan(j*4,4),ints[j]);}
            else if(p.TryGetValue("UUIDMost",out var most)&&most.Value is long high&&p.TryGetValue("UUIDLeast",out var least)&&least.Value is long low){id=new byte[16];BinaryPrimitives.WriteInt64BigEndian(id.AsSpan(0,8),high);BinaryPrimitives.WriteInt64BigEndian(id.AsSpan(8,8),low);}
            if(id!=null){var hex=Convert.ToHexString(id).ToLowerInvariant();uuid=$"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";player=bytes[t.Start..t.End];}
        }
        return new(name,version,dataVersion,uuid,player);
    }
    public static void Copy(string source,string destination,WorldInfo info,Action<string>? progress=null)
    {
        source=Path.GetFullPath(source);destination=Path.GetFullPath(destination);
        if(destination.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("A world cannot be copied into itself.");
        using var worldLock=File.Exists(Path.Combine(source,"session.lock"))?new FileStream(Path.Combine(source,"session.lock"),FileMode.Open,FileAccess.Read,FileShare.None):File.Open(Path.Combine(source,"level.dat"),FileMode.Open,FileAccess.Read,FileShare.Read);
        var lockedInfo=Read(source);if(lockedInfo.MinecraftVersion!=info.MinecraftVersion)throw new IOException("This save changed during import. Close Minecraft and try again.");info=lockedInfo;
        var files=SafeFiles(source).ToArray();long bytes=files.Sum(f=>new FileInfo(f).Length);
        if(new DriveInfo(Path.GetPathRoot(destination)!).AvailableFreeSpace<bytes+512L*1024*1024)throw new IOException("Not enough free space to copy this world.");
        Directory.CreateDirectory(destination);int done=0;
        foreach(var file in files){
            if(Path.GetFileName(file)=="session.lock")continue;
            var target=Path.Combine(destination,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target,false);
            if(++done%100==0)progress?.Invoke($"Copying world: {done:N0} / {files.Length:N0} files…");
        }
        if(info.PlayerPayload!=null&&info.PlayerUuid!=null){
            var folder=Path.Combine(destination,"playerdata");Directory.CreateDirectory(folder);var file=Path.Combine(folder,info.PlayerUuid+".dat");
            if(File.Exists(file))File.Copy(file,file+".before-harbor-import",false);
            using var output=File.Create(file);using var zipped=new GZipStream(output,CompressionLevel.Optimal);zipped.Write(new byte[]{10,0,0});zipped.Write(info.PlayerPayload);
        }
        _=Read(destination);
    }
    static IEnumerable<string> SafeFiles(string root)
    {
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a regular world folder, not a linked folder.");
        foreach(var file in Directory.EnumerateFiles(root)){
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("This world contains a linked file. Import a regular copy of the world.");yield return file;
        }
        foreach(var directory in Directory.EnumerateDirectories(root))foreach(var file in SafeFiles(directory))yield return file;
    }
}
