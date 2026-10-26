using System.Drawing;
using TuneLab.Foundation;
using TuneLab.SDK;
using Point = TuneLab.Foundation.Point;

namespace TuneLab.TestPlugins.V1Voice;

// 画布（CanvasConfig）演示：音素内部的拉伸曲线——横向等距的若干控制点，纵向拖动改值（0..1），值 = 这几个数组成的数组。
// 覆盖：绘制（网格 / 折线 / 控制点 / 文字）、悬停（Enter / Move / Leave）、拖动写草稿 + 松手提交（一个撤销步骤；按下即写草稿锁住画布）、
// 双击控制点复位为线性、滚轮微调指针所在竖列的控制点（只在列上置 Handled，列间照常滚面板；停顿后才提交）、
// 键盘（画布有焦点时）：↑ / ↓ 微调指针所在竖列的控制点（按住连发，松开才提交）、拖动中按 Esc 取消（丢弃草稿）、
// 多选合并值（不一致的点画成空心）。
// 值由插件自定形状，读取时一律校验（撤销 / preset / 粘贴都可能带来任意形状）：形状不对的位置按线性默认处理。
sealed class StretchCurveCanvas(ICanvasHost host) : ICanvasControl
{
    const int Count = 5;
    const double Pad = 10;
    const double HandleRadius = 4.5;

    public static CanvasConfig Config { get; } = CanvasConfig.Create(Linear(), 96, host => new StretchCurveCanvas(host))
        .WithSummary(value => IsLinear(value) ? "Linear" : "Custom");

    static readonly Color Grid = Color.FromArgb(40, 255, 255, 255);
    static readonly Color Curve = Color.FromArgb(255, 115, 194, 229);
    static readonly Color Handle = Color.FromArgb(255, 230, 230, 230);
    static readonly Color Hot = Color.FromArgb(255, 255, 196, 64);
    static readonly Color Label = Color.FromArgb(160, 255, 255, 255);

    static double LinearAt(int i) => i / (double)(Count - 1);

    static PropertyValue Linear() => new PropertyArray(Enumerable.Range(0, Count).Select(i => PropertyValue.Create(LinearAt(i))).ToList());

    static bool IsLinear(PropertyValue value)
    {
        var points = Read(value);
        return points.All(p => p.Known) && points.Select((p, i) => Math.Abs(p.Y - LinearAt(i)) < 1e-9).All(b => b);
    }

    // 读值：每个控制点的 y（0..1）及其是否确定（不确定的按线性默认画成空心）。多选不一致时宿主给的是整体
    // Multiple（不是数组），于是全部点都不确定；形状不对（长度不足、元素不是数）时只有对不上的点不确定。
    static (double Y, bool Known)[] Read(PropertyValue value)
    {
        var result = new (double, bool)[Count];
        value.ToArray(out var array);
        for (int i = 0; i < Count; i++)
        {
            if (array != null && i < array.Count && array[i].ToDouble(out var y) && double.IsFinite(y))
                result[i] = (Math.Clamp(y, 0, 1), true);
            else
                result[i] = (LinearAt(i), false);
        }
        return result;
    }

    static PropertyValue Write((double Y, bool Known)[] points) => new PropertyArray(points.Select(p => PropertyValue.Create(p.Y)).ToList());

    double X(int i) => Pad + (host.Width - 2 * Pad) * LinearAt(i);
    double YOf(double y) => Pad + (host.Height - 2 * Pad) * (1 - y);
    double ValueAt(double y) => Math.Clamp(1 - (y - Pad) / Math.Max(1, host.Height - 2 * Pad), 0, 1);

    int HitTest(Point p)
    {
        var points = Read(host.Value);
        for (int i = 0; i < Count; i++)
        {
            double dx = p.X - X(i), dy = p.Y - YOf(points[i].Y);
            if (dx * dx + dy * dy <= 8 * 8)
                return i;
        }
        return -1;
    }

    // 只按横向判定落在哪个控制点的竖列上：滚轮调的是纵向值，点会离开指针，按点的圆判定则调一格就脱手。
    int HitColumn(Point p)
    {
        for (int i = 0; i < Count; i++)
        {
            if (Math.Abs(p.X - X(i)) <= 8)
                return i;
        }
        return -1;
    }

    public void Render(ICanvasDrawing d)
    {
        double w = host.Width, h = host.Height;
        for (int i = 0; i < Count; i++)
            d.DrawLine(new Point(X(i), Pad), new Point(X(i), h - Pad), Grid);
        d.DrawLine(new Point(Pad, YOf(0.5)), new Point(w - Pad, YOf(0.5)), Grid);

        var points = Read(host.Value);
        d.DrawPolyline(points.Select((p, i) => new Point(X(i), YOf(p.Y))).ToList(), Curve, 2);
        for (int i = 0; i < Count; i++)
        {
            var center = new Point(X(i), YOf(points[i].Y));
            var color = i == (mDragging >= 0 ? mDragging : mHover) ? Hot : Handle;
            if (points[i].Known)
                d.FillEllipse(center, HandleRadius, HandleRadius, color);
            else
                d.DrawEllipse(center, HandleRadius, HandleRadius, color, 1.5);
        }

        int focus = mDragging >= 0 ? mDragging : mHover;
        if (focus >= 0)
        {
            string text = points[focus].Known ? points[focus].Y.ToString("0.00") : "(Multiple)";
            d.DrawText(text, w - Pad, Pad, 0, 0, Label, 11, CanvasAlignment.RightTop);
        }
    }

    public void OnMouseDown(CanvasMouseButtonEvent e)
    {
        if (e.Button != CanvasMouseButton.Primary)
            return;
        int hit = HitTest(e.Position);
        if (hit < 0)
            return;
        if (e.IsDoubleClick)
        {
            var points = Read(host.Value);
            points[hit] = (LinearAt(hit), true);
            host.SetValue(Write(points));
            host.CommitValue();
            return;
        }
        mDragging = hit;
        mMoved = false;
        // 先写一份草稿锁住画布：拖动期间面板不刷新、本实例不会被换掉（即使还没移动、没改值）。
        // 多选不一致时当前值是 Multiple——拿它当草稿锁画布没问题，但不能提交它（见 OnMouseUp）。
        host.SetValue(host.Value);
        host.Invalidate();
    }

    public void OnMouseMove(CanvasMouseEvent e)
    {
        mPointer = e.Position;
        if (mDragging >= 0)
        {
            var points = Read(host.Value);
            points[mDragging] = (ValueAt(e.Position.Y), true);
            host.SetValue(Write(points));
            mMoved = true;
            return;
        }
        UpdateHover(e.Position);
    }

    // Enter 也带位置：新实例（每次提交后都会换）或画布刚出现在静止的指针下时，宿主先发 Enter，据此就能恢复悬停，
    // 不必等用户动鼠标。
    public void OnMouseEnter(CanvasMouseEvent e)
    {
        mPointer = e.Position;
        UpdateHover(e.Position);
    }

    void UpdateHover(Point position)
    {
        int hit = HitTest(position);
        if (hit != mHover)
        {
            mHover = hit;
            host.Invalidate();
        }
    }

    public void OnMouseUp(CanvasMouseButtonEvent e)
    {
        if (mDragging < 0)
            return;   // 按下时没命中控制点
        mDragging = -1;
        // 只是点了一下：什么都没改，丢掉草稿。若提交，多选不一致时草稿就是按下时写的 Multiple——它不是值，宿主会拒绝。
        if (mMoved)
            host.CommitValue();
        else
            host.DiscardValue();
        host.Invalidate();
    }

    public void OnMouseLeave(CanvasMouseEvent e)
    {
        mPointer = null;
        if (mHover < 0)
            return;
        mHover = -1;
        host.Invalidate();
    }

    // 滚轮微调指针所在竖列的控制点（每格 0.05）：只有指针在某个点的竖列上才用这次滚轮（置 Handled，面板不滚），
    // 在列与列之间滚则交还宿主照常滚动面板。
    // 按事件位置现算、不靠 mHover：每次提交都会换一个新实例，悬停状态不跨提交保留。
    public void OnScroll(CanvasWheelEvent e)
    {
        int hit = HitColumn(e.Position);
        if (hit < 0 || e.Delta.Y == 0)
            return;
        e.Handled = true;
        var points = Read(host.Value);
        points[hit] = (Math.Clamp(points[hit].Y + 0.05 * Math.Sign(e.Delta.Y), 0, 1), true);
        host.SetValue(Write(points));
        CommitWhenIdle();
    }

    // 滚轮没有"松手"：停下 400ms 再提交，一段连续滚动只成一个撤销步骤。await 之后回到主线程，可以直接用 host。
    // 这期间草稿一直在，面板不刷新、实例不会被换掉；草稿若被宿主丢弃（如外部改了值），这里再调 CommitValue 也只是空操作。
    async void CommitWhenIdle()
    {
        int ticket = ++mScrollTicket;
        await Task.Delay(400);
        if (ticket == mScrollTicket)
            host.CommitValue();
    }

    int mScrollTicket;

    // ↑ / ↓：微调指针所在竖列的控制点（每次 0.05）。每次按下（含按住时系统的重复）都改草稿，松开才提交——
    // 按住调一段只成一个撤销步骤。Esc：拖动中取消，丢弃草稿回到工程里的值。
    // 只吞用到的键（置 Handled）；指针不在列上、或别的键，照常冒泡给快捷键。
    public void OnKeyDown(CanvasKeyEvent e)
    {
        if (e.Key == CanvasKey.Escape && mDragging >= 0)
        {
            e.Handled = true;
            mDragging = -1;
            host.DiscardValue();
            host.Invalidate();
            return;
        }
        int direction = e.Key switch { CanvasKey.ArrowUp => 1, CanvasKey.ArrowDown => -1, _ => 0 };
        if (direction == 0 || mDragging >= 0 || mPointer is not { } pointer || HitColumn(pointer) is not (>= 0 and var hit))
            return;
        e.Handled = true;
        var points = Read(host.Value);
        points[hit] = (Math.Clamp(points[hit].Y + 0.05 * direction, 0, 1), true);
        host.SetValue(Write(points));
    }

    public void OnKeyUp(CanvasKeyEvent e)
    {
        if (e.Key is not (CanvasKey.ArrowUp or CanvasKey.ArrowDown) || mDragging >= 0)
            return;
        e.Handled = true;
        host.CommitValue();   // 没有草稿（按下时指针不在列上）则什么都不做
    }

    Point? mPointer;   // 指针在画布内的位置（Enter / Move 记下，Leave 清掉）；新实例由宿主补发的 Enter 带来

    int mHover = -1;
    int mDragging = -1;
    bool mMoved;
}
