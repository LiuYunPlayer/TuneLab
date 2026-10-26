using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Point = TuneLab.Foundation.Point;

namespace TuneLab.SDK;

// 画布的绘图接口（宿主实现，只在 ICanvasControl.Render 期间有效）。命名约定：Draw = 线条 / 轮廓，Fill = 填充；
// 既要填充又要描边就调两次。坐标是画布内的逻辑像素（与 DPI 无关），宿主把绘制裁剪在画布区域内。
// 以后需要路径、渐变、虚线等再加方法（宿主实现面，加方法不影响已有插件）。
public interface ICanvasDrawing
{
    void DrawLine(Point from, Point to, Color color, double width = 1);
    void DrawPolyline(IReadOnlyList<Point> points, Color color, double width = 1);

    // 多边形：首尾自动闭合。三角形等都用它。
    void DrawPolygon(IReadOnlyList<Point> points, Color color, double width = 1);
    void FillPolygon(IReadOnlyList<Point> points, Color color);

    void DrawRectangle(double x, double y, double width, double height, Color color, double lineWidth = 1, double cornerRadius = 0);
    void FillRectangle(double x, double y, double width, double height, Color color, double cornerRadius = 0);

    // 椭圆：圆即 radiusX == radiusY。
    void DrawEllipse(Point center, double radiusX, double radiusY, Color color, double width = 1);
    void FillEllipse(Point center, double radiusX, double radiusY, Color color);

    // 文字：按 alignment 对齐在矩形内。矩形宽高为 0 时即"以 (x, y) 为锚点"——如 RightCenter 表示文字右端中点落在该点。
    // 文字里的 \n 换行，多行时每一行也按 alignment 的横向对齐（如 Center = 每行各自居中）。不自动折行、超出不截断。字体由宿主决定（跟随界面字体）：按 MeasureText 的结果排版，别写死文字宽度。
    void DrawText(string text, double x, double y, double width, double height, Color color, double fontSize, CanvasAlignment alignment = CanvasAlignment.Center);
    (double Width, double Height) MeasureText(string text, double fontSize);

    // 图片：缩放画进给定矩形（不保持宽高比——要保持就按 CanvasImage 的 Width / Height 自己算矩形）。
    // SVG 按矩形尺寸与 ICanvasHost.DevicePixelRatio 清晰绘制；位图按像素缩放。已 Dispose 的图片抛 ObjectDisposedException。
    void DrawImage(CanvasImage image, double x, double y, double width, double height);

    // 绘制状态：Push* 之后的绘制都受它影响，直到 Dispose 返回值（用 using）。按栈嵌套、后进先出；
    // 先 Dispose 外层会连同其内层一起撤销。Render 结束时没撤销的宿主一并撤销。
    // · PushOpacity：不透明度（0..1，与外层相乘）；
    // · PushClip：只画矩形内的部分（可圆角，与外层取交集）；
    // · PushTransform：坐标变换（与外层复合）。只影响绘制——鼠标事件的坐标永远是画布坐标，不随变换。
    IDisposable PushOpacity(double opacity);
    IDisposable PushClip(double x, double y, double width, double height, double cornerRadius = 0);
    IDisposable PushTransform(Matrix3x2 transform);
}

// 九宫格对齐。值是冻结契约（编译进插件），故显式写出：低 2 位 = 水平（0 左 / 1 中 / 2 右），第 2–3 位 = 垂直
// （0 上 / 4 中 / 8 下）。插件只按名字使用、别对它做位运算（不是 Flags）。加成员是加性：宿主对未知值按 Center 处理。
public enum CanvasAlignment
{
    LeftTop = 0,
    CenterTop = 1,
    RightTop = 2,
    LeftCenter = 4,
    Center = 5,
    RightCenter = 6,
    LeftBottom = 8,
    CenterBottom = 9,
    RightBottom = 10,
}
