using System;
using System.Collections.Generic;
using Point = TuneLab.Foundation.Point;

namespace TuneLab.SDK;

// ICanvasDrawing 的常用写法：都只是现建一个 CanvasGeometry 交给 DrawGeometry / PushClip，不是新能力，故不进接口
// （接口只放原语，宿主实现也就不必各写一份）。命名：Draw* = 按给的 fill / stroke 画（可只给一个），Fill* 只填充，Stroke* 只描边。
// 同一个形状每帧都画时，也可以自己建好 CanvasGeometry 存起来直接调 DrawGeometry。
public static class CanvasDrawingExtensions
{
    public static void DrawPolyline(this ICanvasDrawing drawing, IReadOnlyList<Point> points, CanvasPen pen)
        => drawing.DrawGeometry(CanvasGeometry.Polyline(points), null, pen);

    public static void DrawPolygon(this ICanvasDrawing drawing, IReadOnlyList<Point> points, CanvasBrush? fill, CanvasPen? stroke)
        => drawing.DrawGeometry(CanvasGeometry.Polygon(points), fill, stroke);
    public static void FillPolygon(this ICanvasDrawing drawing, IReadOnlyList<Point> points, CanvasBrush brush)
        => drawing.DrawGeometry(CanvasGeometry.Polygon(points), brush, null);
    public static void StrokePolygon(this ICanvasDrawing drawing, IReadOnlyList<Point> points, CanvasPen pen)
        => drawing.DrawGeometry(CanvasGeometry.Polygon(points), null, pen);

    public static void DrawRectangle(this ICanvasDrawing drawing, CanvasRect rect, CanvasBrush? fill, CanvasPen? stroke, double cornerRadius = 0)
        => drawing.DrawGeometry(CanvasGeometry.Rectangle(rect, cornerRadius), fill, stroke);
    public static void FillRectangle(this ICanvasDrawing drawing, CanvasRect rect, CanvasBrush brush, double cornerRadius = 0)
        => drawing.DrawGeometry(CanvasGeometry.Rectangle(rect, cornerRadius), brush, null);
    public static void StrokeRectangle(this ICanvasDrawing drawing, CanvasRect rect, CanvasPen pen, double cornerRadius = 0)
        => drawing.DrawGeometry(CanvasGeometry.Rectangle(rect, cornerRadius), null, pen);

    public static void DrawEllipse(this ICanvasDrawing drawing, Point center, double radiusX, double radiusY, CanvasBrush? fill, CanvasPen? stroke)
        => drawing.DrawGeometry(CanvasGeometry.Ellipse(center, radiusX, radiusY), fill, stroke);
    public static void FillEllipse(this ICanvasDrawing drawing, Point center, double radiusX, double radiusY, CanvasBrush brush)
        => drawing.DrawGeometry(CanvasGeometry.Ellipse(center, radiusX, radiusY), brush, null);
    public static void StrokeEllipse(this ICanvasDrawing drawing, Point center, double radiusX, double radiusY, CanvasPen pen)
        => drawing.DrawGeometry(CanvasGeometry.Ellipse(center, radiusX, radiusY), null, pen);

    // 矩形裁剪（可圆角）。
    public static IDisposable PushClip(this ICanvasDrawing drawing, CanvasRect rect, double cornerRadius = 0)
        => drawing.PushClip(CanvasGeometry.Rectangle(rect, cornerRadius));
}
