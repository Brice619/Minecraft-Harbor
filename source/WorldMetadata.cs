using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace MinecraftHarbor;

// Keeps every unedited NBT payload byte, including mod-specific metadata.
internal sealed class WorldMetadata
{
    readonly byte[] data;
    readonly Dictionary<string,(byte Type,int Start,int End)> tags=new();
    public WorldMetadata(string file)
    {
        using var input=File.OpenRead(file);using var gzip=new GZipStream(input,CompressionMode.Decompress);using var output=new MemoryStream();
        var buffer=new byte[65536];int count;while((count=gzip.Read(buffer))>0){if(output.Length+count>64*1024*1024)throw new InvalidDataException("World metadata is too large.");output.Write(buffer,0,count);}data=output.ToArray();
        int pos=0;
        void Need(int n){if(n<0||pos>data.Length-n)throw new InvalidDataException("Invalid world metadata.");}
        byte Byte(){Need(1);return data[pos++];}
        int Int(){Need(4);int n=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos,4));pos+=4;return n;}
        string Str(){Need(2);int n=BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos,2));pos+=2;Need(n);string s=Encoding.UTF8.GetString(data,pos,n);pos+=n;return s;}
        void Payload(byte type,string path,int depth){if(depth>64)throw new InvalidDataException("NBT nesting too deep.");int start=pos;
            switch(type){
                case 1:Need(1);pos++;break;case 2:Need(2);pos+=2;break;case 3:case 5:Need(4);pos+=4;break;case 4:case 6:Need(8);pos+=8;break;
                case 7:{int n=Int();Need(n);pos+=n;break;}case 8:Str();break;
                case 9:{byte sub=Byte();int n=Int();if(n<0||n>1000000)throw new InvalidDataException("Invalid NBT list.");for(int i=0;i<n;i++)Payload(sub,path+"/[]",depth+1);break;}
                case 10:{byte sub;while((sub=Byte())!=0){string name=Str();Payload(sub,path+"/"+name,depth+1);}break;}
                case 11:case 12:{int n=Int();long bytes=(long)n*(type==11?4:8);if(bytes<0||bytes>data.Length)throw new InvalidDataException("Invalid NBT array.");Need((int)bytes);pos+=(int)bytes;break;}
                default:throw new InvalidDataException("Unknown NBT tag.");
            }
            tags[path]=(type,start,pos);
        }
        if(Byte()!=10)throw new InvalidDataException("Invalid world root.");Str();Payload(10,"",0);
    }
    public Dictionary<string,string> Rules()=>tags.Where(t=>(t.Key.StartsWith("/Data/GameRules/")||t.Key.StartsWith("/data/minecraft:"))&&t.Value.Type is 1 or 3 or 8).ToDictionary(t=>t.Key[(t.Key.StartsWith("/Data/GameRules/")?16:6)..],t=>t.Value.Type switch{1=>data[t.Value.Start]==0?"false":"true",3=>BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(t.Value.Start,4)).ToString(),_=>ReadString(t.Value.Start)});
    string ReadString(int start){int length=BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(start,2));return Encoding.UTF8.GetString(data,start+2,length);}
    public void Save(string file,string? name,Dictionary<string,string> rules)
    {
        var edits=new List<(int Start,int End,byte[] Bytes)>();
        void SetString(string key,string value){if(!tags.TryGetValue(key,out var t)||t.Type!=8)throw new InvalidDataException("Missing world setting: "+key);var text=Encoding.UTF8.GetBytes(value);if(text.Length>65535)throw new ArgumentException("World setting too long.");var bytes=new byte[text.Length+2];BinaryPrimitives.WriteUInt16BigEndian(bytes,(ushort)text.Length);text.CopyTo(bytes,2);edits.Add((t.Start,t.End,bytes));}
        if(name!=null)SetString("/Data/LevelName",name);
        foreach(var rule in rules){
            string key=rule.Key.StartsWith("minecraft:")?"/data/"+rule.Key:"/Data/GameRules/"+rule.Key;
            if(!tags.TryGetValue(key,out var tag))throw new InvalidDataException("Missing game rule: "+rule.Key);
            if(tag.Type==8)SetString(key,rule.Value);
            else if(tag.Type==1)edits.Add((tag.Start,tag.End,new byte[]{bool.Parse(rule.Value)?(byte)1:(byte)0}));
            else if(tag.Type==3){var bytes=new byte[4];BinaryPrimitives.WriteInt32BigEndian(bytes,int.Parse(rule.Value));edits.Add((tag.Start,tag.End,bytes));}
            else throw new InvalidDataException("Unsupported game rule type.");
        }
        using var output=new MemoryStream();int pos=0;foreach(var edit in edits.OrderBy(e=>e.Start)){output.Write(data,pos,edit.Start-pos);output.Write(edit.Bytes);pos=edit.End;}output.Write(data,pos,data.Length-pos);
        string temp=file+".harbor-new";using(var target=File.Create(temp))using(var gzip=new GZipStream(target,CompressionLevel.Optimal))gzip.Write(output.ToArray());
        _=new WorldMetadata(temp);File.Move(temp,file,true);
    }
}
