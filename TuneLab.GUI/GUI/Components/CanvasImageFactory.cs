using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Svg.Skia;
using TuneLab.SDK;

namespace TuneLab.GUI.Components;

// CanvasImage 的宿主实现（启动时注入 CanvasImage.Factory）。
// 解码只用 SkiaSharp、不碰 Avalonia：插件可能在任意线程、甚至界面起来之前创建图片。转成 Avalonia 位图推迟到第一次绘制
// （在 UI 线程）。绘制录下的是位图的引用计数句柄（渲染线程合成时还要用），所以插件 Dispose 只是放掉宿主这一份，
// 已经画出去的那一帧不受影响；图片不可改写，也就不存在"正在显示的像素被改掉"。
internal sealed class CanvasImageFactory : ICanvasImageFactory
{
    // 返回宿主侧的具体类型（绘制要用它的 Draw）；接口成员显式实现、转发到这里。
    public Content Decode(ReadOnlySpan<byte> data)
    {
        if (IsSvg(data))
            return SvgContent.Create(data);
        // 先认格式（认不出时 SKBitmap.Decode 直接抛 ArgumentNullException，不是返回 null），再解码。
        using var encoded = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(encoded);
        var bitmap = (codec == null ? null : SKBitmap.Decode(codec))
            ?? throw new ArgumentException("Unrecognized or corrupt image data (expected PNG, JPEG, WebP or SVG).", nameof(data));
        return new RasterContent(bitmap);
    }

    public Content FromPixels(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        unsafe
        {
            rgba.CopyTo(new Span<byte>((void*)bitmap.GetPixels(), rgba.Length));   // 无行间填充：RowBytes == width × 4
        }
        return new RasterContent(bitmap);
    }

    ICanvasImageContent ICanvasImageFactory.Decode(ReadOnlySpan<byte> data) => Decode(data);
    ICanvasImageContent ICanvasImageFactory.FromPixels(int width, int height, ReadOnlySpan<byte> rgba) => FromPixels(width, height, rgba);

    // SVG 是文本：跳过 BOM 与空白后以 '<' 开头。位图格式都以二进制魔数开头，不会误判。
    static bool IsSvg(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            data = data[3..];
        foreach (var b in data)
        {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                continue;
            return b == (byte)'<';
        }
        return false;
    }

    // 内容的公共面：绘制在锁里做，与（可能在别的线程上的）Dispose 互斥。
    internal abstract class Content : ICanvasImageContent, IDisposable
    {
        public abstract double Width { get; }
        public abstract double Height { get; }

        public void Draw(DrawingContext context, Rect rect, double scaling)
        {
            lock (mLock)
            {
                ObjectDisposedException.ThrowIf(mDisposed, this);
                if (GetBitmap(rect.Size, scaling) is { } bitmap)
                    context.DrawImage(bitmap, rect);
            }
        }

        public void Dispose()
        {
            lock (mLock)
            {
                if (mDisposed)
                    return;
                mDisposed = true;
                Release();
            }
        }

        protected abstract Bitmap? GetBitmap(Size size, double scaling);
        protected abstract void Release();

        // SKBitmap → Avalonia 位图（拷贝像素；之后 SKBitmap 可以释放）。
        protected static Bitmap ToAvalonia(SKBitmap source)
        {
            if (source.ColorType != SKColorType.Rgba8888 && source.ColorType != SKColorType.Bgra8888)
            {
                using var converted = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
                source.CopyTo(converted, SKColorType.Bgra8888);
                return ToAvalonia(converted);
            }
            var format = source.ColorType == SKColorType.Rgba8888 ? PixelFormat.Rgba8888 : PixelFormat.Bgra8888;
            var alpha = source.AlphaType switch
            {
                SKAlphaType.Unpremul => AlphaFormat.Unpremul,
                SKAlphaType.Opaque => AlphaFormat.Opaque,
                _ => AlphaFormat.Premul,
            };
            return new Bitmap(format, alpha, source.GetPixels(), new PixelSize(source.Width, source.Height), new Vector(96, 96), source.RowBytes);
        }

        readonly object mLock = new();
        bool mDisposed;
    }

    // 位图：第一次绘制时转成 Avalonia 位图并放掉解码结果（内存只留一份）。按像素缩放绘制。
    sealed class RasterContent(SKBitmap bitmap) : Content
    {
        public override double Width { get; } = bitmap.Width;
        public override double Height { get; } = bitmap.Height;

        protected override Bitmap? GetBitmap(Size size, double scaling)
        {
            if (mBitmap == null)
            {
                mBitmap = ToAvalonia(mSource!);
                mSource!.Dispose();
                mSource = null;
            }
            return mBitmap;
        }

        protected override void Release()
        {
            mSource?.Dispose();
            mSource = null;
            mBitmap?.Dispose();
            mBitmap = null;
        }

        SKBitmap? mSource = bitmap;
        Bitmap? mBitmap;
    }

    // SVG：保留矢量（SKPicture），按绘制尺寸 × 设备像素比栅格化，缓存最近一次的尺寸——尺寸不变就不重画。
    // 换尺寸时旧位图直接 Dispose：已经录进画面的那一份引用计数还在，不受影响。
    sealed class SvgContent : Content
    {
        public static SvgContent Create(ReadOnlySpan<byte> data)
        {
            using var stream = new System.IO.MemoryStream(data.ToArray());
            var svg = new SKSvg();
            SKPicture? picture;
            try
            {
                picture = svg.Load(stream);
            }
            catch (Exception ex)
            {
                svg.Dispose();
                throw new ArgumentException("Corrupt SVG data.", nameof(data), ex);
            }
            var bounds = picture?.CullRect ?? SKRect.Empty;
            if (picture == null || !(bounds.Width > 0) || !(bounds.Height > 0))
            {
                svg.Dispose();
                throw new ArgumentException("SVG has no drawable content or no size.", nameof(data));
            }
            return new SvgContent(svg, picture, bounds);
        }

        SvgContent(SKSvg svg, SKPicture picture, SKRect bounds)
        {
            mSvg = svg;
            mPicture = picture;
            mBounds = bounds;
        }

        public override double Width => mBounds.Width;
        public override double Height => mBounds.Height;

        protected override Bitmap? GetBitmap(Size size, double scaling)
        {
            int width = PixelLength(size.Width * scaling), height = PixelLength(size.Height * scaling);
            if (width == 0 || height == 0)
                return null;
            if (mBitmap != null && mBitmap.PixelSize == new PixelSize(width, height))
                return mBitmap;

            using var raster = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(raster))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(width / mBounds.Width, height / mBounds.Height);
                canvas.Translate(-mBounds.Left, -mBounds.Top);
                canvas.DrawPicture(mPicture);
            }
            mBitmap?.Dispose();
            mBitmap = ToAvalonia(raster);
            return mBitmap;
        }

        protected override void Release()
        {
            mBitmap?.Dispose();
            mBitmap = null;
            mSvg.Dispose();   // 连同它产出的 SKPicture
        }

        // 栅格化的边长上限：画布不会大到这个程度，防止异常尺寸一次申请出巨量内存。
        static int PixelLength(double length) => double.IsFinite(length) && length > 0 ? (int)Math.Min(Math.Ceiling(length), 8192) : 0;

        readonly SKSvg mSvg;
        readonly SKPicture mPicture;
        readonly SKRect mBounds;
        Bitmap? mBitmap;
    }
}
