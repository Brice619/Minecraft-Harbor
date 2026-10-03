using SkiaSharp;
namespace MinecraftHarbor;
internal static class ArtworkTests
{
    internal static async Task Run(string report)
    {
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"artwork-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var checks=new List<string>();
        using(var image=new SKBitmap(12,10)){image.Erase(SKColors.Transparent);image.SetPixel(3,4,SKColors.Lime);using var encoded=SKImage.FromBitmap(image);foreach(var format in new[]{SKEncodedImageFormat.Webp,SKEncodedImageFormat.Png,SKEncodedImageFormat.Jpeg,SKEncodedImageFormat.Gif}){using var bytes=encoded.Encode(format,100);if(bytes==null)continue;string path=Path.Combine(root,format+".png");CatalogArtwork.SavePng(bytes.ToArray(),path,CancellationToken.None);using var bitmap=new Bitmap(path);if(bitmap.Width!=12||bitmap.Height!=10)throw new Exception("Artwork dimensions changed.");if(format==SKEncodedImageFormat.Png&&bitmap.GetPixel(0,0).A!=0)throw new Exception("Artwork transparency was lost.");checks.Add(format+" decodes to a valid cached PNG");}}
        string bad=Path.Combine(root,"corrupt.png");File.WriteAllText(bad,"not a PNG");if(CatalogArtwork.Valid(bad))throw new Exception("Corrupt artwork cache accepted.");bool rejected=false;try{CatalogArtwork.SavePng(new byte[]{1,2,3},bad,CancellationToken.None);}catch(InvalidDataException){rejected=true;}if(!rejected||Directory.EnumerateFiles(root,"*.partial").Any())throw new Exception("Invalid image was published.");checks.Add("Corrupt cache and invalid image bytes are rejected without leaving partial files");
        var catalog=new CurseForgeCatalog(root);using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(2));var page=await catalog.CombinedSearch("",2,0,false,deadline.Token);var projects=page.Items.Where(p=>p.Logo.Length>0).ToList();int webp=projects.Count(p=>new Uri(p.Logo).AbsolutePath.EndsWith(".webp",StringComparison.OrdinalIgnoreCase));
        using var limit=new SemaphoreSlim(4);var images=await Task.WhenAll(projects.Select(async project=>{await limit.WaitAsync(deadline.Token);try{string path=await catalog.Artwork(project,deadline.Token);if(!CatalogArtwork.Valid(path))throw new Exception("Missing artwork: "+project.Name);return new{project.Name,project.Logo,path};}finally{limit.Release();}}));
        if(images.Length==0||webp==0)throw new Exception("Live artwork check did not exercise WebP thumbnails.");string first=images[0].path;File.WriteAllText(first,"damaged cache");await catalog.Artwork(projects[0],deadline.Token);if(!CatalogArtwork.Valid(first))throw new Exception("Damaged cached artwork was not repaired.");checks.Add("Every publisher image on the live first catalog page loads, including WebP thumbnails; damaged cache repairs on retry");
        ServerManager.WriteJson(report,new{passed=true,checks,imagesLoaded=images.Length,webpImages=webp,images,realWorldModified=false});
    }
}
