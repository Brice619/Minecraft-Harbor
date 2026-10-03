using System.Drawing.Imaging;

namespace MinecraftHarbor;

internal sealed class HomeFadeOverlay(Bitmap snapshot):FixedBackdropPanel
{
    internal float Alpha;
    internal void ReplaceSnapshot(Bitmap next){var old=snapshot;snapshot=next;old.Dispose();Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var attributes=new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix{Matrix33=Math.Clamp(Alpha,0,1)});
        e.Graphics.DrawImage(snapshot,ClientRectangle,0,0,snapshot.Width,snapshot.Height,GraphicsUnit.Pixel,attributes);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing)snapshot.Dispose();
        base.Dispose(disposing);
    }
}
