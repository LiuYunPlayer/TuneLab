namespace TuneLab.GUI;

internal interface IScrollView
{
    IScrollAxis HorizontalAxis { get; }
    IScrollAxis VerticalAxis { get; }
}
