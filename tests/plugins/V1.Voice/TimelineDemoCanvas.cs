using System.Globalization;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Voice;

// 时间轴画布（TimelineCanvasConfig）演示：一条贯穿 part 的"标记"轨——每个标记占一格（半拍宽、一个半音高），落在 tick × 音高上。
// 值 = { "marks": [[tick, pitch], …] }，tick 是局部 tick（TickToX(0) = 原点，可为负、可超出 part），pitch 是键号（整数，占 [n, n + 1)）。
// 没设过（Null）= 没有标记。覆盖：
// · 整条时间轴：part 裁剪外照样画、照样能加（宿主盖遮罩）；原点画一条竖线，拆分后右半的原点仍在原处（偏移由宿主维护）。
// · 音高轴：格子与背后音符区的琴键对齐（PitchToY(n) = n 号键下沿）。
// · 改动范围：加 / 拖 / 删提交时带上受影响的那几拍（CommitValue(ranges)，重复的自己去掉）；会话订阅后只重合成相交的块（状态带可见）。
// · IsBackground：作背景只画格子，不画文字与原点线。
// · 事件归属：只接自己用的按下（左键、右键点中格子）并置 Handled；中键、空白处右键不接，冒泡给宿主。
sealed class TimelineDemoCanvas(ITimelineCanvasHost host, PropertyValue value) : ICanvasControl
{
    public const string Id = "markers";
    public const double MarkLength = 240;   // 半拍

    public static TimelineCanvasConfig Config(PropertyValue value)
        => TimelineCanvasConfig.Create(host => new TimelineDemoCanvas(host, value)).WithColor("#7FD1AE");

    static readonly CanvasBrush MarkFill = CanvasBrush.Solid(Color.FromArgb(200, 127, 209, 174));
    static readonly CanvasBrush HotFill = CanvasBrush.Solid(Color.FromRgb(255, 196, 64));
    static readonly CanvasPen OriginPen = CanvasPen.Create(Color.FromArgb(160, 255, 255, 255));
    static readonly CanvasBrush LabelBrush = CanvasBrush.Solid(Color.FromArgb(170, 255, 255, 255));
    static readonly CanvasTextStyle LabelStyle = CanvasTextStyle.Create().WithFontSize(10).WithAlignment(CanvasAlignment.LeftTop);

    PropertyValue Current => mDraft ?? value;

    // 读值一律校验：形状不对的标记跳过。
    public static List<(double Tick, int Pitch)> ReadMarks(PropertyValue value)
    {
        var marks = new List<(double, int)>();
        if (value.ToObject(out var obj) && obj.Map.TryGetValue("marks", out var list) && list.ToArray(out var array))
        {
            foreach (var item in array)
            {
                if (item.ToArray(out var pair) && pair.Count == 2 && pair[0].ToDouble(out var tick) && pair[1].ToDouble(out var pitch)
                    && double.IsFinite(tick) && double.IsFinite(pitch))
                    marks.Add((tick, (int)Math.Floor(pitch)));
            }
        }
        return marks;
    }

    static PropertyValue WriteMarks(List<(double Tick, int Pitch)> marks)
        => new PropertyObject(new Map<string, PropertyValue>
        {
            { "marks", new PropertyArray(marks.Select(m => (PropertyValue)new PropertyArray([PropertyValue.Create(m.Tick), PropertyValue.Create((double)m.Pitch)])).ToList()) },
        });

    Rect MarkRect((double Tick, int Pitch) mark)
    {
        double left = host.TimeAxis.TickToX(mark.Tick), right = host.TimeAxis.TickToX(mark.Tick + MarkLength);
        double top = host.PitchAxis.PitchToY(mark.Pitch + 1), bottom = host.PitchAxis.PitchToY(mark.Pitch);
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public void Render(ICanvasDrawing d)
    {
        var marks = ReadMarks(Current);
        var visible = host.VisibleBounds;
        for (int i = 0; i < marks.Count; i++)
        {
            var rect = MarkRect(marks[i]);
            if (rect.Right < visible.Left || rect.Left > visible.Right)
                continue;
            d.FillRectangle(rect, i == mHot || i == mDragging ? HotFill : MarkFill, 2);
        }

        if (host.IsBackground)
            return;
        double originX = host.TimeAxis.TickToX(0);
        d.DrawPolyline([new Point(originX, 0), new Point(originX, host.Height)], OriginPen);
        var inv = CultureInfo.InvariantCulture;
        d.DrawText("origin", new Rect(originX + 4, 4, 0, 0), LabelBrush, LabelStyle);
        d.DrawText(marks.Count + " marks  view " + host.TimeAxis.XToTick(visible.Left).ToString("0", inv) + "–" + host.TimeAxis.XToTick(visible.Right).ToString("0", inv)
            + "t  " + host.TimeAxis.XToSecond(visible.Left).ToString("0.000", inv) + "–" + host.TimeAxis.XToSecond(visible.Right).ToString("0.000", inv) + "s",
            new Rect(visible.Left + 8, host.Height - 18, 0, 0), LabelBrush, LabelStyle);
    }

    int HitTest(Point at)
    {
        var marks = ReadMarks(Current);
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            if (MarkRect(marks[i]).Contains(at))
                return i;
        }
        return -1;
    }

    // 指针下的格子：tick 吸附到半拍，音高取指针下的键。
    (double Tick, int Pitch) CellAt(Point at)
        => (Math.Floor(host.TimeAxis.XToTick(at.X) / MarkLength) * MarkLength, (int)Math.Floor(host.PitchAxis.YToPitch(at.Y)));

    void Draft(List<(double Tick, int Pitch)> marks)
    {
        var next = WriteMarks(marks);
        if (host.SetValue(next))
            mDraft = next;
    }

    static TuneLab.Foundation.Range SpanOf((double Tick, int Pitch) mark) => new(mark.Tick, mark.Tick + MarkLength);

    // 提交并带上改动范围：去掉重复的段（原位置与落点在同一拍、只换了音高时就是同一段）——宿主原样转发，不替插件合并。
    void Commit(params (double Tick, int Pitch)[] changed)
        => host.CommitValue(changed.Select(SpanOf).Distinct().ToList());

    public void OnMouseDown(CanvasMouseButtonEvent e)
    {
        var marks = ReadMarks(Current);
        int hit = HitTest(e.Position);
        if (e.Button == CanvasMouseButton.Secondary)
        {
            if (hit < 0)
                return;   // 空白处右键不接：交给外层（如范围选区菜单）
            e.Handled = true;
            var removed = marks[hit];
            marks.RemoveAt(hit);
            Draft(marks);
            Commit(removed);
            mHot = -1;
            return;
        }
        if (e.Button != CanvasMouseButton.Primary)
            return;   // 中键不接：参数面板照常平移时间轴
        e.Handled = true;

        if (hit < 0)
        {
            marks.Add(CellAt(e.Position));
            hit = marks.Count - 1;
            Draft(marks);
            mOrigin = null;   // 新加的：只报落点
        }
        else
        {
            mOrigin = marks[hit];
        }
        mDragging = hit;
        mDragEdit = host.EditScope();
    }

    public void OnMouseMove(CanvasMouseEvent e)
    {
        if (mDragging >= 0)
        {
            var marks = ReadMarks(Current);
            if (mDragging >= marks.Count)
                return;
            var cell = CellAt(e.Position);
            if (marks[mDragging] != cell)
            {
                marks[mDragging] = cell;
                Draft(marks);
            }
            return;
        }
        int hot = HitTest(e.Position);
        if (hot != mHot)
        {
            mHot = hot;
            host.Invalidate();
        }
    }

    public void OnMouseUp(CanvasMouseButtonEvent e)
    {
        if (e.Button != CanvasMouseButton.Primary || mDragging < 0)
            return;
        var marks = ReadMarks(Current);
        int index = mDragging;
        mDragging = -1;
        // 按最终结果报：拖回原处 = 没改，丢掉草稿、什么都不报；否则报原位置（新加的没有）与落点。
        if (index < marks.Count && mOrigin == marks[index])
        {
            host.DiscardValue();
        }
        else
        {
            var changed = new List<(double Tick, int Pitch)>();
            if (mOrigin is { } origin)
                changed.Add(origin);
            if (index < marks.Count)
                changed.Add(marks[index]);
            Commit(changed.ToArray());
        }
        mDragEdit?.Dispose();
        mDragEdit = null;
    }

    public void OnMouseLeave(CanvasMouseEvent e)
    {
        if (mHot >= 0)
        {
            mHot = -1;
            host.Invalidate();
        }
    }

    PropertyValue? mDraft;
    (double Tick, int Pitch)? mOrigin;
    int mDragging = -1;
    int mHot = -1;
    IDisposable? mDragEdit;
}
