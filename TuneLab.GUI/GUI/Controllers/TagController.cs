using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;
using Point = Avalonia.Point;

namespace TuneLab.GUI.Controllers;

// tag 组（TagConfig）的面板渲染：已选的值排成 chip 横排、自动换行，末尾一个「+」弹候选菜单（与列表 / 键控容器
// 同一个弹层：分组、分隔线、叶子多时可搜索）。交互跟着变长列表走：
//   · 点 chip 选中（Ctrl 切换、Shift 连选，再点唯一选中项即取消）；焦点在组内时 Delete 删选区；
//   · chip 上的 × 删它自己；右键 chip 删它（点在已选中的上则删整组选区）；
//   · 拖 chip 调序（整组拖选区），插入处画一条竖线，松手一次写回。
//
// 值按「整段数组」读写（而非逐元素 token）：tag 的身份就是值本身，增删挪都是对序列的整体变换。
// 多选（MultipleDataPropertyObject）时逐成员读写：显示并集（首个成员的序在前，其余成员独有的追加在后），
// 只部分成员有的 tag 显示为半透明——+ 菜单里仍列着它，点了即补给其余成员；删除按值从每个成员里删；
// 调序只在各成员序列完全相同时允许（否则「挪到第 i 个」对各成员不是一回事）。一次操作扇出全部成员、一次提交。
internal sealed class TagController : Panel
{
    public TagController()
    {
        Focusable = true;   // 点 chip 时接管键盘焦点，Delete 才路由得到这里（见 OnKeyDown）
        Background = Style.INTERFACE.ToBrush();

        mWrap = new WrapPanel { Orientation = Orientation.Horizontal };
        mCaret = new Border
        {
            Width = 2,
            Background = Style.HIGH_LIGHT.ToBrush(),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false,
        };
        var body = new Panel { Margin = new(ArrayControlsFactory.Gutter, 0, ArrayControlsFactory.Gutter, ArrayControlsFactory.TitleGap - ChipGap) };
        body.Children.Add(mWrap);
        body.Children.Add(mCaret);
        Children.Add(body);

        mAddButton = ArrayControlsFactory.MakeTextButton("+", ChipHeight + 4);
        mAddButton.Height = ChipHeight;
        mAddButton.Clicked += OnAddButtonClicked;
        mAddMenu = new DropDownMenu(mAddButton);
        mAddMenu.ItemPicked += item => AddTag((PropertyValue)item.Tag!);
        mAddHost = new Panel { Margin = new(0, 0, ChipGap, ChipGap) };
        mAddHost.Children.Add(mAddButton);
        mAddHost.Children.Add(mAddMenu);
    }

    public void Bind(IDataPropertyObject parent, string key)
    {
        Unbind();
        mParent = parent;
        mKey = key;
        parent.Modified.Subscribe(ScheduleRefresh, s);
    }

    public void Unbind()
    {
        s.DisposeAll();
        mParent = null;
        mSelected.Clear();
        mAnchor = null;
        mView = [];
        mChips.Clear();
        mWrap.Children.Clear();
        mStale = true;   // 下次 Refresh 必重建（视图清空后「与上次相同」的比对不再可靠）
    }

    public void Apply(TagConfig config)
    {
        bool itemsChanged = !config.Items.SequenceEqual(mConfig.Items);
        mConfig = config;
        Refresh(force: itemsChanged);
    }

    // 数据改动经文档 Modified 到达：推迟到当前事件链返回后再重建（可能正处在某个 chip 自己的指针回调里）。
    void ScheduleRefresh()
    {
        if (mRefreshPending)
            return;
        mRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            mRefreshPending = false;
            if (mParent != null)
                Refresh(force: false);
        });
    }

    IReadOnlyList<IDataPropertyObject> Members => mParent switch
    {
        null => [],
        MultipleDataPropertyObject multiple => multiple.Members,
        _ => [mParent],
    };

    PropertyValue DefaultValue => ((IValueConfig)mConfig).DefaultValue;

    List<List<PropertyValue>> ReadMembers() => Members.Select(member => TagModel.Read(member, mKey, DefaultValue)).ToList();

    void Refresh(bool force)
    {
        if (mParent == null || mDrag != null)
            return;

        var members = ReadMembers();
        var view = TagModel.Union(members);
        int memberCount = members.Count;

        // + 菜单：每个成员都已有的值隐藏；全隐藏了就收起 +。
        mAddItems = AddableMenu.Build(mConfig.Items, value => view.Any(v => v.Value.Equals(value) && v.Count == memberCount));
        bool canAdd = AddableMenu.FirstLeaf(mAddItems) != null;

        if (force || mStale || !view.SequenceEqual(mView) || canAdd != mAddHost.IsVisible || memberCount != mMemberCount)
        {
            mStale = false;
            mView = view;
            mMemberCount = memberCount;
            mAddHost.IsVisible = canAdd;
            RebuildChips();
        }

        // 选区随值走：不在了的出选区。
        mSelected.RemoveWhere(value => !mView.Any(v => v.Value.Equals(value)));
        if (mAnchor is { } anchor && !mSelected.Contains(anchor) && !mView.Any(v => v.Value.Equals(anchor)))
            mAnchor = null;
        RefreshSelection();
    }

    void RebuildChips()
    {
        mChips.Clear();
        mWrap.Children.Clear();
        foreach (var (value, count) in mView)
        {
            var text = TagModel.DisplayTextOf(mConfig.Items, value);
            var chip = new Chip(this, value, text ?? TagModel.Literal(value), invalid: text == null, partial: count < mMemberCount);
            mChips.Add(chip);
            mWrap.Children.Add(chip);
        }
        mWrap.Children.Add(mAddHost);
    }

    // ===== 写入：对每个成员的序列做同一个变换，写回去重后的整段，一次提交 =====
    void Write(Func<List<PropertyValue>, List<PropertyValue>> transform)
    {
        if (mParent == null)
            return;
        mParent.BeginMergeNotify();
        foreach (var member in Members)
        {
            var current = TagModel.Read(member, mKey, DefaultValue);
            var next = transform(current);
            var stored = member.GetValue(mKey, PropertyValue.Null);
            PropertyValue nextValue = new PropertyArray(next);
            if (!nextValue.Equals(stored))   // 序列没变也会因去重或物化默认值而写（存值与显示不同时）
                member.SetValue(mKey, nextValue);
        }
        mParent.EndMergeNotify();
        mParent.Commit();
    }

    void AddTag(PropertyValue value) => Write(tags => TagModel.Add(tags, value));

    void RemoveTags(IReadOnlyCollection<PropertyValue> values)
    {
        foreach (var value in values)
            mSelected.Remove(value);
        Write(tags => TagModel.Remove(tags, values));
    }

    void OnAddButtonClicked()
    {
        if (mParent == null || mAddMenu.JustClosed)
            return;
        if (AddableMenu.CountLeaves(mAddItems) == 1)
        {
            AddTag((PropertyValue)AddableMenu.FirstLeaf(mAddItems)!.Tag!);
            return;
        }
        mAddMenu.Open(mAddItems, 0);
    }

    // ===== 选中（同变长列表的元素选中）=====
    void PressChip(Chip chip, KeyModifiers modifiers)
    {
        Focus(NavigationMethod.Pointer);
        mPressedSoleSelected = mSelected.Count == 1 && mSelected.Contains(chip.Value);
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        bool ctrl = (modifiers & KeyModifiers.Control) != 0;
        if (shift && mAnchor is { } anchor)
        {
            int from = mView.FindIndex(v => v.Value.Equals(anchor)), to = mView.FindIndex(v => v.Value.Equals(chip.Value));
            if (from >= 0 && to >= 0)
            {
                mSelected.Clear();
                for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                    mSelected.Add(mView[i].Value);
            }
        }
        else if (ctrl)
        {
            if (!mSelected.Remove(chip.Value))
                mSelected.Add(chip.Value);
            mAnchor = chip.Value;
        }
        else if (!mSelected.Contains(chip.Value))
        {
            mSelected.Clear();
            mSelected.Add(chip.Value);
            mAnchor = chip.Value;
        }
        RefreshSelection();
    }

    void ClickChip(Chip chip, KeyModifiers modifiers)
    {
        if ((modifiers & (KeyModifiers.Shift | KeyModifiers.Control)) != 0)
            return;
        mSelected.Clear();
        if (mPressedSoleSelected)
        {
            mAnchor = null;   // 再点一下唯一选中项 = 取消选中
        }
        else
        {
            mSelected.Add(chip.Value);
            mAnchor = chip.Value;
        }
        RefreshSelection();
    }

    // 右键：点在已选中的上 → 删整组；否则只删它自己。
    void DeleteFromChip(Chip chip)
        => RemoveTags(mSelected.Contains(chip.Value) ? SelectedValues() : [chip.Value]);

    List<PropertyValue> SelectedValues() => mView.Select(v => v.Value).Where(mSelected.Contains).ToList();

    void RefreshSelection()
    {
        foreach (var chip in mChips)
            chip.SetSelected(mSelected.Contains(chip.Value));
    }

    // Delete：焦点在组内且有选中时删整组；在文本框里打字时让路（同列表）。无选中不吃这个键，让它继续冒泡。
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Delete || e.KeyModifiers != KeyModifiers.None || e.IsHandledByTextBox())
            return;
        if (mSelected.Count == 0)
            return;
        RemoveTags(SelectedValues());
        e.Handled = true;
    }

    // ===== 拖拽调序 =====
    // 拖的是选区（按下时已选中被拖的 chip）。插入点按指针所在行、越过哪个 chip 的中线定，画一条竖线；
    // 被拖的 chip 压暗。松手按 MoveBlock 一次写回。各成员序列不同（多选）时不让拖。
    bool BeginDrag(Chip chip)
    {
        if (!mSelected.Contains(chip.Value) || mSelected.Count == mView.Count)
            return false;
        if (!TagModel.SameSequence(ReadMembers()))
            return false;
        mDrag = new DragState(SelectedValues());
        foreach (var c in mChips)
            if (mSelected.Contains(c.Value))
                c.Opacity = 0.4;
        return true;
    }

    void UpdateDrag(Point pointer)
    {
        if (mDrag == null || mChips.Count == 0)
            return;

        // 指针所在行：竖向落在哪行 chip 的上下沿之间（落在行缝 / 范围外取最近的一行）。
        var rows = mChips.GroupBy(c => c.Bounds.Y).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        var row = rows.MinBy(r => Math.Abs(pointer.Y - (r[0].Bounds.Y + r[0].Bounds.Height / 2)))!;

        var before = row.FirstOrDefault(c => c.Bounds.X + c.Bounds.Width / 2 > pointer.X);
        double x;
        if (before != null)
        {
            mDrag.InsertBefore = mChips.IndexOf(before);
            x = before.Bounds.X - ChipGap / 2 - 1;
        }
        else
        {
            var last = row[^1];
            mDrag.InsertBefore = mChips.IndexOf(last) + 1;
            x = last.Bounds.Right + ChipGap / 2 - 1;
        }
        mCaret.Height = row[0].Bounds.Height;
        mCaret.Margin = new(Math.Max(0, x), row[0].Bounds.Y, 0, 0);
        mCaret.IsVisible = true;
    }

    void EndDrag(bool commit)
    {
        if (mDrag is not { } drag)
            return;
        mDrag = null;
        mCaret.IsVisible = false;
        foreach (var c in mChips)
            c.Opacity = c.Partial ? PartialOpacity : 1;
        if (commit && drag.InsertBefore >= 0)
            Write(tags => TagModel.MoveBlock(tags, drag.Moved, drag.InsertBefore));
        Refresh(force: false);
    }

    sealed class DragState(IReadOnlyCollection<PropertyValue> moved)
    {
        public IReadOnlyCollection<PropertyValue> Moved { get; } = moved;
        public int InsertBefore { get; set; } = -1;
    }

    // ===== chip =====
    // 圆角矩形：文字 + 常显的 ×（指到变红，同删除钮）。选中态同列表元素：底色提亮 + 强调色描边。只部分成员有 → 半透明；值不在候选里 → 琥珀色字样 + 说明（同下拉非法值的措辞），
    // 原值不动（同下拉的非法值：怎么生效只有插件知道，宿主不替它删、不替它解释）。
    sealed class Chip : Border
    {
        public PropertyValue Value { get; }
        public bool Partial { get; }

        public Chip(TagController owner, PropertyValue value, string text, bool invalid, bool partial)
        {
            mOwner = owner;
            Value = value;
            Partial = partial;
            Height = ChipHeight;
            CornerRadius = new(4);   // 同侧栏按钮 / 下拉的圆角，与末尾的 + 钮同形
            Margin = new(0, 0, ChipGap, ChipGap);
            Padding = new(10, 0, 4, 0);
            BorderThickness = new(1);
            Opacity = partial ? PartialOpacity : 1;

            var label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = (invalid ? Style.SYNTHESIS_DEGRADED : Style.TEXT_LIGHT).ToBrush(),
            };
            // × 画成两笔线段（不靠字体字形）；外包一块透明底的命中区，指到变红（同删除钮的关闭色）。
            var cross = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M0,0 L6,6 M6,0 L0,6"),
                Stroke = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(),
                StrokeThickness = 1.5,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            mClose = new Border
            {
                Width = 16,
                Height = 16,
                Margin = new(4, 0, 0, 0),
                Background = Brushes.Transparent,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = cross,
            };
            mClose.PointerEntered += (_, _) => cross.Stroke = Style.CLOSE_HOVER.ToBrush();
            mClose.PointerExited += (_, _) => cross.Stroke = Style.LIGHT_WHITE.Opacity(0.6).ToBrush();
            mClose.PointerPressed += (_, e) =>
            {
                e.Handled = true;   // 不当作按下 chip（不改选区、不起拖）
                if (e.GetCurrentPoint(mClose).Properties.IsLeftButtonPressed)
                    owner.RemoveTags([Value]);
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(label);
            content.Children.Add(mClose);
            Child = content;

            if (invalid)
                ToolTip.SetTip(this, string.Format("Stored value \"{0}\" is not among the current tags.\nThe plugin decides how to treat it.".Tr(owner), text));
            else if (partial)
                ToolTip.SetTip(this, "Only some of the selected items have this tag. Pick it from + to add it to all of them.".Tr(owner));

            Paint();
        }

        public void SetSelected(bool selected)
        {
            mSelected = selected;
            Paint();
        }

        void Paint()
        {
            Background = (mSelected ? Style.HIGH_LIGHT.Opacity(0.45) : Style.BUTTON_NORMAL).ToBrush();
            BorderBrush = (mSelected ? Style.HIGH_LIGHT : Colors.Transparent).ToBrush();
        }

        // 按下：左键按选中语义更新选区，越过阈值才算拖（此刻才捕获指针）；不拖就松开 = 单击；右键 = 删除。
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.Handled)
                return;
            var properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsRightButtonPressed)
            {
                e.Handled = true;   // 不让右键再冒泡出面板的右键菜单
                mOwner.DeleteFromChip(this);
                return;
            }
            if (!properties.IsLeftButtonPressed)
                return;
            e.Handled = true;
            mOwner.PressChip(this, e.KeyModifiers);
            mPressed = true;
            mDown = e.GetPosition(mOwner.mWrap);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!mPressed)
                return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                mPressed = false;
                return;
            }
            var position = e.GetPosition(mOwner.mWrap);
            if (!mDragging)
            {
                if (Math.Abs(position.X - mDown.X) < DragThreshold && Math.Abs(position.Y - mDown.Y) < DragThreshold)
                    return;
                if (!mOwner.BeginDrag(this))
                {
                    mPressed = false;
                    return;
                }
                mDragging = true;
                e.Pointer.Capture(this);
            }
            mOwner.UpdateDrag(position);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            bool wasClick = mPressed && !mDragging;
            mPressed = false;
            if (wasClick)
            {
                mOwner.ClickChip(this, e.KeyModifiers);
                return;
            }
            if (!mDragging)
                return;
            mDragging = false;
            e.Pointer.Capture(null);   // 先清标志再释放：释放触发的 CaptureLost 不会被当成取消
            mOwner.EndDrag(commit: true);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            mPressed = false;
            if (!mDragging)
                return;
            mDragging = false;
            mOwner.EndDrag(commit: false);   // 捕获丢失：不提交
        }

        readonly TagController mOwner;
        readonly Border mClose;
        bool mSelected, mPressed, mDragging;
        Point mDown;
    }

    const double ChipHeight = 24;
    const double ChipGap = 6;
    const double PartialOpacity = 0.5;
    const double DragThreshold = 4;

    readonly WrapPanel mWrap;
    readonly Border mCaret;
    readonly Button mAddButton;
    readonly DropDownMenu mAddMenu;
    readonly Panel mAddHost;
    readonly List<Chip> mChips = [];
    readonly HashSet<PropertyValue> mSelected = [];
    readonly DisposableManager s = new();
    IDataPropertyObject? mParent;
    string mKey = string.Empty;
    TagConfig mConfig = TagConfig.Create([]);
    List<(PropertyValue Value, int Count)> mView = [];
    int mMemberCount;
    IReadOnlyList<DropDownItem> mAddItems = [];
    PropertyValue? mAnchor;
    bool mPressedSoleSelected;
    bool mRefreshPending;
    bool mStale = true;
    DragState? mDrag;
}
