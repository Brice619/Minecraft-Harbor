using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using Tesseract;
namespace MinecraftHarbor;
internal static class ArtworkFocus
{
    // Analyze the current artwork, never use pack IDs or image-specific crop coordinates.
    public static RectangleF? Detect(Bitmap artwork,string packName)
    {
        var data=Path.Combine(AppContext.BaseDirectory,"tessdata");if(!File.Exists(Path.Combine(data,"eng.traineddata")))return null;
        float scale=Math.Min(1,1200f/Math.Max(artwork.Width,artwork.Height));
        using var bitmap=new Bitmap(Math.Max(1,(int)(artwork.Width*scale)),Math.Max(1,(int)(artwork.Height*scale)),PixelFormat.Format24bppRgb);
        using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.White);g.DrawImage(artwork,new Rectangle(0,0,bitmap.Width,bitmap.Height));}
        using var lettering=IsolateBrightLettering(bitmap);
        TesseractEnviornment.CustomSearchPath=AppContext.BaseDirectory;
        using var engine=new TesseractEngine(data,"eng",EngineMode.LstmOnly);
        // Pack logos often tilt their lettering. Try the same small orientation
        // range for every image, and map recognized words back to the original.
        foreach(var candidate in new[]{bitmap,lettering})
        foreach(int angle in new[]{0,15,-15,30,-30}){
            double radians=angle*Math.PI/180;
            int width=(int)Math.Ceiling(Math.Abs(bitmap.Width*Math.Cos(radians))+Math.Abs(bitmap.Height*Math.Sin(radians)));
            int height=(int)Math.Ceiling(Math.Abs(bitmap.Width*Math.Sin(radians))+Math.Abs(bitmap.Height*Math.Cos(radians)));
            using var oriented=new Bitmap(width,height,PixelFormat.Format24bppRgb);
            using var transform=new Matrix();transform.Translate(width/2f,height/2f);transform.Rotate(angle);transform.Translate(-bitmap.Width/2f,-bitmap.Height/2f);
            using(var g=Graphics.FromImage(oriented)){g.Clear(Color.White);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.Transform=transform;g.DrawImage(candidate,0,0,bitmap.Width,bitmap.Height);}
            transform.Invert();using var bytes=new MemoryStream();oriented.Save(bytes,System.Drawing.Imaging.ImageFormat.Png);using var image=Pix.LoadFromMemory(bytes.ToArray());
            using var page=engine.Process(image,PageSegMode.SparseText);using var iterator=page.GetIterator();var words=new List<(string Text,RectangleF Box,float Confidence)>();
            iterator.Begin();do{
                string text=iterator.GetText(PageIteratorLevel.Word)?.Trim()??"";
                if(text.Any(char.IsLetterOrDigit)&&iterator.GetConfidence(PageIteratorLevel.Word)>=45&&iterator.TryGetBoundingBox(PageIteratorLevel.Word,out var box)){
                    var corners=new[]{new PointF(box.X1,box.Y1),new PointF(box.X2,box.Y1),new PointF(box.X2,box.Y2),new PointF(box.X1,box.Y2)};transform.TransformPoints(corners);
                    var bounds=RectangleF.FromLTRB(corners.Min(p=>p.X)/scale,corners.Min(p=>p.Y)/scale,corners.Max(p=>p.X)/scale,corners.Max(p=>p.Y)/scale);
                    words.Add((text,bounds,iterator.GetConfidence(PageIteratorLevel.Word)));
                }
            }while(iterator.Next(PageIteratorLevel.Word));
            var focus=Choose(words,artwork.Size,packName);if(focus!=null)return focus;
        }
        return null;
    }
    static Bitmap IsolateBrightLettering(Bitmap source)
    {
        // A color-independent contrast pass separates bright, saturated logo faces
        // from photographic scenery and dark 3D shadows before recognition.
        var result=source.Clone(new Rectangle(Point.Empty,source.Size),PixelFormat.Format24bppRgb);
        var data=result.LockBits(new Rectangle(Point.Empty,result.Size),ImageLockMode.ReadWrite,PixelFormat.Format24bppRgb);
        try{
            var pixels=new byte[data.Stride*data.Height];System.Runtime.InteropServices.Marshal.Copy(data.Scan0,pixels,0,pixels.Length);
            for(int y=0;y<data.Height;y++)for(int x=0;x<result.Width;x++){
                int i=y*data.Stride+x*3;int high=Math.Max(pixels[i],Math.Max(pixels[i+1],pixels[i+2])),low=Math.Min(pixels[i],Math.Min(pixels[i+1],pixels[i+2]));
                byte value=high>=170&&high-low>=high*.55? (byte)0:(byte)255;pixels[i]=pixels[i+1]=pixels[i+2]=value;
            }
            System.Runtime.InteropServices.Marshal.Copy(pixels,0,data.Scan0,pixels.Length);
        }finally{result.UnlockBits(data);}
        return result;
    }
    internal static RectangleF? Choose(List<(string Text,RectangleF Box,float Confidence)> words,Size image,string packName)
    {
        if(words.Count==0)return null;
        var tokens=System.Text.RegularExpressions.Regex.Matches(packName.ToLowerInvariant(),@"[a-z0-9]{2,}").Select(m=>m.Value).ToHashSet();
        var matched=words.Where(w=>tokens.Contains(new string(w.Text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()))).ToList();
        // Restrict focus to the installed pack's title. Texture and scenery can
        // resemble large letters; unrelated OCR guesses are not a useful crop.
        var pool=matched;
        if(pool.Count==0)return null;
        // A recognized version number alone is not the pack title. Keep the full
        // image when stylized lettering could not be read reliably.
        if(!pool.Any(w=>w.Text.Count(char.IsLetter)>=3))return null;
        int titleWords=tokens.Count(t=>t.Count(char.IsLetter)>=3);
        if(pool.Where(w=>w.Text.Count(char.IsLetter)>=3).Select(w=>w.Text.ToLowerInvariant()).Distinct().Count()<Math.Min(2,titleWords))return null;
        var bounds=pool.Select(w=>w.Box).Aggregate(RectangleF.Union);
        // Include neighboring title words/numbers of comparable size, then add generous room
        // for outlines and stylized lettering that OCR can only partly recognize.
        var nearby=bounds;nearby.Inflate(image.Width*.08f,image.Height*.10f);
        foreach(var word in words.Where(w=>w.Box.IntersectsWith(nearby)&&w.Box.Height>=pool.Max(x=>x.Box.Height)*.35f))bounds=RectangleF.Union(bounds,word.Box);
        bounds.Inflate(Math.Max(image.Width*.06f,bounds.Width*.12f),Math.Max(image.Height*.06f,bounds.Height*.2f));
        bounds=RectangleF.Intersect(bounds,new RectangleF(PointF.Empty,image));
        if(bounds.Width<image.Width*.15f||bounds.Height<image.Height*.08f||bounds.Width*bounds.Height>image.Width*image.Height*.92f)return null;
        return bounds;
    }
}
