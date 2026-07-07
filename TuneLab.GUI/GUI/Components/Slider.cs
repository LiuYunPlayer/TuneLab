using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
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

        // 背景轨道画在 thumb 实际行程范围内（StartPoint↔EndPoint，两端已内缩 ThumbRadius），
        // 而非整个控件宽/高——否则量程两端到控件边缘的空段也会染色，与可滑动区域不符。
        var start = StartPoint;
        var end = EndPoint;
        if (Direction.IsHorizontal())
        {
            double left = Math.Min(start.X, end.X);
            context.FillRectangle(Style.BACK.ToBrush(), new(left, (Bounds.Height - 4) / 2, Math.Abs(end.X - start.X), 4), 2);
        }
        else
        {
            double top = Math.Min(start.Y, end.Y);
            context.FillRectangle(Style.BACK.ToBrush(), new((Bounds.Width - 4) / 2, top, 4, Math.Abs(end.Y - start.Y)), 2);
        }

        DrawDefaultToValueRange(context);
    }

    // 在轨道上画出「当前值 ↔ 默认值」的区间：两端点沿 thumb 行程轴取（与 thumb 中心对齐），
    // 方向由 StartPoint/EndPoint 已编码，四个方向通用。当前值为 NaN（多选/空）时不画。
    void DrawDefaultToValueRange(DrawingContext context)
    {
        if (double.IsNaN(Value))
            return;

        var start = StartPoint;
        var end = EndPoint;
        double tCur = Scale.ToNormalized(Value).Limit(0, 1);
        double tDef = Scale.ToNormalized(DefaultValue).Limit(0, 1);

        var brush = Style.HIGH_LIGHT.ToBrush();
        if (Direction.IsHorizontal())
        {
            double x1 = start.X + (end.X - start.X) * tCur;
            double x2 = start.X + (end.X - start.X) * tDef;
            context.FillRectangle(brush, new(Math.Min(x1, x2), (Bounds.Height - 4) / 2, Math.Abs(x1 - x2), 4), 2);
        }
        else
        {
            double y1 = start.Y + (end.Y - start.Y) * tCur;
            double y2 = start.Y + (end.Y - start.Y) * tDef;
            context.FillRectangle(brush, new((Bounds.Width - 4) / 2, Math.Min(y1, y2), 4, Math.Abs(y1 - y2)), 2);
        }
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
