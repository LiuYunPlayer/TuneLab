using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;
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

    protected override void OnDraw(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, this.Rect());

        if (Direction.IsHorizontal())
            context.FillRectangle(Style.BACK.ToBrush(), new(0, (Bounds.Height - 4) / 2, Bounds.Width, 4), 2);
        else
            context.FillRectangle(Style.BACK.ToBrush(), new((Bounds.Width - 4) / 2, 0, 4, Bounds.Height), 2);
    }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        return new(ThumbRadius * 2, ThumbRadius * 2);
    }

    protected override Avalonia.Point StartPoint => Direction switch
    {
        SliderDirection.LeftToRight => new(ThumbRadius, Bounds.Height / 2),
        SliderDirection.BottomToTop => new(Bounds.Width / 2, Bounds.Height - ThumbRadius),
        SliderDirection.RightToLeft => new(Bounds.Width - ThumbRadius, Bounds.Height / 2),
        SliderDirection.TopToBottom => new(Bounds.Width / 2, ThumbRadius),
        _ => new()
    };

    protected override Avalonia.Point EndPoint => Direction switch
    {
        SliderDirection.LeftToRight => new(Bounds.Width - ThumbRadius, Bounds.Height / 2),
        SliderDirection.BottomToTop => new(Bounds.Width / 2, ThumbRadius),
        SliderDirection.RightToLeft => new(ThumbRadius, Bounds.Height / 2),
        SliderDirection.TopToBottom => new(Bounds.Width / 2, Bounds.Height - ThumbRadius),
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
