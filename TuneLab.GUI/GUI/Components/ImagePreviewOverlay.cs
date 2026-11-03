using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

// 图片放大预览（lightbox）：盖在窗口 OverlayLayer 上的半透明底 + 一张可缩放平移的图。
// 滚轮以光标为锚缩放、左/中键拖拽平移、未拖动的点击或 Esc 关闭。
//
// 原本是 Agent 侧栏里的私有实现（图片附件点缩略图放大）。手册窗与扩展详情窗里的 markdown 插图
// 同样需要"点开看清"，故抽成共享件——三处若各写一套，缩放手感与关闭方式必然彼此不同。
internal static class ImagePreviewOverlay
{
    const double MinScale = 0.1;
    const double MaxScale = 10;

    // anchor 只用来找它所在窗口的 OverlayLayer；同一时刻只留一个预览（再点别的图即替换、并复位缩放/平移）。
    public static void Show(Visual anchor, IImage source)
    {
        var layer = OverlayLayer.GetOverlayLayer(anchor);
        if (layer == null)
            return;

        if (mCurrent != null)
        {
            // 换窗口时旧浮层挂在旧 layer 上，故从它自己的父层摘除，而不是往当前 layer 上找。
            (mCurrent.Parent as Panel)?.Children.Remove(mCurrent);
            mCurrent = null;
        }

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        var image = new Image
        {
            Source = source,
            // 比视口大的图先缩到装得下（否则一开就只看见左上角一块），小图保持原尺寸；之后滚轮再自由缩放。
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.TopLeft, // 配合下方公式：缩放绕图片左上角，平移用视口像素
            RenderTransform = new TransformGroup { Children = { scale, translate } },
        };

        var backdrop = new Border
        {
            Background = new SolidColorBrush(Colors.Black, 0.85),
            ClipToBounds = true, // 放大平移后超出视口的部分裁掉
            Focusable = true,    // 接收 Esc
            IsTabStop = false,   // 焦点由打开时主动给，Tab 不切进来（否则会画出 Avalonia 的焦点框）
            Cursor = new Cursor(StandardCursorType.Arrow),
            Child = image,
        };
        mCurrent = backdrop;

        void Close()
        {
            layer.Children.Remove(backdrop);
            if (ReferenceEquals(mCurrent, backdrop))
                mCurrent = null;
        }

        // OverlayLayer 继承自 Canvas，不拉伸子项——须把 backdrop 尺寸显式设为 layer 尺寸才能盖满窗口。
        void SyncSize()
        {
            backdrop.Width = layer.Bounds.Width;
            backdrop.Height = layer.Bounds.Height;
        }
        SyncSize();
        EventHandler<AvaloniaPropertyChangedEventArgs> onLayerBounds = (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty)
                SyncSize();
        };
        layer.PropertyChanged += onLayerBounds;

        // 滚轮缩放：以光标位置为锚点（公式 t1 = c - A - f·(c - A - t0)，A=图片布局左上角，f=新旧缩放比）。
        backdrop.PointerWheelChanged += (_, e) =>
        {
            e.Handled = true;
            // 触控板双指横滑（横向分量占优）在预览里没有对应动作，直接吃掉：不忽略的话 Delta.Y==0 会落进
            // 下面的三元判断被当成"向下滚"、横滑一下图就缩小一档。
            if (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
                return;

            var s0 = scale.ScaleX;
            var s1 = Math.Clamp(s0 * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), MinScale, MaxScale);
            if (s1 == s0)
                return;
            var f = s1 / s0;
            var c = e.GetPosition(backdrop);
            var a = image.Bounds.Position; // 居中布局后的左上角（不受 RenderTransform 影响）
            translate.X = c.X - a.X - f * (c.X - a.X - translate.X);
            translate.Y = c.Y - a.Y - f * (c.Y - a.Y - translate.Y);
            scale.ScaleX = scale.ScaleY = s1;
        };

        // 左键/中键拖拽平移；未拖动的点击（窗口任意处，含图片本身）关闭预览。
        var pressed = false;
        var dragged = false;
        var start = default(Point);
        var last = default(Point);
        backdrop.PointerPressed += (_, e) =>
        {
            var p = e.GetCurrentPoint(backdrop).Properties;
            if (!p.IsLeftButtonPressed && !p.IsMiddleButtonPressed)
                return;
            pressed = true;
            dragged = false;
            start = last = e.GetPosition(backdrop);
            e.Pointer.Capture(backdrop);
            e.Handled = true;
        };
        backdrop.PointerMoved += (_, e) =>
        {
            if (!pressed)
                return;
            var now = e.GetPosition(backdrop);
            translate.X += now.X - last.X;
            translate.Y += now.Y - last.Y;
            last = now;
            if (Math.Abs(now.X - start.X) + Math.Abs(now.Y - start.Y) > 4)
                dragged = true; // 超阈值算拖拽，松手不关闭
            e.Handled = true;
        };
        backdrop.PointerReleased += (_, e) =>
        {
            if (!pressed)
                return;
            pressed = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            if (!dragged)
                Close(); // 点击（未拖动）任意处关闭
        };
        backdrop.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
        backdrop.DetachedFromVisualTree += (_, _) => layer.PropertyChanged -= onLayerBounds;

        layer.Children.Add(backdrop);
        backdrop.Focus(); // 让 Esc 立即生效
    }

    static Control? mCurrent;   // 当前打开的浮层（单实例守卫：再点图片先关旧的）
}
