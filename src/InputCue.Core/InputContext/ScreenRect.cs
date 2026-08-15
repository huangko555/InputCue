namespace InputCue.Core.InputContext;

public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public bool IsUsable =>
        double.IsFinite(X) &&
        double.IsFinite(Y) &&
        double.IsFinite(Width) &&
        double.IsFinite(Height) &&
        Width > 0 &&
        Height > 0;
}
