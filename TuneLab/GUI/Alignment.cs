namespace TuneLab.GUI;

internal static class HorizontalAlignment
{
    public const int Left = 0;
    public const int Center = 1;
    public const int Right = 2;
}

internal static class VerticalAlignment
{
    public const int Top = 0;
    public const int Center = 4;
    public const int Bottom = 8;
}

internal static class Alignment
{
    public const int LeftTop = HorizontalAlignment.Left | VerticalAlignment.Top;
    public const int CenterTop = HorizontalAlignment.Center | VerticalAlignment.Top;
    public const int RightTop = HorizontalAlignment.Right | VerticalAlignment.Top;
    public const int LeftCenter = HorizontalAlignment.Left | VerticalAlignment.Center;
    public const int Center = HorizontalAlignment.Center | VerticalAlignment.Center;
    public const int RightCenter = HorizontalAlignment.Right | VerticalAlignment.Center;
    public const int LeftBottom = HorizontalAlignment.Left | VerticalAlignment.Bottom;
    public const int CenterBottom = HorizontalAlignment.Center | VerticalAlignment.Bottom;
    public const int RightBottom = HorizontalAlignment.Right | VerticalAlignment.Bottom;
    public static (double, double) Offset(this int alignment, double width, double height)
    {
        return (width / -2 * (alignment % 4), height / -2 * (alignment >> 2));
    }
}
