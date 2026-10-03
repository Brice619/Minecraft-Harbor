using System.Diagnostics;

namespace MinecraftHarbor;
public sealed partial class MainForm
{
    HomeFadeOverlay? pageEntrance;
    int pageEntranceGeneration;
    string pageEntranceName="";
    bool MotionEnabled=>preferences.Animations&&SystemInformation.IsMenuAnimationEnabled;
    void PreparePageEntrance(string name)
    {
        if(!IsHandleCreated||!Visible||!MotionEnabled||name is not ("Backups" or "Players" or "Settings" or "LAN PCs"))return;
        var page=pages[name];if(page.Width<1||page.Height<1)return;
        pageEntrance=new HomeFadeOverlay(new Bitmap(page.Width,page.Height)){Bounds=page.Bounds,Alpha=0};pageEntranceName=name;
        content.Controls.Add(pageEntrance);pageEntrance.BringToFront();
    }
    async void AnimatePageEntrance()
    {
        var overlay=pageEntrance;if(overlay==null)return;int generation=pageEntranceGeneration;string name=pageEntranceName;
        bool Current()=>generation==pageEntranceGeneration&&!IsDisposed&&!overlay.IsDisposed&&pages[name].Visible;
        try{
            // Cover the new page before its first paint; wait briefly for its actual rows.
            if(name=="Players"){var ready=Stopwatch.StartNew();while(playerLoadRunning&&ready.ElapsedMilliseconds<300&&Current())await Task.Delay(15);}
            if(!Current())return;var page=pages[name];page.PerformLayout();overlay.Bounds=page.Bounds;var snapshot=new Bitmap(page.Width,page.Height);try{page.DrawToBitmap(snapshot,page.ClientRectangle);PaintEntryInputs(page,snapshot);overlay.ReplaceSnapshot(snapshot);}catch{snapshot.Dispose();throw;}
            var watch=Stopwatch.StartNew();int duration=preferences.FadeMilliseconds;
            while(watch.ElapsedMilliseconds<duration&&Current()){float t=Math.Min(1,watch.ElapsedMilliseconds/(float)duration);overlay.Alpha=t*t*(3-2*t);overlay.Invalidate();await Task.Delay(15);}
            if(Current())FinishPageEntrance();
        }catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException){server.Log("Page entrance unavailable: "+ex.Message);if(generation==pageEntranceGeneration)FinishPageEntrance();}
    }
    static void PaintEntryInputs(Control page,Bitmap image)
    {
        using var g=Graphics.FromImage(image);
        void Visit(Control parent,Point origin){foreach(Control child in parent.Controls){if(!child.Visible)continue;var location=new Point(origin.X+child.Left,origin.Y+child.Top);if(child is TextBox box&&box.TextLength==0&&box.PlaceholderText.Length>0){using var fill=new SolidBrush(box.BackColor);g.FillRectangle(fill,new Rectangle(location,box.Size));TextRenderer.DrawText(g,box.PlaceholderText,box.Font,new Rectangle(location,box.Size),SystemColors.GrayText,TextFormatFlags.NoPadding|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);}Visit(child,location);}}
        Visit(page,Point.Empty);
    }
    void FinishPageEntrance(){pageEntranceGeneration++;var old=pageEntrance;pageEntrance=null;pageEntranceName="";if(old!=null){old.Parent?.Controls.Remove(old);old.Dispose();}}
}
