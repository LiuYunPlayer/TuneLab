using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

// "正在执行"指示：一段高亮沿圆角矩形边框循环流动。叠在被指示的控件上方（不参与命中），只画边框——
// 不往控件里加任何内容物，故控件自身的布局与文字在执行前后完全不变。
// 动画按帧驱动（TopLevel.RequestAnimationFrame），只在 IsRunning 期间请求帧，空闲时零开销。
internal sealed class RunningBorder : Control
{
    // 与被覆盖控件的圆角一致（按钮的 BorderItem 是 6）。
    public double CornerRadius { get; set; } = 6;

    public bool IsRunning
    {
        get => mRunning;
        set
        {
            if (mRunning == value)
                return;
            mRunning = value;
            if (value)
            {
                mClock.Restart();
                RequestFrame();
            }
            InvalidateVisual();
        }
    }

    public RunningBorder()
    {
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        if (!mRunning)
            return;

        var rect = new Rect(Bounds.Size).Deflate(Thickness / 2);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        double radius = Math.Min(CornerRadius, Math.Min(rect.Width, rect.Height) / 2);
        double perimeter = 2 * (rect.Width + rect.Height - 4 * radius) + 2 * Math.PI * radius;

        // 虚线样式的单位是线宽：一段亮、其余空，平移虚线偏移量即让亮段沿边框走。
        double segment = perimeter * SegmentFraction;
        double phase = mClock.Elapsed.TotalMilliseconds / PeriodMilliseconds % 1.0;
        var dashes = new DashStyle(new[] { segment / Thickness, (perimeter - segment) / Thickness }, -phase * perimeter / Thickness);
        var pen = new Pen(Style.HIGH_LIGHT.ToBrush(), Thickness, dashes, PenLineCap.Round);
        context.DrawRectangle(null, pen, rect, radius, radius);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        mFrameRequested = false;   // 离开可视树后不再续帧；重新挂上时由下一次 IsRunning / 帧请求接续
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (mRunning)
            RequestFrame();
    }

    void RequestFrame()
    {
        if (mFrameRequested)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;   // 尚未挂上可视树：OnAttachedToVisualTree 时补请求
        mFrameRequested = true;
        topLevel.RequestAnimationFrame(_ =>
        {
            mFrameRequested = false;
            if (!mRunning)
                return;
            InvalidateVisual();
            RequestFrame();
        });
    }

    const double Thickness = 1.5;
    const double SegmentFraction = 0.25;      // 亮段占周长的比例
    const double PeriodMilliseconds = 1400;   // 绕一圈的时长

    readonly Stopwatch mClock = new();
    bool mRunning;
    bool mFrameRequested;
}
