using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.Primitives.DataStructures;
using TuneLab.GUI.Components;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

internal enum SliderDirection
{
    LeftToRight,
    BottomToTop,
    RightToLeft,
    TopToBottom
}

internal static class SliderDirectionExtension
{
    public static bool IsHorizontal(this SliderDirection direction)
    {
        return direction == SliderDirection.LeftToRight || direction == SliderDirection.RightToLeft;
    }

    public static bool IsVertical(this SliderDirection direction)
    {
        return direction == SliderDirection.BottomToTop || direction == SliderDirection.TopToBottom;
    }
}

internal class Slider : AbstractSlider
{
    public SliderDirection Direction { get => mDirection; set { mDirection = value; InvalidateVisual(); } }

    public Slider()
    {
        Thumb = new SliderThumb(this);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, this.Rect());

        if (Direction.IsHorizontal())
            context.FillRectangle(Style.BACK.ToBrush(), new(0, (Bounds.Height - 4) / 2, Bounds.Width, 4), 2);
        else
            context.FillRectangle(Style.BACK.ToBrush(), new((Bounds.Width - 4) / 2, 0, 4, Bounds.Height), 2);
    }

    protected override Avalonia.Point GetStartPoint(Avalonia.Size size) => Direction switch
    {
        SliderDirection.LeftToRight => new(ThumbRadius, size.Height / 2),
        SliderDirection.BottomToTop => new(size.Width / 2, size.Height - ThumbRadius),
        SliderDirection.RightToLeft => new(size.Width - ThumbRadius, size.Height / 2),
        SliderDirection.TopToBottom => new(size.Width / 2, ThumbRadius),
        _ => new()
    };

    protected override Avalonia.Point GetEndPoint(Avalonia.Size size) => Direction switch
    {
        SliderDirection.LeftToRight => new(size.Width - ThumbRadius, size.Height / 2),
        SliderDirection.BottomToTop => new(size.Width / 2, ThumbRadius),
        SliderDirection.RightToLeft => new(ThumbRadius, size.Height / 2),
        SliderDirection.TopToBottom => new(size.Width / 2, size.Height - ThumbRadius),
        _ => new()
    };

    protected class SliderThumb : AbstractThumb
    {
        public SliderThumb(AbstractSlider slider) : base(slider)
        {
            Width = ThumbRadius * 2;
            Height = ThumbRadius * 2;
        }

        public override void Render(DrawingContext context)
        {
            context.DrawEllipse(Style.LIGHT_WHITE.ToBrush(), null, this.Rect());
        }
    }

    SliderDirection mDirection = SliderDirection.LeftToRight;
    static readonly double ThumbRadius = 6;
}
