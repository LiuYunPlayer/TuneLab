using Avalonia.Media;

namespace TuneLab.GUI;

internal struct ColorSet
{
    public Color Color { get; set; }
    public Color? HoveredColor { get; set; }
    public Color? PressedColor { get; set; }
}
