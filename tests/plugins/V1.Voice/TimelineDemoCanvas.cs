using System;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Voice;

// 时间轴画布（TimelineCanvasConfig）的演示：只在参数面板里、每个对象一块。眼下只标出这一块覆盖的时间段
//（XToTime(0) 到 XToTime(Width)），用来核对宿主给的时间换算。
sealed class TimelineDemoCanvas(ITimelineCanvasHost host, string label) : ICanvasControl
{
    public static TimelineCanvasConfig Config(string label) => TimelineCanvasConfig.Create(Default, host => new TimelineDemoCanvas(host, label));

    public static readonly PropertyValue Default = PropertyObject.Empty;

    public void Render(ICanvasDrawing d)
    {
        double w = host.Width, h = host.Height;
        d.StrokeRectangle(new Rect(0.5, 0.5, Math.Max(0, w - 1), Math.Max(0, h - 1)), Frame);
        var axis = host.TimeAxis;
        d.DrawText($"{label} {axis.XToTime(0):0.000}–{axis.XToTime(w):0.000}s", new Rect(4, 4, 0, 0), LabelBrush, LabelStyle);
    }

    static readonly CanvasPen Frame = CanvasPen.Create(Color.FromArgb(120, 255, 255, 255));
    static readonly CanvasBrush LabelBrush = CanvasBrush.Solid(Color.FromArgb(200, 255, 255, 255));
    static readonly CanvasTextStyle LabelStyle = CanvasTextStyle.Create().WithFontSize(11).WithAlignment(CanvasAlignment.LeftTop);
}
