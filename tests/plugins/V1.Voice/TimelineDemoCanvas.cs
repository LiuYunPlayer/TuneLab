using System.Globalization;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Voice;

// 时间轴画布（TimelineCanvasConfig）演示：音素混合式的编辑——一条随时间变化的曲线 + 一个符号 + 一种语言。只在参数面板里、
// 每个对象（note 级 = 每个 note，音素级 = 每个核音素）一块，宽 = 它的时间段、高 = 面板高度。
// 值 = { "curve": [[秒, 值], …], "symbol": "…", "lang": "…" }，秒是**距这块画布起点**（XToTime(0)）的秒数，值 0..1。
// 默认 {} = 中性（没有曲线、没有符号）：未编辑过的对象不因合成后长度变化而改变听感。
// 覆盖：
// · 时间换算（ITimelineCanvasHost.TimeAxis）：点按秒存，画的时候**逐像素采样**——每列 XToTime(x) 换回秒、再按秒插值——
//   跨过速度变化处与按秒插值的合成严格一致（别只换算控制点再连直线）；左上角标出这块覆盖的秒数（XToTime(0) 到 XToTime(Width)）。
// · 露出范围（VisibleBounds）：采样只算露出来的那几列（放大后画布可能上万像素宽）。
// · 按尺寸排版：窄（< 72）或矮（< 56）时只画曲线，不画文字。
// · 交互：点空白处加点、拖点移动（按下写草稿、松手提交）、右键点删除；点左上角符号弹 PopupTextBox 改符号；
//   右键空白处弹 PopupMenu 选语言（当前语言标在项尾，不往前加符号——加在前面会让这一项文字错位）/ 清空曲线。
// 值由插件自定形状，读取时一律校验：形状不对的部分按默认处理。
sealed class TimelineDemoCanvas(ITimelineCanvasHost host, PropertyValue value, string label) : ICanvasControl
{
    public static readonly PropertyValue Default = PropertyObject.Empty;

    // 摘要（侧栏那一行）：点数 + 语言，声明时按值算好。
    public static TimelineCanvasConfig Config(PropertyValue value, string label)
        => TimelineCanvasConfig.Create(Default, host => new TimelineDemoCanvas(host, value, label)).WithSummary(Summary(value));

    static string Summary(PropertyValue value)
    {
        int count = ReadCurve(value).Count;
        string lang = ReadText(value, "lang");
        string text = count == 0 ? "No curve" : count == 1 ? "1 point" : count + " points";
        return lang.Length > 0 ? text + " · " + lang : text;
    }

    const double Pad = 6;
    const double HandleRadius = 4;
    const double CompactWidth = 72, CompactHeight = 56;

    static readonly CanvasPen Frame = CanvasPen.Create(Color.FromArgb(70, 255, 255, 255));
    static readonly CanvasPen Curve = CanvasPen.Create(Color.FromHex("#E58AC9"), 2);
    static readonly CanvasBrush HandleFill = CanvasBrush.Solid(Color.FromRgb(240, 240, 240));
    static readonly CanvasBrush HotFill = CanvasBrush.Solid(Color.FromRgb(255, 196, 64));
    static readonly CanvasBrush LabelBrush = CanvasBrush.Solid(Color.FromArgb(150, 255, 255, 255));
    static readonly CanvasBrush SymbolBrush = CanvasBrush.Solid(Color.FromRgb(255, 255, 255));
    static readonly CanvasTextStyle SymbolStyle = CanvasTextStyle.Create().WithFontSize(13).WithAlignment(CanvasAlignment.LeftTop);
    static readonly CanvasTextStyle LabelStyle = CanvasTextStyle.Create().WithFontSize(10).WithAlignment(CanvasAlignment.RightTop);

    static readonly string[] Languages = ["zh", "ja", "en"];

    // —— 值 ——
    PropertyValue Current => mDraft ?? value;

    static List<(double Time, double Value)> ReadCurve(PropertyValue value)
    {
        var points = new List<(double, double)>();
        if (value.ToObject(out var obj) && obj.Map.TryGetValue("curve", out var curve) && curve.ToArray(out var array))
        {
            foreach (var item in array)
            {
                if (item.ToArray(out var pair) && pair.Count == 2 && pair[0].ToDouble(out var t) && pair[1].ToDouble(out var v)
                    && double.IsFinite(t) && double.IsFinite(v))
                    points.Add((Math.Max(0, t), Math.Clamp(v, 0, 1)));
            }
        }
        points.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return points;
    }

    static string ReadText(PropertyValue value, string key)
        => value.ToObject(out var obj) && obj.Map.TryGetValue(key, out var member) && member.ToString(out var text) ? text : "";

    static PropertyValue WithMember(PropertyValue current, string key, PropertyValue? member)
    {
        var map = new Map<string, PropertyValue>();
        if (current.ToObject(out var obj))
        {
            foreach (var pair in obj.Map)
            {
                if (pair.Key != key)
                    map.Add(pair.Key, pair.Value);
            }
        }
        if (member is { } m)
            map.Add(key, m);
        return new PropertyObject(map);
    }

    static PropertyValue WriteCurve(PropertyValue current, List<(double Time, double Value)> points)
        => WithMember(current, "curve", points.Count == 0 ? (PropertyValue?)null : new PropertyArray(points
            .Select(p => (PropertyValue)new PropertyArray([PropertyValue.Create(p.Time), PropertyValue.Create(p.Value)])).ToList()));

    void Draft(PropertyValue next)
    {
        if (host.SetValue(next))
            mDraft = next;
    }

    // —— 坐标：秒（距起点）↔ x 经宿主的时间轴；值 ↔ y 在面板高度里 ——
    double Start => host.TimeAxis.XToTime(0);
    double XOf(double offset) => host.TimeAxis.TimeToX(Start + offset);
    double OffsetAt(double x) => host.TimeAxis.XToTime(x) - Start;
    double YOf(double v) => Pad + (host.Height - 2 * Pad) * (1 - v);
    double ValueAt(double y) => Math.Clamp(1 - (y - Pad) / Math.Max(1, host.Height - 2 * Pad), 0, 1);

    // 按秒线性插值；首点之前取首点、末点之后取末点。没有点 = 没有曲线。
    static double Evaluate(List<(double Time, double Value)> points, double offset)
    {
        if (offset <= points[0].Time)
            return points[0].Value;
        for (int i = 1; i < points.Count; i++)
        {
            if (offset <= points[i].Time)
            {
                var (t0, v0) = points[i - 1];
                var (t1, v1) = points[i];
                return t1 > t0 ? v0 + (v1 - v0) * (offset - t0) / (t1 - t0) : v1;
            }
        }
        return points[^1].Value;
    }

    bool Compact => host.Width < CompactWidth || host.Height < CompactHeight;

    Rect SymbolBounds => new(Pad, Pad, Math.Max(24, host.Width / 2), 18);

    public void Render(ICanvasDrawing d)
    {
        double w = host.Width, h = host.Height;
        d.StrokeRectangle(new Rect(0.5, 0.5, Math.Max(0, w - 1), Math.Max(0, h - 1)), Frame);

        var points = ReadCurve(Current);
        if (points.Count > 0)
        {
            // 逐像素采样，只算露出来的列。
            var visible = host.VisibleBounds;
            int from = (int)Math.Floor(Math.Max(0, visible.Left)), to = (int)Math.Ceiling(Math.Min(w, visible.Right));
            var line = new List<Point>(Math.Max(0, to - from + 1));
            for (int x = from; x <= to; x++)
                line.Add(new Point(x, YOf(Evaluate(points, OffsetAt(x)))));
            if (line.Count >= 2)
                d.DrawPolyline(line, Curve);
            for (int i = 0; i < points.Count; i++)
                d.FillCircle(new Point(XOf(points[i].Time), YOf(points[i].Value)), HandleRadius, i == mHot ? HotFill : HandleFill);
        }

        if (Compact)
            return;
        string symbol = ReadText(Current, "symbol");
        string lang = ReadText(Current, "lang");
        d.DrawText(symbol.Length > 0 ? symbol : "(" + label + ")", SymbolBounds, symbol.Length > 0 ? SymbolBrush : LabelBrush, SymbolStyle);
        var axis = host.TimeAxis;
        d.DrawText((lang.Length > 0 ? lang + "  " : "") + axis.XToTime(0).ToString("0.000", CultureInfo.InvariantCulture) + "–"
            + axis.XToTime(w).ToString("0.000", CultureInfo.InvariantCulture) + "s", new Rect(w - Pad, Pad, 0, 0), LabelBrush, LabelStyle);
    }

    int HitTest(Point at)
    {
        var points = ReadCurve(Current);
        for (int i = 0; i < points.Count; i++)
        {
            double dx = XOf(points[i].Time) - at.X, dy = YOf(points[i].Value) - at.Y;
            if (dx * dx + dy * dy <= (HandleRadius + 3) * (HandleRadius + 3))
                return i;
        }
        return -1;
    }

    public void OnMouseDown(CanvasMouseButtonEvent e)
    {
        if (e.Button == CanvasMouseButton.Secondary)
        {
            int hit = HitTest(e.Position);
            if (hit >= 0)
            {
                var points = ReadCurve(Current);
                points.RemoveAt(hit);
                Draft(WriteCurve(Current, points));
                host.CommitValue();
                mHot = -1;
                return;
            }
            ShowMenu(e.Position);
            return;
        }
        if (e.Button != CanvasMouseButton.Primary)
            return;

        // 点画在符号上面，按下也先归点：压在符号区里的点照样能拖，没点中点才弹符号框。
        int index = HitTest(e.Position);
        if (index < 0 && !Compact && SymbolBounds.Contains(e.Position))
        {
            EditSymbol();
            return;
        }

        var curve = ReadCurve(Current);
        if (index < 0)
        {
            curve.Add((Math.Max(0, OffsetAt(e.Position.X)), ValueAt(e.Position.Y)));
            curve.Sort((a, b) => a.Time.CompareTo(b.Time));
            index = curve.FindIndex(p => p.Time == Math.Max(0, OffsetAt(e.Position.X)));
            Draft(WriteCurve(Current, curve));
        }
        mDragging = index;
        mDragEdit = host.EditScope();
    }

    public void OnMouseMove(CanvasMouseEvent e)
    {
        if (mDragging >= 0)
        {
            var points = ReadCurve(Current);
            if (mDragging >= points.Count)
                return;
            points[mDragging] = (Math.Max(0, OffsetAt(Math.Clamp(e.Position.X, 0, host.Width))), ValueAt(e.Position.Y));
            var moved = points[mDragging];
            points.Sort((a, b) => a.Time.CompareTo(b.Time));
            mDragging = points.IndexOf(moved);
            Draft(WriteCurve(Current, points));
            return;
        }
        int hot = HitTest(e.Position);
        if (hot != mHot)
        {
            mHot = hot;
            host.Invalidate();
        }
    }

    public void OnMouseLeave(CanvasMouseEvent e)
    {
        if (mHot >= 0)
        {
            mHot = -1;
            host.Invalidate();
        }
    }

    public void OnMouseUp(CanvasMouseButtonEvent e)
    {
        if (e.Button != CanvasMouseButton.Primary || mDragging < 0)
            return;
        mDragging = -1;
        host.CommitValue();   // 加点 / 拖点都写过草稿：提交成一个撤销步骤
        mDragEdit?.Dispose();
        mDragEdit = null;
    }

    async void EditSymbol()
    {
        string? text = await host.PopupTextBox(ReadText(Current, "symbol"), SymbolBounds, SymbolStyle);
        if (text == null)
            return;
        Draft(WithMember(Current, "symbol", text.Length > 0 ? PropertyValue.Create(text) : (PropertyValue?)null));
        host.CommitValue();
    }

    // 右键菜单：值写成命令名，拿到后自己分派。
    async void ShowMenu(Point at)
    {
        string lang = ReadText(Current, "lang");
        var items = new List<ComboBoxItem>();
        foreach (var language in Languages)
            items.Add(new ComboBoxItem(PropertyValue.Create("lang:" + language), "Language: " + language + (language == lang ? "  (current)" : "")));
        items.Add(ComboBoxItem.Separator());
        items.Add(new ComboBoxItem(PropertyValue.Create("clear"), "Clear Curve"));

        var picked = await host.PopupMenu(new Rect(at.X, at.Y, 0, 0), items);
        if (picked is not { } command || !command.ToString(out var name))
            return;
        if (name.StartsWith("lang:", StringComparison.Ordinal))
            Draft(WithMember(Current, "lang", PropertyValue.Create(name[5..])));
        else if (name == "clear")
            Draft(WithMember(Current, "curve", null));
        host.CommitValue();
    }

    PropertyValue? mDraft;
    int mDragging = -1;
    int mHot = -1;
    IDisposable? mDragEdit;
}
