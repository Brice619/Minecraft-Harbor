using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MinecraftHarbor;
internal sealed class AppearanceSettings
{
    public string Background {get;set;}="night";
    public static AppearanceSettings Load(string root)
    {
        try{var p=Path.Combine(root,"appearance.json");return File.Exists(p)?JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(p))??new():new();}
        catch(Exception e)when(e is IOException or JsonException){return new();}
    }
}
internal static class PackArtwork
{
    public static Bitmap NightScene()
        =>EmbeddedImage("night-harbor.png");
    public static Bitmap Wordmark()
        =>EmbeddedImage("harbor-wordmark.png");
    public static Icon AppIcon()
    {
        var assembly=Assembly.GetExecutingAssembly();using var stream=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith("harbor.ico")))!;
        using var icon=new Icon(stream,256,256);return (Icon)icon.Clone();
    }
    static Bitmap EmbeddedImage(string name)
    {
        var assembly=Assembly.GetExecutingAssembly();using var stream=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith(name)))!;
        using var image=Image.FromStream(stream);return new Bitmap(image);
    }
    public static Rectangle VisibleBounds(Bitmap image)
    {
        using var pixels=new Bitmap(image.Width,image.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(pixels))g.DrawImageUnscaled(image,0,0);
        var data=pixels.LockBits(new Rectangle(Point.Empty,pixels.Size),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        int left=image.Width,top=image.Height,right=-1,bottom=-1;var row=new byte[Math.Abs(data.Stride)];
        try{for(int y=0;y<image.Height;y++){System.Runtime.InteropServices.Marshal.Copy(data.Scan0+y*data.Stride,row,0,row.Length);for(int x=0;x<image.Width;x++)if(row[x*4+3]>8){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}}}
        finally{pixels.UnlockBits(data);}
        return right<left?new Rectangle(Point.Empty,image.Size):Rectangle.FromLTRB(left,top,right+1,bottom+1);
    }
    public static async Task<Bitmap?> Load(string root,ServerProfile profile,CancellationToken token)
    {
        var cache=Path.Combine(root,"artwork",profile.Id+".image");
        if(File.Exists(cache)){try{return ReadImage(cache);}catch(Exception e)when(e is ArgumentException or IOException){}}
        var metadata=Path.Combine(profile.CurseForgePath,"minecraftinstance.json");if(!File.Exists(metadata))return null;
        using var json=JsonDocument.Parse(await File.ReadAllTextAsync(metadata,token));
        if(!json.RootElement.TryGetProperty("installedModpack",out var pack)||pack.ValueKind!=JsonValueKind.Object||!pack.TryGetProperty("thumbnailUrl",out var thumbnail))return null;
        if(!Uri.TryCreate(thumbnail.GetString(),UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="media.forgecdn.net")return null;
        using var handler=new HttpClientHandler{AllowAutoRedirect=false};using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(15)};
        var fullPath=Regex.Replace(uri.AbsolutePath,@"^/avatars/thumbnails/(\d+)/(\d+)/\d+/\d+/", "/avatars/$1/$2/");
        var requested=new UriBuilder(uri){Path=fullPath}.Uri;
        var fetched=await http.GetAsync(requested,HttpCompletionOption.ResponseHeadersRead,token);
        if(!fetched.IsSuccessStatusCode&&requested!=uri){fetched.Dispose();requested=uri;fetched=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);}
        using var response=fetched;response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>10*1024*1024)throw new IOException("The pack artwork is too large.");
        using var source=await response.Content.ReadAsStreamAsync(token);using var bytes=new MemoryStream();var buffer=new byte[65536];int count;
        while((count=await source.ReadAsync(buffer,token))>0){if(bytes.Length+count>10*1024*1024)throw new IOException("The pack artwork is too large.");bytes.Write(buffer,0,count);}
        bytes.Position=0;using var decoded=Image.FromStream(bytes);if(decoded.Width>8192||decoded.Height>8192)throw new IOException("The pack artwork is too large.");
        var result=new Bitmap(decoded);Directory.CreateDirectory(Path.GetDirectoryName(cache)!);await File.WriteAllBytesAsync(cache+".new",bytes.ToArray(),token);File.Move(cache+".new",cache,true);
        ServerManager.WriteJson(cache+".source.json",new{source=requested.ToString(),pack=profile.Name,checkedUtc=DateTime.UtcNow});return result;
    }
    static Bitmap ReadImage(string path){using var image=Image.FromFile(path);return new Bitmap(image);}
}
