using Avalonia.Media;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI.Components;
using TuneLab.GUI;
using TuneLab.Utils;

namespace TuneLab.GUI;

internal interface IItem
{
    void Paint(DrawingContext context, Rect rect, Color color);
}

internal class BorderItem : IItem
{
    public double CornerRadius { get; set; } = 4;

    public void Paint(DrawingContext context, Rect rect, Color color)
    {
        context.DrawRectangle(color.ToBrush(), null, rect, CornerRadius, CornerRadius);
    }
}

internal class TextItem : IItem
{
    public double FontSize { get; set; } = 12;
    public string Text { get; set; } = string.Empty;
    public Point Offset { get; set; } = new Point();
    public int PivotAlignment { get; set; } = GUI.Alignment.Center;
    public int Alignment { get; set; } = GUI.Alignment.Center;

    public void Paint(DrawingContext context, Rect rect, Color color)
    {
        context.DrawString(Text, rect, color.ToBrush(), FontSize, Alignment, PivotAlignment, Offset);
    }
}

internal class IconItem : IItem
{
    public SvgIcon? Icon { get; set; }
    public int PivotAlignment { get; set; } = GUI.Alignment.Center;
    public int Alignment { get; set; } = GUI.Alignment.Center;
    public Point Offset { get; set; } = new Point();

    public void Paint(DrawingContext context, Rect rect, Color color)
    {
        if (Icon == null)
            return;

        var image = Icon.GetImage(color);
        var size = image.Size;
        var anchor = Alignment.Offset(rect.Width, rect.Height);
        var pivot = Alignment.Offset(size.Width, size.Height);
        context.DrawImage(Icon.GetImage(color), new Rect(Offset.X - anchor.Item1 + pivot.Item1, Offset.Y - anchor.Item2 + pivot.Item2, size.Width, size.Height));
    }
}