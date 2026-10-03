namespace MinecraftHarbor;
internal sealed class AlignedLabel:Label
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var flags=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl;
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ForeColor,flags);
    }
}
