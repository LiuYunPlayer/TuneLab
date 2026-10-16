using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Animation;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.GUI.Components;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;
using CheckBox = TuneLab.GUI.Components.CheckBox;
using IDataObject = TuneLab.Foundation.IDataObject;   // Avalonia.Input 也有同名类型

namespace TuneLab.GUI.Controllers;

// 有序可重复列表（PropertyArray）的面板渲染。数组「字段」本身由父 PropertyObjectController 的 Creator 带 key 标题；
// 字段内的元素行走本控制器——元素无 key 标签，行布局见 ElementRow（序号条 + 控件，可删时浮出删除钮），不复用对象字段「标题 + 分隔符」的排布。
// 元素以稳定 token 寻址（IDataPropertyArray 把 token 当 key），按 token keyed-diff：同 token 复用行（拖动中的 slider /
// 编辑中的文本框不被销毁），中插/删只增删一行。config = f(数据) 随数据变化重算，由面板级 reconcile 在每次 commit 后驱动。
internal abstract class ArrayControllerBase : StackPanel, IElementRowHost
{
    // 按钮作用域（见 ButtonRoot）：由外层 creator 在 Apply 前设好；元素里的对象按 "路径/元素 token" 继续往下传。
    // 未设（独立使用）时元素里的按钮各自成树。
    internal ButtonRoot? ButtonRoot { get; set; }
    internal string ButtonPath { get; set; } = string.Empty;

    protected ArrayControllerBase()
    {
        Orientation = Orientation.Vertical;
        Background = Style.INTERFACE.ToBrush();
        Children.Add(mRowsPanel);
        SetTrailingGap(true);
        mDragAnimation = new RowDragAnimation(this);
        Focusable = true;   // 点序号条时接管键盘焦点，Delete 才路由得到这里（见 OnKeyDown）
    }

    // 收尾留白：+ 按钮在时由它的上下边距撑开字段底部；没有 + 时（定长数组 / 列表达上限隐藏 +）末行会直接贴住
    // 下一个字段的分隔线，故补上与普通字段控件同样的底边距。
    protected void SetTrailingGap(bool needed) => mRowsPanel.Margin = new(0, 0, 0, needed ? TrailingGap : 0);

    internal const double TrailingGap = 12;

    // 绑定到（新的）数组数据外观；切换数据时清空旧行（复用的 widget 仍绑在旧 token 上）。
    public void Bind(IDataPropertyArray array)
    {
        ResetRows();
        mArray = array;
    }

    public void Unbind()
    {
        ResetRows();
        mArray = null;
    }

    protected virtual bool Deletable => false;

    protected IDataPropertyArray Array => mArray ?? throw new InvalidOperationException("ArrayController has no data object.");

    // 把当前展示的 seed（越界虚拟行）物化为真实元素：数组从现长补齐到声明数、各取 element config 默认值。
    // 任何结构操作（+ / 删除）前先调用——否则只动一个元素会令插件因 present 而不再 reseed、其余 seed 行塌掉。
    protected void MaterializeSeed()
    {
        if (mArray == null)
            return;
        for (int i = mArray.Count; i < mElements.Count; i++)
            mArray.Add(mElements[i].GetDefaultValue());
    }

    // 删除某行：先物化整段 seed（保 presence、其余行不塌），再按身份删该元素、提交（成一个撤销单元）。
    // 真实行用 token（跨重排仍指向同一元素）；seed 行无 token，物化后用其位置。
    void RemoveRow(int position, string? token)
    {
        if (mArray == null)
            return;

        MaterializeSeed();
        int index = token != null ? mArray.Tokens.IndexOf(token) : position;
        if (index < 0 || index >= mArray.Count)
            return;

        mArray.RemoveAt(index);
        mArray.Commit();
    }

    // 行的删除回调（Deletable 时非 null）：真实行按 token、seed 行按位置。
    protected Action? DeleteCallback(int position, string? token)
        => Deletable ? () => RemoveRow(position, token) : null;

    // 元素行 keyed-diff 对齐到 elements.Count 行（镜像 PropertyObjectController.Reconcile）：
    //   现存位（i < 数据元素数）绑真实 token——身份稳定，中插/删只增删一行；
    //   越界（seed）位（i ≥ 数据元素数）绑 SeedPositionView——读默认、首次写物化整段 seed（presence 语义）。
    // 同 key 同类型复用行仅更参数、key 消失则 dispose、新 key 建、同 key 换类型则换行；仅结构变化时重排布局。
    // 复合 seed 元素物化前不渲染虚拟行（仅标量 seed 虚拟绑定，见 SeedPositionView）。
    protected void ReconcileRows(IReadOnlyList<IControllerConfig> elements)
    {
        if (mArray == null)
            return;

        mElements = elements;

        // 多选复合数组：注入各元素默认值，供「缺位=默认」读取 + 「编辑补齐」填短成员（单选真实数组无此需求）。
        if (mArray is MultipleDataPropertyArray multiArray)
            multiArray.SetElementDefaults(elements.Select(e => e.GetDefaultValue()).ToList());

        var tokens = mArray.Tokens;
        int dataCount = tokens.Count;
        IReadOnlyList<PropertyValue>? seedDefaults = null;

        var nextOrder = new List<string>();
        var nextByKey = new Dictionary<string, ElementRow>();
        bool structureChanged = false;

        for (int i = 0; i < elements.Count; i++)
        {
            var cfg = elements[i];
            string key;
            IDataPropertyObject host;
            Action? onDelete;

            if (i < dataCount)
            {
                key = tokens[i];
                host = mArray;
                onDelete = DeleteCallback(i, tokens[i]);   // 真实行：按 token 删
            }
            else
            {
                if (cfg is not IValueConfig)
                    continue; // 复合 seed 元素：物化前不渲染
                key = SeedKeyPrefix + i;
                seedDefaults ??= elements.Select(e => e.GetDefaultValue()).ToList();
                host = new SeedPositionView(mArray, i, seedDefaults);
                onDelete = DeleteCallback(i, null);        // seed 行：删除前物化、按位置删
            }

            if (nextByKey.ContainsKey(key))
                continue; // key 本就唯一；防御

            ElementRow row;
            if (mRowsByKey.TryGetValue(key, out var existing) && existing.ConfigType == cfg.GetType())
            {
                existing.Update(cfg);
                mRowsByKey.Remove(key);
                row = existing;
            }
            else
            {
                row = new ElementRow(host, key, cfg, onDelete, Deletable ? this : null, ButtonRoot, ButtonPath + "/" + key);
                structureChanged = true; // 新建（含同 key 换类型 / seed 位物化为真实位）
            }
            row.SetPosition(i);   // 序号 = 元素位置 + 1，增删/重排后随位置刷新
            nextByKey.Add(key, row);
            nextOrder.Add(key);
        }

        // 选中随行身份走：消失的行出选区；锚点消失则作废。
        mSelected.IntersectWith(nextOrder);
        if (mAnchor != null && !nextByKey.ContainsKey(mAnchor))
            mAnchor = null;
        foreach (var kvp in nextByKey)
            kvp.Value.SetSelected(mSelected.Contains(kvp.Key));

        if (mRowsByKey.Count > 0)
            structureChanged = true; // 有 key 被删
        if (!mOrder.SequenceEqual(nextOrder))
            structureChanged = true; // 顺序变

        if (structureChanged)
        {
            foreach (var kvp in mRowsByKey)
            {
                mRowsPanel.Children.Remove(kvp.Value.Root);
                kvp.Value.Dispose();
            }
            mRowsByKey = nextByKey;
            mOrder = nextOrder;
            AlignRows();
        }
        else
        {
            mRowsByKey = nextByKey;
            mOrder = nextOrder;
        }
    }

    // 虚拟（seed）行键前缀：以 '@' 起头，与真实元素 token（"e0"、"e1"…）绝不撞。
    const string SeedKeyPrefix = "@seed:";

    // 增量对齐 mRowsPanel.Children 到 mOrder：复用行留原位（不失焦/不打断拖动），仅移动错位的、插入新建的、移除尾部多余。
    void AlignRows()
    {
        int index = 0;
        foreach (var token in mOrder)
        {
            var view = mRowsByKey[token].Root;
            int current = mRowsPanel.Children.IndexOf(view);
            if (current == index)
            {
                index++;
                continue;
            }
            if (current >= 0)
                mRowsPanel.Children.RemoveAt(current);
            mRowsPanel.Children.Insert(index, view);
            index++;
        }
        while (mRowsPanel.Children.Count > index)
            mRowsPanel.Children.RemoveAt(mRowsPanel.Children.Count - 1);
    }

    void ResetRows()
    {
        StopRowDrag();
        mSelected.Clear();   // 换绑数据（如换选音符）= 换了一批元素，旧选区无意义
        mAnchor = null;
        foreach (var kvp in mRowsByKey)
            kvp.Value.Dispose();
        mRowsByKey = new();
        mOrder = new();
        mRowsPanel.Children.Clear();
    }

    // ===== 元素选中（仿轨道头）=====
    // 点序号条选中：按下未选中的 → 独占选中它；按下已选中的 → 保留选区（便于整组拖）；不拖就松开的平地单击
    // 收敛为只选它——但若按下时它就是唯一选中项，单击 = 取消选中（再点一下即取消）。Ctrl 切换单个、Shift 从锚点连选。选区是纯界面状态：不进撤销，换绑数据即清空，
    // 行消失即出选区（见 ReconcileRows）。选中后：拖动整组、右键已选中的序号条删整组、焦点在列表内时 Delete 删整组。

    readonly HashSet<string> mSelected = new();
    string? mAnchor;
    bool mPressedSoleSelected;

    void IElementRowHost.PressRow(ElementRow row, KeyModifiers modifiers)
    {
        Focus(NavigationMethod.Pointer);   // 接管键盘焦点，Delete 才路由到这里；Pointer 方式不画焦点框
        // 按下时记一笔「它是不是唯一选中项」：单击松开时据此取消选中（按下时不动选区，以免拖它时先被取消）。
        mPressedSoleSelected = mSelected.Count == 1 && mSelected.Contains(row.Key);
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        bool ctrl = (modifiers & KeyModifiers.Control) != 0;
        if (shift && mAnchor != null)
        {
            int from = mOrder.IndexOf(mAnchor), to = mOrder.IndexOf(row.Key);
            if (from >= 0 && to >= 0)
            {
                mSelected.Clear();
                for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                    mSelected.Add(mOrder[i]);
            }
        }
        else if (ctrl)
        {
            if (!mSelected.Remove(row.Key))
                mSelected.Add(row.Key);
            mAnchor = row.Key;
        }
        else if (!mSelected.Contains(row.Key))
        {
            mSelected.Clear();
            mSelected.Add(row.Key);
            mAnchor = row.Key;
        }
        RefreshSelection();
    }

    void IElementRowHost.ClickRow(ElementRow row, KeyModifiers modifiers)
    {
        if ((modifiers & (KeyModifiers.Shift | KeyModifiers.Control)) != 0)
            return;   // 增减选已在按下时做完
        mSelected.Clear();
        if (mPressedSoleSelected)
        {
            mAnchor = null;   // 再点一下唯一选中项 = 取消选中
        }
        else
        {
            mSelected.Add(row.Key);
            mAnchor = row.Key;
        }
        RefreshSelection();
    }

    // 右键序号条：点在已选中的上 → 删整组；否则只删它自己（不动选区里的别人）。
    void IElementRowHost.DeleteFromStrip(ElementRow row)
    {
        if (mSelected.Contains(row.Key))
            DeleteRows(SelectedRows());
        else
            DeleteRows([row]);
    }

    // Delete：焦点在列表内任意处（序号条 / 元素里的非文本控件）且有选中时删整组。正在文本框里打字时让路——
    // 文本框自己处理 Delete（删字符）且通常已置 Handled、走不到这里；此处再按「焦点在不在文本控件里」判一次，
    // 与 Editor / 窗口层的快捷键分发同一判据（IsHandledByTextBox，含多行编辑器）。
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Delete || e.KeyModifiers != KeyModifiers.None || e.IsHandledByTextBox())
            return;
        if (!Deletable || mSelected.Count == 0)
            return;   // 无选中：不吃这个键，让它继续冒泡（外层列表 / 编辑器自有处理）
        DeleteRows(SelectedRows());
        e.Handled = true;
    }

    List<ElementRow> SelectedRows() => mOrder.Where(mSelected.Contains).Select(key => mRowsByKey[key]).ToList();

    void RefreshSelection()
    {
        foreach (var kvp in mRowsByKey)
            kvp.Value.SetSelected(mSelected.Contains(kvp.Key));
    }

    // 删若干行：先物化 seed（同单删），再按数据位置从后往前删——前面的位置不受后面删除的影响；一次提交 = 一个撤销单元。
    void DeleteRows(IReadOnlyCollection<ElementRow> rows)
    {
        if (mArray == null || rows.Count == 0)
            return;
        var positions = rows.Select(r => r.Position).OrderByDescending(p => p).ToList();
        foreach (var row in rows)
            mSelected.Remove(row.Key);
        MaterializeSeed();
        foreach (var position in positions)
            if (position < mArray.Count)
                mArray.RemoveAt(position);
        mArray.Commit();
    }

    // ===== 拖序号条重排（仿轨道头拖拽，支持整组）=====
    // 拖的是选区（按下时已选中被拖行，见 PressRow）：被拖各行平移同一像素量、彼此相对次序与间隔不变；
    // 落点按整组前沿越过了几个未选中行的中线定（往下看组的下沿、往上看上沿），即「每个被拖行的新位置 =
    // 原位置 + 同一偏移」，同轨道头的多选拖拽。其余行按新排布的实际位置让位（元素高矮不一，按实测高度算），
    // 以指数缓动滑过去（时间常数同轨道头）。全程只动 RenderTransform、不动布局；松手才一次性挪数据 + Commit
    //（一个撤销单元），随后 reconcile 按新顺序排布、变换归零。
    // 定长数组不让拖（位置本身有含义，如 pair 的低/高）；多选且各成员不等长时「第 i 个元素」各指各的，也不让拖。

    // 能否重排：变长列表才可。
    protected virtual bool Reorderable => false;

    bool IElementRowHost.BeginRowDrag(ElementRow row)
    {
        if (!Reorderable || mArray == null || mArray is MultipleDataPropertyArray { HasUniformLength: false })
            return false;
        if (!mSelected.Contains(row.Key))
            return false;   // Ctrl 按下时把它切出了选区：这次按下不是在拖它

        var rows = mOrder.Select(key => mRowsByKey[key]).ToList();
        var dragged = Enumerable.Range(0, rows.Count).Where(i => mSelected.Contains(rows[i].Key)).ToArray();
        if (dragged.Length == 0 || dragged.Length == rows.Count)
            return false;   // 全选了：无处可挪

        mDrag = new RowDrag(rows, dragged);
        foreach (var i in dragged)
            rows[i].Root.ZIndex = 1;   // 浮在让位的兄弟之上
        AnimationManager.SharedManager.StartAnimation(mDragAnimation);
        return true;
    }

    void IElementRowHost.UpdateRowDrag(double pointerDelta) => mDrag?.Update(pointerDelta);

    void IElementRowHost.EndRowDrag()
    {
        if (mDrag is not { } drag)
            return;
        StopRowDrag();
        if (drag.Delta == 0 || mArray == null)
            return;

        // 序号条里存的是数据位置。按最终排布逐槽把应在那里的元素挪过去（从前往后填，已填好的槽不再被动）；
        // seed 行先物化（与 + / 删除同理）。
        var slots = drag.Rows.Select(r => r.Position).ToList();               // 这些行所占的数据位置（升序）
        var desired = drag.FinalOrder().Select(i => drag.Rows[i].Position).ToList();
        MaterializeSeed();
        var current = Enumerable.Range(0, mArray.Count).ToList();               // 模拟：current[k] = 此刻在 k 处的原位置
        for (int k = 0; k < slots.Count; k++)
        {
            int from = current.IndexOf(desired[k]);
            int to = slots[k];
            if (from == to)
                continue;
            mArray.Move(from, to);
            current.RemoveAt(from);
            current.Insert(to, desired[k]);
        }

        // 选区跟着挪过去的元素走（多选外观的 token 是位置串，故按新位置取 token 统一处理）。
        var tokens = mArray.Tokens;
        mSelected.Clear();
        foreach (var i in drag.Dragged)
        {
            int slot = System.Array.IndexOf(drag.FinalOrder(), i);
            mSelected.Add(tokens[slots[slot]]);
        }
        mAnchor = null;
        mArray.Commit();
    }

    void IElementRowHost.CancelRowDrag() => StopRowDrag();

    void StopRowDrag()
    {
        if (mDrag is not { } drag)
            return;
        mDrag = null;
        AnimationManager.SharedManager.StopAnimation(mDragAnimation);
        drag.Reset();
    }

    sealed class RowDrag
    {
        public IReadOnlyList<ElementRow> Rows { get; }
        public int[] Dragged { get; }   // 被拖行下标（升序）
        public int Delta { get; private set; }

        public RowDrag(IReadOnlyList<ElementRow> rows, int[] dragged)
        {
            Rows = rows;
            Dragged = dragged;
            mDraggedSet = new HashSet<int>(dragged);
            mTops = rows.Select(r => r.Root.Bounds.Y).ToArray();
            mHeights = rows.Select(r => r.Root.Bounds.Height).ToArray();
            mOffsets = new double[rows.Count];
            mTargets = new double[rows.Count];
            mFinalOrder = Enumerable.Range(0, rows.Count).ToArray();
        }

        // 新排布：mFinalOrder[槽] = 落在该槽的行下标。被拖行占 原位置+Delta，其余行按原次序填剩下的槽。
        public int[] FinalOrder() => mFinalOrder;

        // pointerDelta：指针相对按下处的纵向位移。整组跟手但不越出列表首尾。
        public void Update(double pointerDelta)
        {
            double blockTop = mTops[Dragged[0]];
            double blockBottom = mTops[Dragged[^1]] + mHeights[Dragged[^1]];
            double listBottom = mTops[^1] + mHeights[^1];
            double offset = Math.Clamp(pointerDelta, mTops[0] - blockTop, listBottom - blockBottom);
            foreach (var i in Dragged)
                Translate(Rows[i], offset);

            // 偏移量（以「越过几个未选中行」计）：前沿越过其中线即算越过。用前沿而非中线——整组被夹在列表首尾内，
            // 中线最远只到末行中线（等高恰好压线、比末行高还够不着），拖到底会挤不掉最后一个。
            int delta = 0;
            if (offset > 0)
            {
                for (int j = Dragged[^1] + 1; j < Rows.Count; j++)
                    if (!mDraggedSet.Contains(j) && blockBottom + offset > mTops[j] + mHeights[j] / 2)
                        delta++;
            }
            else if (offset < 0)
            {
                for (int j = Dragged[0] - 1; j >= 0; j--)
                    if (!mDraggedSet.Contains(j) && blockTop + offset < mTops[j] + mHeights[j] / 2)
                        delta--;
            }
            Delta = delta;

            // 排布：被拖行占 i+Delta 的槽，其余行按原次序填空槽。
            var order = new int[Rows.Count];
            var occupied = new bool[Rows.Count];
            foreach (var i in Dragged)
            {
                order[i + delta] = i;
                occupied[i + delta] = true;
            }
            int slot = 0;
            for (int j = 0; j < Rows.Count; j++)
            {
                if (mDraggedSet.Contains(j))
                    continue;
                while (occupied[slot])
                    slot++;
                order[slot++] = j;
            }
            mFinalOrder = order;

            // 未被拖的行的目标位移 = 新排布下的顶 − 原顶。
            double top = mTops[0];
            foreach (var j in order)
            {
                mTargets[j] = mDraggedSet.Contains(j) ? 0 : top - mTops[j];
                top += mHeights[j];
            }
        }

        // 让位缓动：各未被拖行的位移指数逼近目标位移。
        public void Tick(double millisec)
        {
            double dt = double.IsNaN(mLastTickMs) ? FrameMs : Math.Max(0, millisec - mLastTickMs);
            mLastTickMs = millisec;
            double factor = 1 - Math.Exp(-dt / SmoothTau);
            for (int j = 0; j < Rows.Count; j++)
            {
                if (mDraggedSet.Contains(j))
                    continue;
                double next = mOffsets[j] + (mTargets[j] - mOffsets[j]) * factor;
                if (Math.Abs(next - mTargets[j]) < 0.5)
                    next = mTargets[j];
                if (next != mOffsets[j])
                {
                    mOffsets[j] = next;
                    Translate(Rows[j], next);
                }
            }
        }

        public void Reset()
        {
            foreach (var row in Rows)
            {
                row.Root.RenderTransform = null;
                row.Root.ZIndex = 0;
            }
        }

        static void Translate(ElementRow row, double y) => row.Root.RenderTransform = new TranslateTransform(0, y);

        const double SmoothTau = 45;   // 让位缓动时间常数（ms），同轨道头
        const double FrameMs = 16;
        readonly HashSet<int> mDraggedSet;
        readonly double[] mTops;
        readonly double[] mHeights;
        readonly double[] mOffsets;
        readonly double[] mTargets;
        int[] mFinalOrder;
        double mLastTickMs = double.NaN;
    }

    sealed class RowDragAnimation(ArrayControllerBase owner) : IAnimation
    {
        public void Update(double millisec) => owner.mDrag?.Tick(millisec);
    }

    RowDrag? mDrag;

    readonly RowDragAnimation mDragAnimation;
    protected IDataPropertyArray? mArray;
    IReadOnlyList<IControllerConfig> mElements = [];   // 当前各元素 config（用于 seed 物化默认值）
    readonly StackPanel mRowsPanel = new() { Orientation = Orientation.Vertical };
    Dictionary<string, ElementRow> mRowsByKey = new();
    List<string> mOrder = new();
}

// 定长数组：长度 = config 声明数，逐位渲染、不可增删；第 i 位由 Elements[i] 渲染并绑定到 token[i]。
// 数据应有 Elements.Count 个元素（由「恢复默认」walker 递归 seed）；若短则渲染现有位、不在浏览时写入数据（保 presence）。
internal sealed class ArrayController : ArrayControllerBase
{
    public void Apply(ArrayConfig config) => ReconcileRows(config.Elements);
}

// 变长列表：行可删（悬浮显删除钮）、底部 + 按钮添加。AddableElements 只有一个可添加叶子时直加该类型默认值，
// 否则弹共用菜单（DropDownMenu：分组为子菜单、叶子多时可搜索）按 Label 选。
internal sealed class ListController : ArrayControllerBase
{
    public ListController()
    {
        mAddButton = ArrayControlsFactory.MakeTextButton("+", 0);
        mAddButton.Height = 30;
        mAddButton.Margin = new(24, 8);
        mAddButton.Clicked += OnAddButtonClicked;
        mAddMenu = new DropDownMenu(mAddButton);
        mAddMenu.ItemPicked += item => AddElement((AddableElement)item.Tag!);
        mAddHost = new Panel();
        mAddHost.Children.Add(mAddButton);
        mAddHost.Children.Add(mAddMenu);
        Children.Add(mAddHost); // 置于 mRowsPanel 之后
    }

    protected override bool Deletable => true;
    protected override bool Reorderable => true;

    public void Apply(ListConfig config)
    {
        mAddableItems = AddableMenu.Build(config.AddableElements);
        ReconcileRows(config.Elements);
        mAddHost.IsVisible = AddableMenu.FirstLeaf(mAddableItems) != null; // 无可添加叶子（如达上限）→ 隐藏 +
        SetTrailingGap(!mAddHost.IsVisible);
    }

    void OnAddButtonClicked()
    {
        if (mArray == null || mAddMenu.JustClosed)
            return;

        if (AddableMenu.CountLeaves(mAddableItems) == 1)
        {
            AddElement((AddableElement)AddableMenu.FirstLeaf(mAddableItems)!.Tag!);
            return;
        }

        mAddMenu.Open(mAddableItems, 0);
    }

    void AddElement(AddableElement addable)
    {
        MaterializeSeed();   // 先把当前展示的 seed 行物化为真实元素，再追加——否则其余 seed 行会塌掉
        Array.Add(addable.Template.GetDefaultValue());
        Array.Commit();
    }

    readonly Button mAddButton;
    readonly DropDownMenu mAddMenu;
    readonly Panel mAddHost;
    IReadOnlyList<DropDownItem> mAddableItems = [];
}

// 单个元素行，仿轨道头：左侧序号条贴满元素上下沿、数字居中，元素之间一条 1px 分隔线，首个元素头顶也有一条，
// 整组上下封口——复合元素（对象/嵌套列表）占好几行，整条高的序号条把「这一整块是一个元素」框出来。
// 可删时（rowHost 非 null，即变长列表）序号条是元素的把手：左键选中 / 拖动重排、右键删除，统筹在宿主控制器
//（见 ArrayControllerBase 的选中与拖拽段）。另有悬浮元素时右侧外浮出的删除钮（见 FloatingDelete），只删本元素——
// 给初见用户的直观入口；右键序号条是熟手的快捷手段：删完下一个上移到原位，指针不动连点即可连删（撤销兜底，不弹确认）。
// 选中态同轨道头：整行底色提亮 + 右缘 3px 强调条（叠在上层、不占布局，选中与否不抖动）。
sealed class ElementRow : IDisposable
{
    public Control Root => mRoot;
    public Type ConfigType => mWidget.ConfigType;

    // 行身份（真实元素的 token / seed 位的虚拟键），选区按它记。
    public string Key { get; }

    // 数据位置（0 起）：序号 = Position + 1；删除 / 拖拽松手时按它告诉数组动哪个。
    public int Position { get; private set; }

    public ElementRow(IDataPropertyObject host, string bindKey, IControllerConfig config, Action? onDelete, IElementRowHost? rowHost, ButtonRoot? buttonRoot, string buttonPath)
    {
        Key = bindKey;
        // host = 现存位的数组外观（bindKey = token）或越界位的 SeedPositionView（bindKey 仅作行身份、视图按 position 寻址）。
        mWidget = ElementWidget.Create(host, bindKey, config, buttonRoot, buttonPath);

        mIndexText = new TextBlock
        {
            FontSize = 12,
            Foreground = Style.TEXT_LIGHT.ToBrush(),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        // 序号条取音素标签同色（HIGH_LIGHT），白字。
        var index = new Border { Width = ArrayControlsFactory.StripWidth, Background = Style.HIGH_LIGHT.ToBrush(), Child = mIndexText };
        if (rowHost != null)
            AttachHandle(index, rowHost);
        mWidget.View.Margin = new(8, 6, 0, 6);
        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(index, Avalonia.Controls.Dock.Left);
        body.Children.Add(index);
        body.Children.Add(mWidget.View);

        // 分隔线是实线控件（颜色同字段间分隔线；只留缝露出的是面板底色，看不出来）。下沿每个元素都有；
        // 顶线只给首个元素（SetPosition 时定），使整组上下封口。
        mTopLine = ArrayControlsFactory.MakeLine();
        var bottomLine = ArrayControlsFactory.MakeLine();
        mElement = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(mTopLine, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(bottomLine, Avalonia.Controls.Dock.Bottom);
        mElement.Children.Add(mTopLine);
        mElement.Children.Add(bottomLine);
        mElement.Children.Add(body);

        mSelectionStrip = new Border
        {
            Width = 3,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Background = Style.HIGH_LIGHT.ToBrush(),
            IsHitTestVisible = false,
            IsVisible = false,
        };

        // 透明底：元素内空白处也要收得到悬浮，删除钮才浮得出来。
        mRoot = new Panel { Margin = new(ArrayControlsFactory.Gutter, 0), Background = Brushes.Transparent };
        mRoot.Children.Add(mElement);
        mRoot.Children.Add(mSelectionStrip);
        if (onDelete != null)
            mDelete = new FloatingDelete(mRoot, mElement, ArrayControlsFactory.DeleteOffset, onDelete);
    }

    public void SetPosition(int position)
    {
        Position = position;
        mIndexText.Text = (position + 1).ToString();
        mTopLine.IsVisible = position == 0;
    }

    public void SetSelected(bool selected)
    {
        mElement.Background = selected ? Style.HIGH_LIGHT.Opacity(0.18).ToBrush() : null;
        mSelectionStrip.IsVisible = selected;
    }

    // 序号条把手：左键按下即按选中语义更新选区（见 PressRow），越过阈值才算拖（并此刻才捕获指针），
    // 不拖就松开 = 单击（收敛选区，见 ClickRow）；右键 = 删除。位移按行所在列表面板的坐标量——拖动中行自身在平移，量自己会自激。
    void AttachHandle(Border strip, IElementRowHost rowHost)
    {
        bool pressed = false, dragging = false;
        double downY = 0;

        double PanelY(PointerEventArgs e)
            => e.GetPosition(mRoot.Parent as Visual ?? mRoot).Y;

        strip.PointerPressed += (_, e) =>
        {
            var properties = e.GetCurrentPoint(strip).Properties;
            if (properties.IsRightButtonPressed)
            {
                e.Handled = true;   // 不让右键再冒泡出面板的右键菜单
                rowHost.DeleteFromStrip(this);
                return;
            }
            if (!properties.IsLeftButtonPressed)
                return;
            e.Handled = true;
            rowHost.PressRow(this, e.KeyModifiers);
            pressed = true;
            downY = PanelY(e);
        };
        strip.PointerMoved += (_, e) =>
        {
            if (!pressed)
                return;
            if (!e.GetCurrentPoint(strip).Properties.IsLeftButtonPressed)
            {
                pressed = false;   // 按键已在别处松开，标志滞留——复位，免得悬空起拖
                return;
            }
            double y = PanelY(e);
            if (!dragging)
            {
                if (Math.Abs(y - downY) < DragThreshold)
                    return;
                if (!rowHost.BeginRowDrag(this))
                {
                    pressed = false;
                    return;
                }
                dragging = true;
                mDelete?.SetSuppressed(true);
                e.Pointer.Capture(strip);
            }
            rowHost.UpdateRowDrag(y - downY);
        };
        strip.PointerReleased += (_, e) =>
        {
            bool wasClick = pressed && !dragging;
            pressed = false;
            if (wasClick)
            {
                rowHost.ClickRow(this, e.KeyModifiers);
                return;
            }
            if (!dragging)
                return;
            dragging = false;
            mDelete?.SetSuppressed(false);
            e.Pointer.Capture(null);   // 先清标志再释放：释放触发的 CaptureLost 不会被当成取消
            rowHost.EndRowDrag();
        };
        strip.PointerCaptureLost += (_, _) =>
        {
            pressed = false;
            if (!dragging)
                return;
            dragging = false;
            mDelete?.SetSuppressed(false);
            rowHost.CancelRowDrag();   // 捕获丢失：回弹、不提交
        };
    }

    const double DragThreshold = 4;

    public void Update(IControllerConfig config) => mWidget.Update(config);

    public void Dispose()
    {
        mDelete?.Dispose();
        mWidget.Dispose();
    }

    readonly Panel mRoot;
    readonly DockPanel mElement;
    readonly TextBlock mIndexText;
    readonly Border mTopLine;
    readonly Border mSelectionStrip;
    readonly FloatingDelete? mDelete;
    readonly ElementWidget mWidget;
}

// 浮出式删除钮，仿 tooltip：悬浮到所在行时，在锚点右侧外浮出一枚 ×——Popup 是独立浮层，
// 可越出面板乃至窗口，故不必在面板里给它留位置，也不遮控件。平时灰底白叉，指到它红底（CLOSE_HOVER），仿窗口关闭键；
// 内置 Button 的 Clicked 在抬起且指针仍在键内才触发，按下后拖走可取消。
// 显隐由「行被悬浮」「钮被悬浮」两个状态的或决定，而非按事件先后：钮是另一个顶层窗口，指针从行移到钮上时
// 「钮进入」与「行离开」到达的先后不定——若按事件顺序处理，「行离开」晚到就会在指针下把钮收掉，指针落回行上
// 又弹出，循环闪烁（钮与行有重叠时必现，如横贯全宽的键控条目）。两者皆否才收，且宽限一小段：
// 指针从行移向钮途中会短暂两边都不在。
sealed class FloatingDelete : IDisposable
{
    public FloatingDelete(Panel row, Control anchor, double horizontalOffset, Action onDelete)
    {
        var button = new Button { Width = ArrayControlsFactory.DeleteSize, Height = ArrayControlsFactory.DeleteSize }
            .AddContent(new()
            {
                Item = new BorderItem { CornerRadius = 4 },
                ColorSet = new() { Color = Style.BACK, HoveredColor = Style.CLOSE_HOVER, PressedColor = Style.CLOSE_PRESSED },
            })
            .AddContent(new()
            {
                Item = new IconItem { Icon = Assets.WindowClose },
                ColorSet = new() { Color = Style.LIGHT_WHITE, HoveredColor = Style.WHITE, PressedColor = Style.WHITE },
            });
        button.Clicked += () => { Hide(); onDelete(); };

        // Popup 挂进行的子树（零尺寸），随行一起进出视觉树。Placement.Right：贴锚点右沿、竖向居中。
        mPopup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Right,
            HorizontalOffset = horizontalOffset,
            IsLightDismissEnabled = false,
            Child = button,
        };
        row.Children.Add(mPopup);

        row.PointerEntered += (_, _) => { mRowHovered = true; Refresh(); };
        row.PointerExited += (_, _) => { mRowHovered = false; Refresh(); };
        button.PointerEntered += (_, _) => { mButtonHovered = true; Refresh(); };
        button.PointerExited += (_, _) => { mButtonHovered = false; Refresh(); };
        row.DetachedFromVisualTree += (_, _) => { mRowHovered = mButtonHovered = false; Hide(); };
    }

    void Refresh()
    {
        if (mRowHovered || mButtonHovered)
            Show();
        else
            HideSoon();
    }

    // 压制：拖拽期间不浮出（被拖的行在平移，钮会悬在原处）。
    public void SetSuppressed(bool suppressed)
    {
        mSuppressed = suppressed;
        if (suppressed)
            Hide();
    }

    void Show()
    {
        CancelPendingHide();
        if (!mSuppressed)
            mPopup.IsOpen = true;
    }

    void HideSoon()
    {
        CancelPendingHide();
        mPendingHide = DispatcherTimer.RunOnce(Hide, TimeSpan.FromMilliseconds(150));
    }

    void Hide()
    {
        CancelPendingHide();
        mPopup.IsOpen = false;
    }

    void CancelPendingHide()
    {
        mPendingHide?.Dispose();
        mPendingHide = null;
    }

    public void Dispose() => Hide();

    readonly Popup mPopup;
    IDisposable? mPendingHide;
    bool mSuppressed;
    bool mRowHovered;
    bool mButtonHovered;
}

// 元素行把序号条上的操作上报给所在列表控制器：选中（按下 / 单击）、右键删除、拖拽（开始可拒绝，如多选不等长）。
interface IElementRowHost
{
    void PressRow(ElementRow row, KeyModifiers modifiers);
    void ClickRow(ElementRow row, KeyModifiers modifiers);
    void DeleteFromStrip(ElementRow row);
    bool BeginRowDrag(ElementRow row);
    void UpdateRowDrag(double pointerDelta);
    void EndRowDrag();
    void CancelRowDrag();
}

// 元素控件分发件：为单个 element config 在 (dataObject, token) 处建并双向绑定一个无标题控件。
// 复用各控件件（Slider/Text/ComboBox/CheckBox）+ IDataPropertyObject 字段绑定（token 当 key）；
// 复合元素递归——对象元素嵌 PropertyObjectController（其内部字段自带 key 标签）、数组元素嵌 Array/ListController。
internal abstract class ElementWidget : IDisposable
{
    public abstract Control View { get; }
    public abstract Type ConfigType { get; }
    public abstract void Update(IControllerConfig config);
    public virtual void Dispose() => s.DisposeAll();

    protected readonly DisposableManager s = new();

    // buttonRoot / buttonPath：本元素在按钮作用域里的位置（见 ButtonRoot），复合元素据此把作用域传给内层控制器。
    // ButtonConfig 直接当元素（或可加键模板）不支持——那些位置每一格都是一个值，按钮没有值可占——落到 UnknownElement。
    public static ElementWidget Create(IDataPropertyObject dataObject, string token, IControllerConfig config,
        ButtonRoot? buttonRoot = null, string buttonPath = "") => config switch
    {
        SliderConfig c => new SliderElement(dataObject, token, c),
        TextBoxConfig c => new TextElement(dataObject, token, c),
        PathPickerConfig c => new PathElement(dataObject, token, c),
        ComboBoxConfig c => new ComboElement(dataObject, token, c),
        CheckBoxConfig c => new CheckElement(dataObject, token, c),
        ObjectConfig c => new ObjectElement(dataObject, token, c, buttonRoot, buttonPath),
        ArrayConfig c => new NestedArrayElement(dataObject, token, c, buttonRoot, buttonPath),
        ListConfig c => new NestedListElement(dataObject, token, c, buttonRoot, buttonPath),
        ExtensibleObjectConfig c => new NestedExtensibleObjectElement(dataObject, token, c, buttonRoot, buttonPath),
        _ => new UnknownElement(config),
    };

    sealed class SliderElement : ElementWidget
    {
        public SliderElement(IDataPropertyObject dataObject, string token, SliderConfig config)
        {
            mController = new SliderController { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            Apply(config);
            mController.BindDataProperty(dataObject.DoubleField(token, config.DefaultValue), s);
        }

        void Apply(SliderConfig config)
        {
            mController.SetScale(config.Scale);
            mController.SetDefaultValue(config.DefaultValue);
            mController.NumberFormat = config.Format;
            mController.ShowRandomButton = config.Randomizable;
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(SliderConfig);
        public override void Update(IControllerConfig config) => Apply((SliderConfig)config);

        readonly SliderController mController;
    }

    // 单行 / 多行（IsMultiline）两种形态共用 TextBoxConfig，故与属性面板的 TextCreator 同款：控件装在容器里，
    // View 恒为该容器，换形态只换容器里的孩子——否则 View 是构造期取一次的，reconcile 复用本件时换不掉。
    sealed class TextElement : ElementWidget
    {
        public TextElement(IDataPropertyObject dataObject, string token, TextBoxConfig config)
        {
            mDataObject = dataObject;
            mToken = token;
            Build(config);
        }

        public override Control View => mHost;
        public override Type ConfigType => typeof(TextBoxConfig);

        public override void Update(IControllerConfig config)
        {
            var c = (TextBoxConfig)config;
            if (c.IsMultiline != mIsMultiline)
            {
                mFieldScope.DisposeAll();
                mHost.Children.Clear();
                Build(c);
                return;
            }

            if (mMultiline != null)
                mMultiline.SetMaxVisibleLines(c.MaxVisibleLines);
            else if (mSingleLine != null)
                mSingleLine.IsPassword = c.IsPassword;
        }

        void Build(TextBoxConfig config)
        {
            mIsMultiline = config.IsMultiline;
            var field = mDataObject.StringField(mToken, config.DefaultValue);
            if (mIsMultiline)
            {
                mSingleLine = null;
                mMultiline = new MultilineTextInput { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
                mMultiline.SetMaxVisibleLines(config.MaxVisibleLines);
                mMultiline.BindDataProperty(field, mFieldScope);
                mHost.Children.Add(mMultiline);
            }
            else
            {
                mMultiline = null;
                mSingleLine = new SingleLineTextController { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, IsPassword = config.IsPassword };
                mSingleLine.BindDataProperty(field, mFieldScope);
                mHost.Children.Add(mSingleLine);
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            mFieldScope.DisposeAll();
        }

        readonly IDataPropertyObject mDataObject;
        readonly string mToken;
        readonly Panel mHost = new();
        readonly DisposableManager mFieldScope = new();
        bool mIsMultiline;
        SingleLineTextController? mSingleLine;
        MultilineTextInput? mMultiline;
    }

    sealed class PathElement : ElementWidget
    {
        public PathElement(IDataPropertyObject dataObject, string token, PathPickerConfig config)
        {
            mController = new PathPicker { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            mController.Apply(config);
            mController.BindDataProperty(dataObject.StringField(token, config.DefaultValue), s);
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(PathPickerConfig);
        public override void Update(IControllerConfig config) => mController.Apply((PathPickerConfig)config);

        readonly PathPicker mController;
    }

    sealed class ComboElement : ElementWidget
    {
        public ComboElement(IDataPropertyObject dataObject, string token, ComboBoxConfig config)
        {
            mDataObject = dataObject;
            mToken = token;
            mConfig = config;
            mController = new ComboBoxController { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            BindWith(config);
        }

        // SetConfig（自洽显示 default）后紧接 bind（bind 内 Refresh 把当前数据值按三态显示），复刻 ComboBoxCreator 的成对范式。
        void BindWith(ComboBoxConfig config)
        {
            mController.SetConfig(config);
            mController.BindDataProperty(mDataObject.ValueField(mToken, config.DefaultOption.Value), s);
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(ComboBoxConfig);

        // 选项未变则跳过（SetConfig 会 Clear + 重填打断选中）；选项变则解旧绑定 → 重新 SetConfig+bind 显示回当前数据值。
        public override void Update(IControllerConfig config)
        {
            var combo = (ComboBoxConfig)config;
            if (combo.Items.SequenceEqual(mConfig.Items))
                return;
            mConfig = combo;
            s.DisposeAll();
            BindWith(combo);
        }

        readonly IDataPropertyObject mDataObject;
        readonly string mToken;
        readonly ComboBoxController mController;
        ComboBoxConfig mConfig;
    }

    sealed class CheckElement : ElementWidget
    {
        public CheckElement(IDataPropertyObject dataObject, string token, CheckBoxConfig config)
        {
            mController = new CheckBox { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            mController.BindDataProperty(dataObject.BooleanField(token, config.DefaultValue), s);
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(CheckBoxConfig);
        public override void Update(IControllerConfig config) { }

        readonly CheckBox mController;
    }

    // 对象元素：嵌 PropertyObjectController 渲染该元素子字段（带各自 key 标签），导航进 dataObject.Object(token)。
    sealed class ObjectElement : ElementWidget
    {
        public ObjectElement(IDataPropertyObject dataObject, string token, ObjectConfig config, ButtonRoot? buttonRoot, string buttonPath)
        {
            mController = new PropertyObjectController();
            if (buttonRoot != null)
                mController.AttachButtonRoot(buttonRoot, buttonPath);
            mController.SetConfig(config, dataObject.Object(token));
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(ObjectConfig);
        public override void Update(IControllerConfig config) => mController.Reconcile((ObjectConfig)config);
        public override void Dispose()
        {
            base.Dispose();
            mController.ResetConfig();
        }

        readonly PropertyObjectController mController;
    }

    // 数组套数组元素：嵌 ArrayController/ListController，导航进 dataObject.Array(token)。
    sealed class NestedArrayElement : ElementWidget
    {
        public NestedArrayElement(IDataPropertyObject dataObject, string token, ArrayConfig config, ButtonRoot? buttonRoot, string buttonPath)
        {
            mController.ButtonRoot = buttonRoot;
            mController.ButtonPath = buttonPath;
            mController.Bind(dataObject.Array(token));
            mController.Apply(config);
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(ArrayConfig);
        public override void Update(IControllerConfig config) => mController.Apply((ArrayConfig)config);
        public override void Dispose()
        {
            base.Dispose();
            mController.Unbind();
        }

        readonly ArrayController mController = new();
    }

    sealed class NestedListElement : ElementWidget
    {
        public NestedListElement(IDataPropertyObject dataObject, string token, ListConfig config, ButtonRoot? buttonRoot, string buttonPath)
        {
            mController.ButtonRoot = buttonRoot;
            mController.ButtonPath = buttonPath;
            mController.Bind(dataObject.Array(token));
            mController.Apply(config);
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(ListConfig);
        public override void Update(IControllerConfig config) => mController.Apply((ListConfig)config);
        public override void Dispose()
        {
            base.Dispose();
            mController.Unbind();
        }

        readonly ListController mController = new();
    }

    // 数组套变长键控对象元素：嵌 ExtensibleObjectController，导航进 dataObject.Object(token)。
    sealed class NestedExtensibleObjectElement : ElementWidget
    {
        public NestedExtensibleObjectElement(IDataPropertyObject dataObject, string token, ExtensibleObjectConfig config, ButtonRoot? buttonRoot, string buttonPath)
        {
            mController.ButtonRoot = buttonRoot;
            mController.ButtonPath = buttonPath;
            mController.Bind(dataObject.Object(token));
            mController.Apply(config);
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(ExtensibleObjectConfig);
        public override void Update(IControllerConfig config) => mController.Apply((ExtensibleObjectConfig)config);
        public override void Dispose()
        {
            base.Dispose();
            mController.Unbind();
        }

        readonly ExtensibleObjectController mController = new();
    }

    sealed class UnknownElement : ElementWidget
    {
        public UnknownElement(IControllerConfig config)
        {
            mConfigType = config.GetType();
            mView = new Label { Content = $"(unsupported element: {mConfigType.Name})", FontSize = 12, Foreground = Style.LIGHT_WHITE.ToBrush(), Margin = new(0, 6) };
        }

        public override Control View => mView;
        public override Type ConfigType => mConfigType;
        public override void Update(IControllerConfig config) { }

        readonly Label mView;
        readonly Type mConfigType;
    }
}

// 借壳数据对象（app 侧）：把整套文档身份（撤销/Modified/merge/Head）转发给被包裹对象。
// 等价 Hosting.Foundation 内部的 IDataObject.Wrapper，但那是 internal、跨程序集不可用，故此处自备一份供 seed 视图复用。
abstract class ForwardingDataObject(IDataObject inner) : IDataObject
{
    public IModifiedEvent Modified => inner.Modified;
    public IModifiedEvent WillModify => inner.WillModify;
    public Head Head => inner.Head;
    public void Attach(IDataObject parent) => inner.Attach(parent);
    public void Detach() => inner.Detach();
    public IDisposable MergeNotify() => inner.MergeNotify();
    public void BeginMergeNotify() => inner.BeginMergeNotify();
    public void EndMergeNotify() => inner.EndMergeNotify();
    public bool Pushable() => inner.Pushable();
    public bool Commit() => inner.Commit();
    public bool Discard() => inner.Discard();
    public bool DiscardTo(Head head) => inner.DiscardTo(head);
    public bool Undo() => inner.Undo();
    public bool Redo() => inner.Redo();
}

// 越界（seed/虚拟）位置的数据外观：表示数组 position（≥ 现长）处尚未物化的元素槽。
// 读：返回默认值（浏览不弄脏文档，对称标量字段「缺席读默认」）。
// 写：先把整段 seed 物化——数组从现长补齐到 seedDefaults.Count 个元素、各取对应 element config 默认值（含本位），
//     再对 position 落写入值。如此「首次写入即物化整段 seed」，物化后 present-count = N，插件不再 reseed、显示稳定不塌；
//     下次 reconcile 用真实 token 行替换本虚拟行。提交由控件绑定的提交周期承担（与对象懒建 GetOrCreateObject 同，不在此 commit）。
sealed class SeedPositionView : ForwardingDataObject, IDataPropertyObject
{
    public SeedPositionView(IDataPropertyArray array, int position, IReadOnlyList<PropertyValue> seedDefaults) : base(array)
    {
        mArray = array;
        mPosition = position;
        mSeedDefaults = seedDefaults;
    }

    bool Real => mPosition < mArray.Count;

    void Materialize()
    {
        for (int i = mArray.Count; i < mSeedDefaults.Count; i++)
            mArray.Add(mSeedDefaults[i]);
    }

    public PropertyValue GetValue(string key, PropertyValue defaultValue)
        => Real ? mArray.GetValue(mArray.Tokens[mPosition], defaultValue) : defaultValue;

    public void SetValue(string key, PropertyValue value)
    {
        if (!Real)
            Materialize();
        mArray.SetValue(mArray.Tokens[mPosition], value);
    }

    // 复合 seed 元素物化前不渲染虚拟行（见 ReconcileRows），故下列导航仅物化后（Real）走到；
    // 防御：未物化先物化再下钻（标量 seed 流程不会触发）。
    public IDataPropertyObject Object(string key) { if (!Real) Materialize(); return mArray.Object(mArray.Tokens[mPosition]); }
    public IDataPropertyArray Array(string key) { if (!Real) Materialize(); return mArray.Array(mArray.Tokens[mPosition]); }
    public void RemoveValue(string key) { } // seed 位删除由控制器 RemoveRow 处理，标量 seed 流程不触发

    readonly IDataPropertyArray mArray;
    readonly int mPosition;
    readonly IReadOnlyList<PropertyValue> mSeedDefaults;
}

internal static class ArrayControlsFactory
{
    // 文本按钮：复刻 EffectsController 的 BorderItem + TextItem 配色，与侧栏其余按钮一致。
    public static Button MakeTextButton(string text, double width)
    {
        var button = new Button();
        if (width > 0)
            button.Width = width;
        button.AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER, PressedColor = Style.INTERFACE } });
        button.AddContent(new() { Item = new TextItem() { Text = text, FontSize = 12 }, ColorSet = new() { Color = Colors.White } });
        return button;
    }

    // 元素行序号条宽度，与轨道头序号块同宽。
    public const double StripWidth = 24;
    // 属性面板左右边距：字段控件都缩进这么多。
    public const double Gutter = 24;
    // 删除钮边长：对齐文本框与按钮的高度（28）。
    public const double DeleteSize = 28;
    // 删除钮与控件右沿的间距。
    public const double DeleteOffset = 4;

    // 分隔线（同字段间分隔线：1px、BACK 色）。
    public static Border MakeLine() => new() { Height = 1, Background = Style.BACK.ToBrush() };

    // 标题与其下内容的间隔：标题 Label 底对齐，不留缝就贴住下方（只有标题的空对象条目会一条条挤在一起）。
    // 取 12 = 普通字段控件的上边距（Margin(24, 12)），容器字段的标题到内容与之同距。
    public const double TitleGap = 12;
    // 键控条目上下内边距（分隔线到名字、末端内容到分隔线）。上下同值：只有名字的空对象条目上下对称。
    public const double EntryPadding = 10;

    // 键控条目的行标题（取 PropertyKey.DisplayText）：字样同属性面板字段标题，高度随文字——上下留白由条目的
    // EntryPadding 统一给，不再靠固定高度底对齐撑出上方空白（那样上下两边各由不同的量决定，对不齐）。
    // 与下方控件之间的 TitleGap 由条目按「下方有无内容」决定（见 KeyedRow）。
    public static Label MakeRowTitle(string text)
    {
        return new Label
        {
            Content = text,
            FontSize = 12,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            Padding = new(24, 0),
        };
    }
}

// config → 默认 PropertyValue 的递归求值：叶子取 IValueConfig.DefaultValue，复合（对象/数组/列表）递归各成员拼合。
// 供「恢复默认 / 应用 preset」walker 与 ListController 的「+ 添加元素」（新元素 seed 默认值）共用。
internal static class ControllerConfigDefaults
{
    public static PropertyValue GetDefaultValue(this IControllerConfig config)
    {
        switch (config)
        {
            case ObjectConfig objectConfig:
            {
                var map = new Map<string, PropertyValue>();
                foreach (var kvp in objectConfig.Properties)
                {
                    if (kvp.Value is ButtonConfig)
                        continue;   // 按钮没有值，不占键
                    map.Add(kvp.Key.Id, kvp.Value.GetDefaultValue());
                }
                return PropertyValue.Create(new PropertyObject(map));
            }
            case ExtensibleObjectConfig addableObjectConfig:
            {
                // 变长键控容器默认值 = 当前已声明键的默认值拼成的对象（同 ObjectConfig）。
                var map = new Map<string, PropertyValue>();
                foreach (var kvp in addableObjectConfig.Properties)
                    map.Add(kvp.Key.Id, kvp.Value.GetDefaultValue());
                return PropertyValue.Create(new PropertyObject(map));
            }
            case ArrayConfig arrayConfig:
                return new PropertyArray(arrayConfig.Elements.Select(e => e.GetDefaultValue()).ToList());
            case ListConfig listConfig:
                return new PropertyArray(listConfig.Elements.Select(e => e.GetDefaultValue()).ToList());
            case IValueConfig valueConfig:
                return valueConfig.DefaultValue;
            default:
                return PropertyValue.Null;
        }
    }
}
