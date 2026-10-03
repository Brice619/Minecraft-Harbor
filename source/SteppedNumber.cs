namespace MinecraftHarbor;
internal sealed class SteppedNumber:NumericUpDown
{
    public override void UpButton(){ValidateEditText();Value=Math.Min(Maximum,(Math.Floor(Value/Increment)+1)*Increment);}
    public override void DownButton(){ValidateEditText();Value=Math.Max(Minimum,(Math.Ceiling(Value/Increment)-1)*Increment);}
}
