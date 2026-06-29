using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using TuneLab.Foundation;

namespace TuneLab.UI;

// 钢琴窗装饰图（声库立绘 + 全局背景图共用）的帧播放器：静态图 = 单帧、无定时器；动图（animated webp / gif /
// apng）= 多帧 + 逐帧时长，DispatcherTimer 按当前帧时长推进、切 CurrentFrame 并触发 FrameChanged（宿主据此重绘）。
// 解码统一走 Skia（SKCodec）——静态 / 动图同一路，静态 webp 与动态 webp 都覆盖；不支持的格式 / 损坏文件
// Load 返回 null（宿主当作无图）。WebM 等视频容器不属图像解码范围，不在此处理。
internal sealed class ImagePlayer : IDisposable
{
    readonly record struct Frame(IImage Image, int DurationMs);

    public IImage? CurrentFrame => mFrames.Count > 0 ? mFrames[mIndex].Image : null;
    public bool IsAnimated => mFrames.Count > 1;
    public event Action? FrameChanged;

    ImagePlayer(List<Frame> frames) => mFrames = frames;

    public static ImagePlayer? Load(string path)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec == null)
                return LoadStaticFallback(path);

            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            int frameCount = Math.Max(1, codec.FrameCount);
            var frameInfos = codec.FrameInfo;   // 动图带逐帧时长 / 依赖；静态图为空数组

            var frames = new List<Frame>(frameCount);
            SKBitmap? prev = null;   // 仅保留上一帧作 delta 帧的解码底（绝大多数动图 RequiredFrame == 前一帧）
            for (int i = 0; i < frameCount; i++)
            {
                var bmp = new SKBitmap(info);
                var options = new SKCodecOptions(i);
                if (frameInfos != null && i < frameInfos.Length && frameInfos[i].RequiredFrame != -1 && prev != null)
                {
                    prev.CopyTo(bmp);   // delta 帧：以上一帧像素为底，再叠本帧增量
                    options = new SKCodecOptions(i, frameInfos[i].RequiredFrame);
                }

                var result = codec.GetPixels(info, bmp.GetPixels(), options);
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                {
                    bmp.Dispose();
                    break;
                }

                int duration = frameInfos != null && i < frameInfos.Length ? frameInfos[i].Duration : 0;
                frames.Add(new Frame(ToBitmap(bmp), duration <= 0 ? DefaultFrameMs : duration));

                prev?.Dispose();
                prev = bmp;
            }
            prev?.Dispose();

            return frames.Count > 0 ? new ImagePlayer(frames) : null;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to decode image '" + path + "': " + ex);
            return null;
        }
    }

    // SKCodec 无法识别（极少数格式）时退回 Avalonia 直解作单张静态图。
    static ImagePlayer? LoadStaticFallback(string path)
    {
        try { return new ImagePlayer(new List<Frame> { new(new Bitmap(path), 0) }); }
        catch (Exception ex) { Log.Error("Failed to load image '" + path + "': " + ex); return null; }
    }

    // SKBitmap(Bgra8888 premul) → Avalonia WriteableBitmap：逐行拷像素（stride 可能不同），避免 unsafe / 编解码往返。
    static Bitmap ToBitmap(SKBitmap src)
    {
        var wb = new WriteableBitmap(new PixelSize(src.Width, src.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        var pixels = src.Bytes;
        int srcStride = src.RowBytes;
        int dstStride = fb.RowBytes;
        int copy = Math.Min(srcStride, dstStride);
        for (int y = 0; y < src.Height; y++)
            Marshal.Copy(pixels, y * srcStride, fb.Address + y * dstStride, copy);
        return wb;
    }

    public void Start()
    {
        if (!IsAnimated || mTimer != null)
            return;

        mTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(mFrames[mIndex].DurationMs) };
        mTimer.Tick += OnTick;
        mTimer.Start();
    }

    public void Stop()
    {
        if (mTimer == null)
            return;

        mTimer.Stop();
        mTimer.Tick -= OnTick;
        mTimer = null;
    }

    void OnTick(object? sender, EventArgs e)
    {
        mIndex = (mIndex + 1) % mFrames.Count;
        if (mTimer != null)
            mTimer.Interval = TimeSpan.FromMilliseconds(mFrames[mIndex].DurationMs);
        FrameChanged?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        foreach (var frame in mFrames)
            (frame.Image as IDisposable)?.Dispose();
        mFrames.Clear();
    }

    // 部分动图把帧时长记为 0（依赖播放器兜底），按浏览器惯例回退到 100ms。
    const int DefaultFrameMs = 100;

    readonly List<Frame> mFrames;
    int mIndex = 0;
    DispatcherTimer? mTimer;
}
