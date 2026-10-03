namespace MinecraftHarbor;
internal static class PlayerAvatars
{
    static readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(8)};
    static readonly SemaphoreSlim downloads=new(3);
    internal static async Task<Bitmap?> Load(string root,string id,CancellationToken token)
    {
        if(!Guid.TryParse(id,out var uuid))return null;var directory=Path.Combine(root,"cache","player-heads");var file=Path.Combine(directory,uuid.ToString("N")+".png");
        if(File.Exists(file))try{using var saved=new Bitmap(file);return new Bitmap(saved);}catch(ArgumentException){}
        await downloads.WaitAsync(token);try{
            foreach(string url in new[]{"https://mc-heads.net/avatar/"+uuid.ToString("N")+"/64","https://crafatar.com/avatars/"+uuid.ToString("N")+"?size=64&overlay"}){
                try{using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>262144)throw new IOException("Player image is too large.");using var stream=await response.Content.ReadAsStreamAsync(token);using var data=new MemoryStream();var buffer=new byte[8192];int count;while((count=await stream.ReadAsync(buffer,token))>0){if(data.Length+count>262144)throw new IOException("Player image is too large.");data.Write(buffer,0,count);}data.Position=0;using var image=new Bitmap(data);if(image.Width>512||image.Height>512)throw new IOException("Player image is too large.");var result=new Bitmap(image);try{Directory.CreateDirectory(directory);result.Save(file,System.Drawing.Imaging.ImageFormat.Png);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}return result;}
                catch(Exception ex)when(!token.IsCancellationRequested&&(ex is HttpRequestException or IOException or ArgumentException or OperationCanceledException)){}
            }
            return null;
        }
        finally{downloads.Release();}
    }
}
