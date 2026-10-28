using System;
using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.DocsPlugins.DemoVoice;

// 文档示例的时间轴画布（TimelineCanvasConfig）「呼吸」：每个音符一块，画一条随时间变化的呼吸强度曲线（只展示，不编辑）。
// 值 = { "curve": [[秒, 值], …] }：秒是距这块画布起点（XToTime(0)）的秒数，值 0..1；默认 {} = 没有曲线（中性）。
// 画法：逐像素采样——每列 XToTime(x) 换回秒、再按秒插值，跨过速度变化也与按秒插值的合成一致；只算露出来的列。
sealed class BreathCanvas(ITimelineCanvasHost host, PropertyValue value) : ICanvasControl
{
    public static readonly PropertyValue Default = PropertyObject.Empty;

    public static TimelineCanvasConfig Config(PropertyValue value)
        => TimelineCanvasConfig.Create(Default, host => new BreathCanvas(host, value));

    static readonly CanvasPen Line = CanvasPen.Create(Color.FromHex("#7FD1C0"), 2);
    static readonly CanvasBrush Fill = CanvasBrush.Solid(Color.FromHex("#337FD1C0"));
    static readonly CanvasPen Edge = CanvasPen.Create(Color.FromArgb(60, 255, 255, 255));
    const double Pad = 6;

    public void Render(ICanvasDrawing d)
    {
        double w = host.Width, h = host.Height;
        d.DrawLine(new Point(0.5, 0), new Point(0.5, h), Edge);   // 音符起点处一条细线，分出相邻两块
        var points = Read(value);
        if (points.Count == 0)
            return;

        var visible = host.VisibleBounds;
        int from = (int)Math.Floor(Math.Max(0, visible.Left)), to = (int)Math.Ceiling(Math.Min(w, visible.Right));
        double start = host.TimeAxis.XToTime(0);
        var line = new List<Point>();
        for (int x = from; x <= to; x++)
            line.Add(new Point(x, Pad + (h - 2 * Pad) * (1 - Evaluate(points, host.TimeAxis.XToTime(x) - start))));
        if (line.Count < 2)
            return;
        var area = new List<Point>(line) { new(line[^1].X, h), new(line[0].X, h) };
        d.FillPolygon(area, Fill);
        d.DrawPolyline(line, Line);
    }

    static List<(double Time, double Value)> Read(PropertyValue value)
    {
        var points = new List<(double, double)>();
        if (value.ToObject(out var obj) && obj.Map.TryGetValue("curve", out var curve) && curve.ToArray(out var array))
        {
            foreach (var item in array)
            {
                if (item.ToArray(out var pair) && pair.Count == 2 && pair[0].ToDouble(out var t) && pair[1].ToDouble(out var v))
                    points.Add((t, Math.Clamp(v, 0, 1)));
            }
        }
        points.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return points;
    }

    static double Evaluate(List<(double Time, double Value)> points, double offset)
    {
        if (offset <= points[0].Time)
            return points[0].Value;
        for (int i = 1; i < points.Count; i++)
        {
            if (offset <= points[i].Time)
            {
                var (t0, v0) = points[i - 1];
                var (t1, v1) = points[i];
                return t1 > t0 ? v0 + (v1 - v0) * (offset - t0) / (t1 - t0) : v1;
            }
        }
        return points[^1].Value;
    }
}
