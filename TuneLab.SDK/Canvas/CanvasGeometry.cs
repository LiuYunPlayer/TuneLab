using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 形状：画（ICanvasDrawing.DrawGeometry，填充 / 描边）与裁剪（PushClip）都收它，所以一种新形状只需一个新工厂。
// 不可变（点列建时拷一份，之后改原列表不影响它），只走静态工厂；固定不变的形状可以建好存进静态字段反复用。
// 填充与裁剪按"围出来的区域"算：不闭合的折线也按首尾相连围出的区域填充 / 裁剪，描边则只画折线本身。
// 退化的形状（点不够、宽高或半径为负）画出来什么都没有，拿来裁剪则什么都不留。
public sealed class CanvasGeometry
{
    private CanvasGeometry() { }

    // 折线（不闭合）与多边形（首尾自动闭合，三角形等都用它）。
    public static CanvasGeometry Polyline(IReadOnlyList<Point> points) => FromPoints(points, ShapeKind.Polyline);
    public static CanvasGeometry Polygon(IReadOnlyList<Point> points) => FromPoints(points, ShapeKind.Polygon);

    // 矩形，可圆角（半径不大于 0 = 直角）。
    public static CanvasGeometry Rectangle(Rect rect, double cornerRadius = 0)
        => new() { Kind = ShapeKind.Rectangle, Rect = rect, CornerRadius = cornerRadius > 0 ? cornerRadius : 0 };

    // 椭圆：圆即 radiusX == radiusY。
    public static CanvasGeometry Ellipse(Point center, double radiusX, double radiusY)
        => new() { Kind = ShapeKind.Ellipse, Center = center, RadiusX = radiusX, RadiusY = radiusY };

    // 圆 = 两个半径相等的椭圆（画控制点、把手最常用）。
    public static CanvasGeometry Circle(Point center, double radius) => Ellipse(center, radius, radius);

    static CanvasGeometry FromPoints(IReadOnlyList<Point> points, ShapeKind kind)
    {
        ArgumentNullException.ThrowIfNull(points);
        return new() { Kind = kind, Points = points.ToArray() };
    }

    // —— 宿主侧（插件只造不读，经 InternalsVisibleTo 给宿主）——

    internal enum ShapeKind { Polyline, Polygon, Rectangle, Ellipse }

    internal ShapeKind Kind { get; private init; }
    internal IReadOnlyList<Point> Points { get; private init; } = [];
    internal Rect Rect { get; private init; }
    internal double CornerRadius { get; private init; }
    internal Point Center { get; private init; }
    internal double RadiusX { get; private init; }
    internal double RadiusY { get; private init; }

    // 转成的原生形状缓存（只在主线程上读写）。
    internal object? Native { get; set; }
}
