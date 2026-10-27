using System;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 画笔：线条与轮廓怎么画（颜色、粗细、端点、转角）。不可变，构造函数全封，只走静态工厂 + With（与 config 同款 ABI 理由：
// 以后加虚线等新维度只是加一个 With，不动任何绘制方法的签名）。可以建好存进静态字段反复用。
public sealed class CanvasPen
{
    public Color Color { get; private set; }

    // 粗细（逻辑像素）。
    public double Width { get; private set; }

    // 默认圆头、圆角（同宿主自己画曲线）：粗线不出尖角。要锐利的矩形角就 WithLineJoin(Miter)。
    public CanvasLineCap LineCap { get; private set; } = CanvasLineCap.Round;
    public CanvasLineJoin LineJoin { get; private set; } = CanvasLineJoin.Round;

    private CanvasPen() { }
    // 副本不带宿主缓存：缓存对应的是原来那支笔的样子。
    CanvasPen Clone() { var c = (CanvasPen)MemberwiseClone(); c.Native = null; return c; }

    public static CanvasPen Create(Color color, double width = 1) => new() { Color = color, Width = CheckWidth(width) };

    public CanvasPen WithColor(Color color) { var c = Clone(); c.Color = color; return c; }
    public CanvasPen WithWidth(double width) { var c = Clone(); c.Width = CheckWidth(width); return c; }
    public CanvasPen WithLineCap(CanvasLineCap lineCap) { var c = Clone(); c.LineCap = lineCap; return c; }
    public CanvasPen WithLineJoin(CanvasLineJoin lineJoin) { var c = Clone(); c.LineJoin = lineJoin; return c; }

    static double CheckWidth(double width)
        => width > 0 && double.IsFinite(width) ? width
            : throw new ArgumentOutOfRangeException(nameof(width), width, "Pen width must be a positive finite number.");

    // 宿主侧：转成的原生画笔缓存在这里（只在主线程上读写）。
    internal object? Native { get; set; }
}

// 线的两端。值是冻结契约（编译进插件），故显式写出；加成员是加性：宿主对未知值按 Round 处理。
public enum CanvasLineCap
{
    Flat = 0,     // 齐头：止于端点
    Round = 1,    // 圆头：端点外加半个线宽的半圆
    Square = 2,   // 方头：端点外加半个线宽
}

// 折线 / 轮廓的转角。同上：未知值按 Round。
public enum CanvasLineJoin
{
    Miter = 0,
    Round = 1,
    Bevel = 2,
}
