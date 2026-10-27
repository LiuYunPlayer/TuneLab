using System;
using System.Drawing;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 画布绘图的样式与形状对象（CanvasPen / CanvasTextStyle / CanvasGeometry）：不可变、构造时校验。
// 宿主把转成的原生对象缓存在它们身上，所以"不可变"是正确性前提——With 必须给新对象、原对象不变。
public class CanvasDrawingStyleTests
{
    [Fact]
    public void Pen_RejectsNonPositiveOrInfiniteWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasPen.Create(Color.White, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasPen.Create(Color.White, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasPen.Create(Color.White, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasPen.Create(Color.White).WithWidth(-1));
    }

    [Fact]
    public void Pen_WithReturnsACopyAndLeavesTheOriginal()
    {
        var pen = CanvasPen.Create(Color.White, 2);
        var miter = pen.WithLineJoin(CanvasLineJoin.Miter);

        Assert.NotSame(pen, miter);
        Assert.Equal(CanvasLineJoin.Round, pen.LineJoin);   // 默认圆头圆角，原对象不变
        Assert.Equal(CanvasLineCap.Round, pen.LineCap);
        Assert.Equal(CanvasLineJoin.Miter, miter.LineJoin);
        Assert.Equal(2, miter.Width);
        Assert.Equal(Color.White.ToArgb(), miter.Color.ToArgb());
    }

    [Fact]
    public void TextStyle_DefaultsToHostFontSizeAndCenter()
    {
        // 字号不给 = null（宿主决定），不是 SDK 里写死的某个数：宿主默认字号以后可以变。
        var style = CanvasTextStyle.Create();
        Assert.Null(style.FontSize);
        Assert.Equal(CanvasAlignment.Center, style.Alignment);

        Assert.Throws<ArgumentOutOfRangeException>(() => style.WithFontSize(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => style.WithFontSize(double.NaN));

        var label = style.WithFontSize(11).WithAlignment(CanvasAlignment.RightTop);
        Assert.Null(style.FontSize);   // 原对象不变
        Assert.Equal(11, label.FontSize);
        Assert.Equal(CanvasAlignment.RightTop, label.Alignment);
    }

    [Fact]
    public void Geometry_RejectsNullPoints()
    {
        Assert.Throws<ArgumentNullException>(() => CanvasGeometry.Polyline(null!));
        Assert.Throws<ArgumentNullException>(() => CanvasGeometry.Polygon(null!));
    }
}
