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

internal class CornerBorderItem : IItem
{
    public CornerRadius CornerRadius { get; set; } = new CornerRadius(4);

    public void Paint(DrawingContext context, Rect rect, Color color)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            double tl = CornerRadius.TopLeft;
            double tr = CornerRadius.TopRight;
            double br = CornerRadius.BottomRight;
            double bl = CornerRadius.BottomLeft;

            ctx.BeginFigure(new Point(rect.Left + tl, rect.Top), true);
            ctx.LineTo(new Point(rect.Right - tr, rect.Top));
            if (tr > 0)
                ctx.ArcTo(new Point(rect.Right, rect.Top + tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise);
            else
                ctx.LineTo(new Point(rect.Right, rect.Top));
            ctx.LineTo(new Point(rect.Right, rect.Bottom - br));
            if (br > 0)
                ctx.ArcTo(new Point(rect.Right - br, rect.Bottom), new Size(br, br), 0, false, SweepDirection.Clockwise);
            else
                ctx.LineTo(new Point(rect.Right, rect.Bottom));
            ctx.LineTo(new Point(rect.Left + bl, rect.Bottom));
            if (bl > 0)
                ctx.ArcTo(new Point(rect.Left, rect.Bottom - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise);
            else
                ctx.LineTo(new Point(rect.Left, rect.Bottom));
            ctx.LineTo(new Point(rect.Left, rect.Top + tl));
            if (tl > 0)
                ctx.ArcTo(new Point(rect.Left + tl, rect.Top), new Size(tl, tl), 0, false, SweepDirection.Clockwise);
            else
                ctx.LineTo(new Point(rect.Left, rect.Top));
            ctx.EndFigure(true);
        }
        context.DrawGeometry(color.ToBrush(), null, geometry);
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
    public double Scale { get; set; } = 1;

    public void Paint(DrawingContext context, Rect rect, Color color)
    {
        if (Icon == null)
            return;

        var image = Icon.GetImage(color);
        var size = image.Size;
        size *= Scale;
        var anchor = Alignment.Offset(rect.Width, rect.Height);
        var pivot = Alignment.Offset(size.Width, size.Height);
        context.DrawImage(Icon.GetImage(color), new Rect(Offset.X - anchor.Item1 + pivot.Item1, Offset.Y - anchor.Item2 + pivot.Item2, size.Width, size.Height));
    }
}