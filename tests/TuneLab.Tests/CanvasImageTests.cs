using System;
using System.Text;
using SkiaSharp;
using TuneLab.GUI.Components;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 画布图片（CanvasImage）的封条：公开工厂的参数校验，与宿主解码实现（CanvasImageFactory）认得的格式、自然尺寸、
// 不可识别数据的报错。解码只用 SkiaSharp、不需要窗口；绘制（转 Avalonia 位图、SVG 栅格化）需要渲染器，不在此测。
public class CanvasImageTests
{
    static readonly CanvasImageFactory Factory = new();

    [Fact]
    public void FromPixelData_ValidatesArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasImage.FromPixelData(0, 1, new byte[4]));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasImage.FromPixelData(1, -1, new byte[4]));
        Assert.Throws<ArgumentException>(() => CanvasImage.FromPixelData(2, 2, new byte[15]));
        Assert.Throws<ArgumentException>(() => CanvasImage.FromPixelData(2, 2, new byte[17]));
    }

    [Fact]
    public void FromEncodedData_RejectsEmptyData()
    {
        Assert.Throws<ArgumentException>(() => CanvasImage.FromEncodedData(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Factory_FromPixels_HasPixelSize()
    {
        using var content = Factory.FromPixels(3, 2, new byte[3 * 2 * 4]);
        Assert.Equal(3, content.Width);
        Assert.Equal(2, content.Height);
    }

    [Fact]
    public void Factory_DecodesPng()
    {
        using var bitmap = new SKBitmap(5, 4);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var content = Factory.Decode(data.AsSpan());
        Assert.Equal(5, content.Width);
        Assert.Equal(4, content.Height);
    }

    const string Svg = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='30'><rect width='40' height='30' fill='red'/></svg>";

    // 认 SVG 的判据：跳过 BOM 与空白后以 '<' 开头（XML 声明本身不许前置空白，故 BOM 后直接接声明）。
    [Theory]
    [InlineData("")]
    [InlineData("\r\n  ")]
    [InlineData("\uFEFF<?xml version='1.0'?>\r\n")]
    public void Factory_DecodesSvg_WithItsDeclaredSize(string prefix)
    {
        using var content = Factory.Decode(Encoding.UTF8.GetBytes(prefix + Svg));
        Assert.Equal(40, content.Width);
        Assert.Equal(30, content.Height);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4, 5 })]
    [InlineData(new byte[] { (byte)'<', (byte)'x' })]
    public void Factory_RejectsUnrecognizedData(byte[] data)
    {
        Assert.Throws<ArgumentException>(() => Factory.Decode(data));
    }

    [Fact]
    public void Factory_Content_DisposeTwice_IsHarmless()
    {
        var content = Factory.FromPixels(1, 1, new byte[4]);
        content.Dispose();
        content.Dispose();
    }
}
