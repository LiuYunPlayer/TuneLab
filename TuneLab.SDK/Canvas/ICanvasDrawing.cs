using System;
using System.Numerics;
using Point = TuneLab.Foundation.Point;

namespace TuneLab.SDK;

// 画布的绘图接口（宿主实现，只在 ICanvasControl.Render 期间有效）。坐标是画布内的逻辑像素（与 DPI 无关），宿主把绘制
// 裁剪在画布区域内。参数一律"画什么、在哪 → 用什么画"；怎么画（颜色、粗细、端点、字号、对齐……）都装在 CanvasPen /
// CanvasBrush / CanvasTextStyle 里，以后加新的样式维度是给它们加 With，不动这里的签名。
// 接口只放原语：矩形、椭圆、折线等常用写法见 CanvasDrawingExtensions（Draw* = 按给的填充 + 描边画，Fill* 只填充，
// Stroke* 只描边）。
public interface ICanvasDrawing
{
    void DrawLine(Point from, Point to, CanvasPen pen);

    // 形状：fill / stroke 可只给一个（另一个传 null），两个都给就先填充再描边。
    void DrawGeometry(CanvasGeometry geometry, CanvasBrush? fill, CanvasPen? stroke);

    // 文字：按 style 的对齐排在矩形内（style 不传 = 默认样式：字号跟随宿主、居中）。矩形宽高为 0 时即"以该点为锚"。
    // 文字里的 \n 换行，多行时每一行也按横向对齐（如 Center = 每行各自居中）。不自动折行、超出不截断。字体由宿主决定
    // （跟随界面字体）：按 MeasureText 的结果排版，别写死文字宽度。
    void DrawText(string text, CanvasRect bounds, CanvasBrush brush, CanvasTextStyle? style = null);
    CanvasSize MeasureText(string text, CanvasTextStyle? style = null);

    // 图片：缩放画进给定矩形（不保持宽高比——要保持就按 CanvasImage 的 Width / Height 自己算矩形）。
    // SVG 按矩形尺寸与 ICanvasHost.DevicePixelRatio 清晰绘制；位图按像素缩放。已 Dispose 的图片抛 ObjectDisposedException。
    void DrawImage(CanvasImage image, CanvasRect bounds);

    // 绘制状态：Push* 之后的绘制都受它影响，直到 Dispose 返回值（用 using）。按栈嵌套、后进先出；
    // 先 Dispose 外层会连同其内层一起撤销。Render 结束时没撤销的宿主一并撤销。
    // · PushOpacity：不透明度（0..1，与外层相乘）；
    // · PushClip：只画形状围出的区域内的部分（与外层取交集；矩形裁剪见扩展方法 PushClip(rect, cornerRadius)）；
    // · PushTransform：坐标变换（与外层复合）。只影响绘制——鼠标事件的坐标永远是画布坐标，不随变换。
    IDisposable PushOpacity(double opacity);
    IDisposable PushClip(CanvasGeometry geometry);
    IDisposable PushTransform(Matrix3x2 transform);
}
