namespace AIDrawer.Core;

public sealed record WindowWorkArea(int X, int Y, int Width, int Height)
{
    public bool IsUsable => Width > 0 && Height > 0;
}

public static class WindowPlacementPolicy
{
    public const int MinimumWidth = 720;
    public const int MinimumHeight = 540;
    public const int MaximumDimension = short.MaxValue;

    public static bool IsValid(WindowPlacementSnapshot placement) =>
        placement.Width >= MinimumWidth
        && placement.Height >= MinimumHeight
        && placement.Width <= MaximumDimension
        && placement.Height <= MaximumDimension;

    public static WindowPlacementSnapshot? ClampToWorkArea(
        WindowPlacementSnapshot placement,
        WindowWorkArea workArea)
    {
        if (!IsValid(placement) || !workArea.IsUsable)
        {
            return null;
        }

        var minimumWidth = Math.Min(MinimumWidth, workArea.Width);
        var minimumHeight = Math.Min(MinimumHeight, workArea.Height);
        var width = Math.Clamp(placement.Width, minimumWidth, workArea.Width);
        var height = Math.Clamp(placement.Height, minimumHeight, workArea.Height);
        var maximumX = (long)workArea.X + workArea.Width - width;
        var maximumY = (long)workArea.Y + workArea.Height - height;
        var x = (int)Math.Clamp((long)placement.X, workArea.X, maximumX);
        var y = (int)Math.Clamp((long)placement.Y, workArea.Y, maximumY);

        return new WindowPlacementSnapshot(x, y, width, height);
    }
}
