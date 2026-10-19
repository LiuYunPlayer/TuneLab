using System;

namespace TuneLab.SDK;

// 画布上画的图片（ICanvasDrawing.DrawImage）。由宿主解码与保管，插件只管创建、绘制、用完 Dispose。
// 创建后不可改写：要换内容就新建一张、把旧的 Dispose——先 Dispose 再新建，宿主更容易及时回收内存。
// 已经画出去的那一帧不受 Dispose 影响（宿主保证正在显示的画面不会因此出错），Dispose 之后不能再画（抛 ObjectDisposedException）。
// 可以在任意线程创建与 Dispose（如在后台线程解码），绘制只在 Render 里。
public sealed class CanvasImage : IDisposable
{
    // 从编码数据创建：PNG、JPEG、WebP、SVG。SVG 只按自身内容绘制，不加载外部引用（外部图片、外部文件里的元素）。
    // 数据无法识别或解码失败抛 ArgumentException。
    public static CanvasImage FromEncodedData(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            throw new ArgumentException("Image data is empty.", nameof(data));
        return new(RequireFactory().Decode(data));
    }

    // 从像素创建：每像素 4 字节，依次为 R、G、B、A（非预乘），逐行从上到下、行间无填充，长度须恰为 width × height × 4。
    public static CanvasImage FromPixelData(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        if (rgba.Length != (long)width * height * 4)
            throw new ArgumentException($"Expected {(long)width * height * 4} bytes of RGBA data for {width}x{height}, got {rgba.Length}.", nameof(rgba));
        return new(RequireFactory().FromPixels(width, height, rgba));
    }

    // 自然尺寸：位图为像素宽高，SVG 为其声明的尺寸。DrawImage 按给定矩形缩放绘制，与它无关。
    public double Width { get; }
    public double Height { get; }

    public void Dispose()
    {
        lock (mLock)
        {
            mContent?.Dispose();
            mContent = null;
        }
    }

    // —— 宿主侧 ——
    // 宿主启动时（插件加载之前）注入；setter internal：插件够不着（同 TuneLabContext.Global）。

    internal static ICanvasImageFactory? Factory { get; set; }

    // 绘制时取内容。已 Dispose 抛 ObjectDisposedException。
    internal ICanvasImageContent Content
    {
        get
        {
            lock (mLock)
                return mContent ?? throw new ObjectDisposedException(nameof(CanvasImage));
        }
    }

    CanvasImage(ICanvasImageContent content)
    {
        mContent = content;
        Width = content.Width;
        Height = content.Height;
    }

    static ICanvasImageFactory RequireFactory() => Factory ?? throw new InvalidOperationException("CanvasImage is only available inside TuneLab.");

    readonly object mLock = new();
    ICanvasImageContent? mContent;
}

// 宿主实现：解码 / 拷贝像素，产出宿主侧的图片内容。
internal interface ICanvasImageFactory
{
    ICanvasImageContent Decode(ReadOnlySpan<byte> data);
    ICanvasImageContent FromPixels(int width, int height, ReadOnlySpan<byte> rgba);
}

internal interface ICanvasImageContent : IDisposable
{
    double Width { get; }
    double Height { get; }
}
