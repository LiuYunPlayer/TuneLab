using System.Globalization;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Voice;

// 画布（CanvasConfig）演示：音素内部的拉伸曲线——横向等距的若干控制点，纵向拖动改值（0..1）。
// 值 = { "points": [y0, …], "note": "…" }，默认 {}（未设 = 线性、无备注）。顶层用对象：加成员只是多一个键，不必换形状。
// 覆盖：绘制（网格 / 折线 / 控制点 / 文字）、悬停（Move / Leave；Enter 之后宿主保证紧跟一个 Move）、按下 EditScope 宣告编辑、拖动写草稿、松手提交（一个撤销步骤）、
// 双击控制点复位为线性、滚轮微调指针所在竖列的控制点（只在列上置 Handled，列间照常滚面板；停顿后才提交）、
// 键盘（画布有焦点时）：↑ / ↓ 微调指针所在竖列的控制点（按住连发，松开才提交）、拖动中按 Esc 取消（丢弃草稿）、
// 取值：声明时从 context 取出各选中对象的值交给工厂（宿主不把值读回给控件）；多选逐点合并——各对象一致的点实心、不一致的空心；
// 收起摘要在声明时算好。草稿由控件自己持有（写给宿主的同时记下，画的就是它）。
// 弹出（右键）：PopupMenu 当操作菜单——点上右键有"复位这个点 / 输入数值"，任意处有"编辑备注 / 全部复位"；
// "输入数值"在右上角数值标签处弹 PopupTextBox（传画字的参数，框正好盖在字上）；"编辑备注"弹多行文本框盖在居中画的备注上；
// "全部复位"先经 DialogService.Confirm 确认。
// 值由插件自定形状，读取时一律校验（撤销 / preset / 粘贴都可能带来任意形状）：形状不对的位置按线性默认处理。
sealed class StretchCurveCanvas(ICanvasHost host, IReadOnlyList<PropertyValue> values) : ICanvasControl
{
    const int Count = 5;
    const double Pad = 10;
    const double HandleRadius = 4.5;

    // 默认值 {} = 未设（线性、无备注）。声明处读值时用同一个，别各写一份。
    public static readonly PropertyValue Default = PropertyObject.Empty;

    // 声明时调用：values = 每个选中对象的这个字段的值（未设过的给 Default）。
    public static CanvasConfig Config(IReadOnlyList<PropertyValue> values)
        => CanvasConfig.Create(Default, 280, 96, host => new StretchCurveCanvas(host, values))
            .WithSummary(!AllEqual(values) ? "Mixed" : IsLinear(values[0]) ? "Linear" : "Custom");

    // 画笔、画刷、文字样式都不可变：建好存进静态字段，每帧直接用（宿主据此缓存原生对象）。
    static readonly CanvasPen Grid = CanvasPen.Create(Color.FromArgb(40, 255, 255, 255));
    static readonly CanvasPen Curve = CanvasPen.Create(Color.FromHex("#73C2E5"), 2);
    static readonly Color Handle = Color.FromRgb(230, 230, 230);
    static readonly Color Hot = Color.FromRgb(255, 196, 64);
    static readonly CanvasBrush HandleFill = CanvasBrush.Solid(Handle), HotFill = CanvasBrush.Solid(Hot);
    static readonly CanvasPen HandleRing = CanvasPen.Create(Handle, 1.5), HotRing = CanvasPen.Create(Hot, 1.5);
    static readonly CanvasBrush Label = CanvasBrush.Solid(Color.FromArgb(160, 255, 255, 255));
    static readonly CanvasBrush Note = CanvasBrush.Solid(Color.FromArgb(90, 255, 255, 255));
    // 数值标签以右上角为锚、字号小一点；备注用默认样式（不传：字号跟随宿主、居中）。弹出文本框时传的样式与画字同一套。
    static readonly CanvasTextStyle LabelStyle = CanvasTextStyle.Create().WithFontSize(11).WithAlignment(CanvasAlignment.RightTop);

    static double LinearAt(int i) => i / (double)(Count - 1);

    static bool IsLinear(PropertyValue value)
    {
        var points = Read(value);
        return points.All(p => p.Known) && points.Select((p, i) => Math.Abs(p.Y - LinearAt(i)) < 1e-9).All(b => b);
    }

    // 读值：每个控制点的 y（0..1）及其是否确定（不确定的按线性默认画成空心）。
    // · 未设（对象里没有 points）= 线性，是确定的；
    // · 形状不对（不是对象、points 不是数组、长度不足、元素不是数）时只有对不上的点不确定。
    static (double Y, bool Known)[] Read(PropertyValue value)
    {
        var result = new (double, bool)[Count];
        PropertyArray? array = null;
        bool unset = false;
        if (value.ToObject(out var obj))
        {
            if (obj.Map.TryGetValue("points", out var points))
                points.ToArray(out array);
            else
                unset = true;
        }
        for (int i = 0; i < Count; i++)
        {
            if (array != null && i < array.Count && array[i].ToDouble(out var y) && double.IsFinite(y))
                result[i] = (Math.Clamp(y, 0, 1), true);
            else
                result[i] = (LinearAt(i), unset);
        }
        return result;
    }

    static string ReadNote(PropertyValue value)
        => value.ToObject(out var obj) && obj.Map.TryGetValue("note", out var note) && note.ToString(out var text) ? text : "";

    static bool AllEqual(IReadOnlyList<PropertyValue> values) => values.All(v => v.Equals(values[0]));

    // 多选逐点合并：各对象这个点都确定且相同 = 确定，否则不确定（画空心，位置取第一个对象的）。
    static (double Y, bool Known)[] Merge(IReadOnlyList<PropertyValue> values)
    {
        var first = Read(values[0]);
        foreach (var other in values.Skip(1).Select(Read))
        {
            for (int i = 0; i < Count; i++)
            {
                if (!other[i].Known || Math.Abs(other[i].Y - first[i].Y) > 1e-9)
                    first[i].Known = false;
            }
        }
        return first;
    }

    // 现在画的：有草稿画草稿，否则画各对象的合并。
    (double Y, bool Known)[] Points() => mDraft is { } draft ? Read(draft) : Merge(values);
    string NoteText() => mDraft is { } draft ? ReadNote(draft) : AllEqual(values) ? ReadNote(values[0]) : "";

    // 改值的起点：有草稿从草稿改；多选各对象一致从那个值改；不一致时没有一个真实的值可依，从空对象开始——
    // 提交时同一个值写到全部对象，各对象自己的其余成员（如备注）随之被覆盖。
    PropertyValue Base => mDraft ?? (AllEqual(values) ? values[0] : PropertyObject.Empty);

    // 写草稿：交给宿主保管（提交时写进工程），同时自己记下——宿主不读回给控件，画的是自己手里这个。
    void Draft(PropertyValue value)
    {
        if (host.SetValue(value))
            mDraft = value;
    }

    // 换掉（member 为 null 则删掉）一个成员，其余成员原样保留。
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
        if (member is { } value)
            map.Add(key, value);
        return new PropertyObject(map);
    }

    PropertyValue Write((double Y, bool Known)[] points)
        => WithMember(Base, "points", new PropertyArray(points.Select(p => PropertyValue.Create(p.Y)).ToList()));

    double X(int i) => Pad + (host.Width - 2 * Pad) * LinearAt(i);
    double YOf(double y) => Pad + (host.Height - 2 * Pad) * (1 - y);
    double ValueAt(double y) => Math.Clamp(1 - (y - Pad) / Math.Max(1, host.Height - 2 * Pad), 0, 1);

    int HitTest(Point p)
    {
        var points = Points();
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

    // 两处文字的位置：Render 画字与弹出文本框用同一个矩形。数值标签是零尺寸矩形 = 以右上角为锚。
    Rect LabelAt => new(host.Width - Pad, Pad, 0, 0);
    Rect NoteBounds => new(Pad, Pad, host.Width - 2 * Pad, host.Height - 2 * Pad);

    public void Render(ICanvasDrawing d)
    {
        double w = host.Width, h = host.Height;
        for (int i = 0; i < Count; i++)
            d.DrawLine(new Point(X(i), Pad), new Point(X(i), h - Pad), Grid);
        d.DrawLine(new Point(Pad, YOf(0.5)), new Point(w - Pad, YOf(0.5)), Grid);
        d.DrawText(NoteText(), NoteBounds, Note);

        var points = Points();
        d.DrawPolyline(points.Select((p, i) => new Point(X(i), YOf(p.Y))).ToList(), Curve);
        for (int i = 0; i < Count; i++)
        {
            var center = new Point(X(i), YOf(points[i].Y));
            bool hot = i == (mDragging >= 0 ? mDragging : mHover);
            if (points[i].Known)
                d.FillEllipse(center, HandleRadius, HandleRadius, hot ? HotFill : HandleFill);
            else
                d.StrokeEllipse(center, HandleRadius, HandleRadius, hot ? HotRing : HandleRing);
        }

        int focus = mDragging >= 0 ? mDragging : mHover;
        if (focus >= 0)
        {
            string text = points[focus].Known ? points[focus].Y.ToString("0.00") : "(Multiple)";
            d.DrawText(text, LabelAt, Label, LabelStyle);
        }
    }

    public void OnMouseDown(CanvasMouseButtonEvent e)
    {
        if (e.Button == CanvasMouseButton.Secondary && mDragging < 0)
        {
            ShowMenu(e.Position);
            return;
        }
        if (e.Button != CanvasMouseButton.Primary)
            return;
        int hit = HitTest(e.Position);
        if (hit < 0)
            return;
        if (e.IsDoubleClick)
        {
            var points = Points();
            points[hit] = (LinearAt(hit), true);
            Draft(Write(points));
            host.CommitValue();
            return;
        }
        mDragging = hit;
        // 宣告开始编辑：拖动期间面板不刷新、本实例不会被换掉（即使还没移动）。不写任何值——还没改呢。
        mDragEdit = host.EditScope();
        host.Invalidate();
    }

    public void OnMouseMove(CanvasMouseEvent e)
    {
        mPointer = e.Position;
        if (mDragging >= 0)
        {
            var points = Points();
            points[mDragging] = (ValueAt(e.Position.Y), true);
            Draft(Write(points));
            return;
        }
        UpdateHover(e.Position);
    }

    // 不必实现 OnMouseEnter：宿主保证每个 Enter 之后紧跟一个同位置的 Move（新实例、画布出现在静止的指针下时也一样），
    // 悬停在 OnMouseMove 里就恢复了。
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
        host.CommitValue();   // 拖动过才有草稿；只是点了一下就没有草稿，这里什么都不做
        EndDrag();
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
        var points = Points();
        points[hit] = (Math.Clamp(points[hit].Y + 0.05 * Math.Sign(e.Delta.Y), 0, 1), true);
        Draft(Write(points));
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
            host.DiscardValue();   // 取消：丢掉草稿回到工程里的值……
            mDraft = null;
            EndDrag();             // ……再结束编辑——两件事各说各的
            host.Invalidate();
            return;
        }
        int direction = e.Key switch { CanvasKey.ArrowUp => 1, CanvasKey.ArrowDown => -1, _ => 0 };
        if (direction == 0 || mDragging >= 0 || mPointer is not { } pointer || HitColumn(pointer) is not (>= 0 and var hit))
            return;
        e.Handled = true;
        var points = Points();
        points[hit] = (Math.Clamp(points[hit].Y + 0.05 * direction, 0, 1), true);
        Draft(Write(points));
    }

    public void OnKeyUp(CanvasKeyEvent e)
    {
        if (e.Key is not (CanvasKey.ArrowUp or CanvasKey.ArrowDown) || mDragging >= 0)
            return;
        e.Handled = true;
        host.CommitValue();   // 没有草稿（按下时指针不在列上）则什么都不做
    }

    Point? mPointer;   // 指针在画布内的位置（Move 记下，Leave 清掉）；新实例由宿主补发的 Enter + Move 带来

    // 右键菜单：值写成命令名，拿到后自己分派。弹出期间值若被别处改了，本实例已作废：SetValue 返回 false、什么都不写。
    async void ShowMenu(Point at)
    {
        int hit = HitTest(at);
        var items = new List<ComboBoxItem>();
        if (hit >= 0)
        {
            items.Add(new ComboBoxItem(PropertyValue.Create("reset-point"), "Reset This Point"));
            items.Add(new ComboBoxItem(PropertyValue.Create("set-point"), "Set Value..."));
            items.Add(ComboBoxItem.Separator());
        }
        items.Add(new ComboBoxItem(PropertyValue.Create("edit-note"), "Edit Note..."));
        items.Add(new ComboBoxItem(PropertyValue.Create("reset-all"), "Reset All..."));

        var picked = await host.PopupMenu(new Rect(at.X, at.Y, 0, 0), items);
        if (picked is not { } value || !value.ToString(out var command))
            return;
        switch (command)
        {
            case "reset-point":
                SetPoint(hit, LinearAt(hit));
                break;
            case "set-point":
                await EditPoint(hit);
                break;
            case "edit-note":
                await EditNote();
                break;
            case "reset-all":
                if (await host.DialogService.Confirm("Reset Curve", "Reset every point to linear? The note is kept."))
                {
                    Draft(WithMember(Base, "points", null));
                    host.CommitValue();
                }
                break;
        }
    }

    void SetPoint(int index, double y)
    {
        var points = Points();
        points[index] = (Math.Clamp(y, 0, 1), true);
        Draft(Write(points));
        host.CommitValue();
    }

    // 在右上角数值标签处弹文本框：矩形与样式就是 Render 里画标签用的那两个，框正好盖在字上。
    async Task EditPoint(int index)
    {
        var point = Points()[index];
        mHover = index;   // 弹出期间标签照常显示这个点
        host.Invalidate();
        string? text = await host.PopupTextBox(point.Known ? point.Y.ToString("0.00", CultureInfo.InvariantCulture) : "",
            LabelAt, LabelStyle);
        if (text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && double.IsFinite(y))
            SetPoint(index, y);
    }

    // 备注：多行文本框盖在居中画的备注上（同一个矩形、同样居中），Shift+Enter 换行。清空即删掉成员。
    async Task EditNote()
    {
        string? text = await host.PopupTextBox(NoteText(), NoteBounds, multiline: true);
        if (text == null)
            return;
        Draft(WithMember(Base, "note", text.Length == 0 ? (PropertyValue?)null : PropertyValue.Create(text)));
        host.CommitValue();
    }

    void EndDrag()
    {
        mDragEdit?.Dispose();
        mDragEdit = null;
    }

    PropertyValue? mDraft;   // 写给宿主的草稿（自己记一份来画）；提交后留着它，直到按新声明换实例
    int mHover = -1;
    int mDragging = -1;
    IDisposable? mDragEdit;   // 拖动期间的编辑作用域（按下时 EditScope、松开或 Esc 时结束）
}
