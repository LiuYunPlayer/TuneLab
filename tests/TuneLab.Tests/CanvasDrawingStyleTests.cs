using System;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 画布绘图的样式与形状对象（CanvasPen / CanvasTextStyle / CanvasGeometry）与它们用到的 Foundation 值类型（Color / Rect）：
// 不可变、构造时校验。宿主把转成的原生对象缓存在样式与形状对象上，所以"不可变"是正确性前提——With 必须给新对象、原对象不变。
public class CanvasDrawingStyleTests
{
    static readonly Color White = Color.FromRgb(255, 255, 255);

    [Fact]
    public void Pen_RejectsNonPositiveOrInfiniteWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasPen.Create(White, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasPen.Create(White, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasPen.Create(White, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasPen.Create(White).WithWidth(-1));
    }

    [Fact]
    public void Pen_WithReturnsACopyAndLeavesTheOriginal()
    {
        var pen = CanvasPen.Create(White, 2);
        var miter = pen.WithLineJoin(CanvasLineJoin.Miter);

        Assert.NotSame(pen, miter);
        Assert.Equal(CanvasLineJoin.Round, pen.LineJoin);   // 默认圆头圆角，原对象不变
        Assert.Equal(CanvasLineCap.Round, pen.LineCap);
        Assert.Equal(CanvasLineJoin.Miter, miter.LineJoin);
        Assert.Equal(2, miter.Width);
        Assert.Equal(White, miter.Color);
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

    // 十六进制与 SDK 别处（AutomationConfig.WithColor 等）同一种写法：#RRGGBB 不透明、#AARRGGBB 带透明度，# 可省、大小写不限。
    [Fact]
    public void Color_ParsesHexInBothForms()
    {
        Assert.Equal(Color.FromArgb(255, 0x73, 0xC2, 0xE5), Color.FromHex("#73C2E5"));
        Assert.Equal(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), Color.FromHex("80ffffff"));   // Alpha 在前，同工程文件里的 #AARRGGBB
        Assert.Throws<ArgumentException>(() => Color.FromHex("#FFF"));
        Assert.Throws<ArgumentException>(() => Color.FromHex("#GG0000"));
    }

    // 相等只看四个分量（没有"颜色名"之类的附带状态）；WithAlpha 给新值。
    [Fact]
    public void Color_EqualityIsComponentWise()
    {
        var blue = Color.FromRgb(0x73, 0xC2, 0xE5);
        Assert.Equal(blue, Color.FromArgb(255, 0x73, 0xC2, 0xE5));
        Assert.Equal(Color.FromArgb(40, 0x73, 0xC2, 0xE5), blue.WithAlpha(40));
        Assert.Equal(0, default(Color).A);   // 默认值 = 完全透明
        Assert.Equal(255, blue.A);
    }

    [Fact]
    public void Rect_EdgesFollowPositionAndSize()
    {
        var rect = new Rect(10, 20, 30, 40);
        Assert.Equal(10, rect.Left);
        Assert.Equal(20, rect.Top);
        Assert.Equal(40, rect.Right);
        Assert.Equal(60, rect.Bottom);
        Assert.Equal(new Point(25, 40), rect.Center);
        Assert.Equal(rect, Rect.FromLTRB(10, 20, 40, 60));
    }

    // 含边；向内缩用负数，缩过头即空矩形（宽高为负），不含任何点。
    [Fact]
    public void Rect_ContainsAndInflate()
    {
        var rect = new Rect(0, 0, 10, 10);
        Assert.True(rect.Contains(new Point(10, 0)));
        Assert.False(rect.Contains(new Point(10.5, 5)));

        Assert.Equal(new Rect(2, 2, 6, 6), rect.Inflate(-2, -2));
        var empty = rect.Inflate(-6, -6);
        Assert.True(empty.Width < 0);
        Assert.False(empty.Contains(new Point(5, 5)));
    }
}
