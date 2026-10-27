using System.Drawing;
using System.Numerics;
using System.Text;
using TuneLab.Foundation;
using TuneLab.SDK;
using Point = TuneLab.Foundation.Point;

namespace TuneLab.TestPlugins.V1Voice;

// 画布图片与绘制状态演示（CanvasImage / DrawImage / PushClip / PushOpacity / PushTransform / DevicePixelRatio / 键盘）。
// 从左到右三块：
// · 立绘（编码位图 WebP，插件级缓存）：圆角裁剪；指针不在画布上时半透明；
// · 星形（SVG，插件级缓存）：按值旋转（PushTransform）——横拖旋转、双击复位、画布有焦点时 ← / → 每次 5°（按住连发，松开才提交）；
// · 色带（FromPixelData 自生成）：按 宽 × 设备像素比 生成才清晰，尺寸或像素比变了就先 Dispose 旧的再新建。
// 值 = 星形的旋转角度（度）。声明时取各选中对象的值交给工厂；多选不一致时按第一个对象的角度画、标签写 Mixed。
sealed class ImageDemoCanvas(ICanvasHost host, IReadOnlyList<PropertyValue> values) : ICanvasControl
{
    const double Pad = 8;

    public static readonly PropertyValue Default = PropertyValue.Create(0.0);

    // 声明时调用：values = 每个选中对象的这个字段的值（未设过的给 Default）。
    public static CanvasConfig Config(IReadOnlyList<PropertyValue> values)
        => CanvasConfig.Create(Default, 280, 96, host => new ImageDemoCanvas(host, values))
            .WithSummary(IsMixed(values) ? "Mixed" : $"{Angle(values[0]):0}°");

    static bool IsMixed(IReadOnlyList<PropertyValue> values) => values.Any(v => !v.Equals(values[0]));

    // 重资源在插件级缓存：控件实例每次声明都会重建，图片不跟着它重复解码。
    static readonly Lazy<CanvasImage?> Portrait = new(() =>
    {
        var dir = Path.GetDirectoryName(typeof(ImageDemoCanvas).Assembly.Location) ?? "";
        var path = Path.Combine(dir, "portrait.webp");
        return File.Exists(path) ? CanvasImage.FromEncodedData(File.ReadAllBytes(path)) : null;
    });

    static readonly Lazy<CanvasImage> Star = new(() => CanvasImage.FromEncodedData(Encoding.UTF8.GetBytes(
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <polygon points="32,4 39,24 60,24 43,37 49,58 32,45 15,58 21,37 4,24 25,24" fill="#FFC440" stroke="#FFFFFF" stroke-width="2" stroke-linejoin="round"/>
          <circle cx="32" cy="32" r="5" fill="#73C2E5"/>
        </svg>
        """)));

    static double Angle(PropertyValue value) => value.ToDouble(out var angle) && double.IsFinite(angle) ? angle : 0;

    // 现在画的角度：有草稿画草稿（自己记的），否则画工程里的值（多选不一致时取第一个对象的）。
    double CurrentAngle => mDraft ?? Angle(values[0]);
    bool Mixed => mDraft == null && IsMixed(values);

    void Draft(double angle)
    {
        if (host.SetValue(PropertyValue.Create(angle)))
            mDraft = angle;
    }

    double? mDraft;

    public void Render(ICanvasDrawing d)
    {
        double w = host.Width, h = host.Height, size = h - 2 * Pad;

        // 立绘：保持宽高比，圆角裁剪；不在画布上时半透明。
        if (Portrait.Value is { } portrait)
        {
            double pw = size * portrait.Width / portrait.Height;
            using (d.PushClip(Pad, Pad, pw, size, 8))
            using (d.PushOpacity(mInside ? 1 : 0.5))
                d.DrawImage(portrait, Pad, Pad, pw, size);
        }

        // 星形：绕自身中心旋转。
        double cx = w / 2, cy = h / 2;
        using (d.PushTransform(Matrix3x2.CreateRotation((float)(CurrentAngle * Math.PI / 180), new Vector2((float)cx, (float)cy))))
            d.DrawImage(Star.Value, cx - size / 2, cy - size / 2, size, size);
        d.DrawText(Mixed ? "Mixed" : $"{CurrentAngle:0}°", cx + size / 2 + 4, h - Pad, 0, 0, Color.FromArgb(160, 255, 255, 255), 11, CanvasAlignment.LeftBottom);

        // 色带：按物理像素生成。
        double gw = Math.Min(120, w / 4), gx = w - Pad - gw;
        d.DrawImage(Gradient(gw, size), gx, Pad, gw, size);
        d.DrawRectangle(gx, Pad, gw, size, Color.FromArgb(80, 255, 255, 255));
    }

    // 色带图片：逻辑尺寸 × 设备像素比 = 像素尺寸。变了就先 Dispose 旧的再新建（不可改写，换内容就换一张）。
    CanvasImage Gradient(double width, double height)
    {
        int pw = Math.Max(1, (int)Math.Ceiling(width * host.DevicePixelRatio));
        int ph = Math.Max(1, (int)Math.Ceiling(height * host.DevicePixelRatio));
        if (mGradient != null && mGradient.Width == pw && mGradient.Height == ph)
            return mGradient;
        mGradient?.Dispose();
        var rgba = new byte[pw * ph * 4];
        for (int y = 0; y < ph; y++)
        {
            for (int x = 0; x < pw; x++)
            {
                int i = (y * pw + x) * 4;
                rgba[i] = (byte)(255 * x / Math.Max(1, pw - 1));
                rgba[i + 1] = (byte)(255 * y / Math.Max(1, ph - 1));
                rgba[i + 2] = 200;
                rgba[i + 3] = (byte)(64 + 191 * x / Math.Max(1, pw - 1));   // 非预乘：左边更透明
            }
        }
        mGradient = CanvasImage.FromPixelData(pw, ph, rgba);
        return mGradient;
    }

    public void OnMouseEnter(CanvasMouseEvent e) { mInside = true; host.Invalidate(); }
    public void OnMouseLeave(CanvasMouseEvent e) { mInside = false; host.Invalidate(); }

    public void OnMouseDown(CanvasMouseButtonEvent e)
    {
        if (e.Button != CanvasMouseButton.Primary)
            return;
        if (e.IsDoubleClick)
        {
            Draft(0);
            host.CommitValue();
            return;
        }
        mDragFrom = e.Position.X;
        mDragAngle = CurrentAngle;
        mDragEdit = host.EditScope();   // 宣告编辑：拖动期间面板不刷新、实例不被换掉（还没改值，不写草稿）
    }

    public void OnMouseMove(CanvasMouseEvent e)
    {
        if (mDragFrom is { } from)
            Draft(Math.Round(mDragAngle + (e.Position.X - from) * 2));
    }

    public void OnMouseUp(CanvasMouseButtonEvent e)
    {
        if (mDragFrom == null)
            return;
        mDragFrom = null;
        host.CommitValue();   // 拖动过才有草稿；只点了一下则什么都不做
        mDragEdit?.Dispose();
        mDragEdit = null;
    }

    IDisposable? mDragEdit;

    // ← / → 旋转：每次按下（含按住时系统的重复）改草稿，松开才提交——按住转一圈只成一个撤销步骤。
    // 只吞用到的两个键（置 Handled），其余键照常冒泡给快捷键（空格播放等）。按着键时拖动 / 拖动时按键都按同一份草稿接着改。
    public void OnKeyDown(CanvasKeyEvent e)
    {
        int direction = e.Key switch { CanvasKey.ArrowLeft => -1, CanvasKey.ArrowRight => 1, _ => 0 };
        if (direction == 0 || e.Modifiers != CanvasModifierKeys.None)
            return;
        e.Handled = true;
        Draft(CurrentAngle + 5 * direction);
    }

    public void OnKeyUp(CanvasKeyEvent e)
    {
        if (e.Key is not (CanvasKey.ArrowLeft or CanvasKey.ArrowRight) || mDragFrom != null)
            return;
        e.Handled = true;
        host.CommitValue();
    }

    bool mInside;
    double? mDragFrom;
    double mDragAngle;
    CanvasImage? mGradient;
}
