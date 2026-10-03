using SkiaSharp;
namespace MinecraftHarbor;

internal static class CatalogArtwork
{
    internal static bool Valid(string path)
    {
        if(!File.Exists(path))return false;
        try{using var stream=File.OpenRead(path);using var image=Image.FromStream(stream,true,true);return image.Width>0&&image.Height>0;}
        catch(Exception ex)when(ex is IOException or ArgumentException or OutOfMemoryException){return false;}
    }
    internal static void SavePng(byte[] bytes,string path,CancellationToken token)
    {
        using var data=SKData.CreateCopy(bytes);using var codec=SKCodec.Create(data)??throw new InvalidDataException("The publisher's artwork is not a supported image.");
        if(codec.Info.Width<=0||codec.Info.Height<=0||(long)codec.Info.Width*codec.Info.Height>40000000)throw new InvalidDataException("The publisher's artwork is too large.");
        using var bitmap=SKBitmap.Decode(codec)??throw new InvalidDataException("The publisher's artwork could not be decoded.");using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100)??throw new InvalidDataException("The artwork could not be cached.");
        token.ThrowIfCancellationRequested();Directory.CreateDirectory(Path.GetDirectoryName(path)!);string partial=path+"."+Guid.NewGuid().ToString("N")+".partial";
        try{using(var output=File.Create(partial))png.SaveTo(output);token.ThrowIfCancellationRequested();File.Move(partial,path,true);}finally{if(File.Exists(partial))File.Delete(partial);}
    }
}
