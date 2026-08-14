using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Media;
using TuneLab.Animation;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI.Input;
using TuneLab.SDK;
using TuneLab.Utils;
using TuneLab.Configs;

namespace TuneLab.UI;

// 参数区分段轨（IPiecewiseAutomation）的编辑操作：镜像 pitch 的分段编辑（绘制/擦除/锚点选/移/删/插），
// 值轴用 config 的标度（INormalizedScale）映射到 Bounds.Height（不像 pitch 用 PitchAxis）。
// 分段轨无默认基线：锚点 Value 即绝对值（区别于连续轨存"值减默认"）。复用现有 State 值，Move/Up 按 IsOperating 分派。
internal partial class AutomationRenderer
{
    bool TryGetActivePiecewise([NotNullWhen(true)] out IPiecewiseAutomation? automation, [NotNullWhen(true)] out AutomationConfig? config, bool createIfMissing)
    {
        automation = null;
        config = null;
        if (Part == null)
            return false;

        var key = mDependency.ActiveAutomation;
        if (key == null || !Part.IsEffectivePiecewiseAutomation(key.Value))
            return false;

        config = Part.GetEffectivePiecewiseAutomationConfig(key.Value);
        automation = Part.GetEffectivePiecewiseAutomation(key.Value);
        if (automation == null && createIfMissing)
            automation = Part.AddEffectivePiecewiseAutomation(key.Value);

        return automation != null;
    }

    // 当前激活轨是分段轨（用于 OnMouseDown 路由——同 id 不跨连续/分段复用，故 piecewise 判定即足够）。
    bool ActiveIsPiecewise()
    {
        var key = mDependency.ActiveAutomation;
        return Part != null && key != null && Part.IsEffectivePiecewiseAutomation(key.Value);
    }

    // Anchor 工具下、激活轨为分段轨时的鼠标按下分派（镜像连续轨的 Anchor 处理）。
    void OnPiecewiseAnchorMouseDown(MouseDownEventArgs e, bool ctrl, Item? item)
    {
        switch (e.MouseButtonType)
        {
            case MouseButtonType.PrimaryButton:
                // 连线预览生效时，单击即按预览落笔（与 pitch 锚点同手感：选中某锚点 → 悬浮空白见预览线 → 单击接上）。
                // 必须先于下面的分支：预览的落点可能正是某个锚点（悬浮组首/末时连接两组），不能被 AnchorMove 抢走。
                if (mPiecewisePreviewItem?.OnDown is { } onDown)
                {
                    onDown.Invoke(e, ctrl);
                    break;
                }

                if (item is PiecewiseAnchorItem anchorItem)
                {
                    mPiecewiseAnchorMoveOperation.Down(e.Position, ctrl, anchorItem.Automation, anchorItem.AnchorPoint, anchorItem.Scale);
                }
                else if (e.IsDoubleClick)
                {
                    if (Part == null || !TryGetActivePiecewise(out var automation, out var config, true))
                        break;

                    var anchor = new AnchorPoint(TickAxis.X2Tick(e.Position.X) - Part.Pos.Value, YToValue(e.Position.Y, config.Scale)) { IsSelected = true };
                    automation.InsertPoint(anchor);
                    var inserted = automation.AnchorGroups.SelectMany(group => group).FirstOrDefault(point => point.Pos == anchor.Pos);
                    if (inserted == null)
                        break;

                    automation.DeselectAllAnchors();
                    inserted.Select();
                    mPiecewiseAnchorMoveOperation.Down(e.Position, ctrl, automation, inserted, config.Scale, true);
                }
                else
                {
                    mPiecewiseAnchorSelectOperation.Down(e.Position, ctrl);
                }
                break;
            case MouseButtonType.SecondaryButton:
                // 右键先取消该轨全部选中（空白处右键即"取消选中"，对齐 pitch 锚点工具），再按命中删点。
                // 取消选中后必须重绘：选中环是绘制出来的，不刷新会留着假的选中态；连线预览也随选中态变化。
                if (TryGetActivePiecewise(out var deselectTarget, out _, false))
                    deselectTarget.DeselectAllAnchors();
                if (item is PiecewiseAnchorItem deleteAnchorItem)
                    deleteAnchorItem.Automation.DeletePoints([deleteAnchorItem.AnchorPoint]);
                InvalidateVisual();
                mPiecewiseAnchorDeleteOperation.Down(e.Position.X);
                break;
            default:
                break;
        }
    }

    // 分段轨锚点工具的"连线预览"（镜像 pitch 的 PreviewAnchorGroupItem）：有选中锚点的组 + 鼠标悬浮位置 ⇒
    // 在临时 PiecewiseAutomation 上试插一个点，画出"单击后线会怎么连"的白色预览；单击即在真数据上重放同一步。
    // 没有它，分段轨上只有"双击插孤立点"可用——锚点工具在空轨上永远连不成线（双击的前半个单击会经框选清掉
    // 选中态，而 InsertPoint 的并组判据正是"相邻组有选中锚点"）。
    //
    // 两种形态（与 pitch 一致）：
    // · 悬浮空白：向左 / 右相邻组（其中有选中锚点的）伸出一段，单击 = 插点并入该组；
    // · 悬浮某组的首 / 末锚点：若其外侧相邻组也有选中锚点，预览把两组接成一段，单击 = ConnectAnchorGroup。
    void UpdatePiecewisePreview(IItemCollection items, IPiecewiseAutomation piecewise, AutomationConfig config)
    {
        if (Part == null || mState != State.None || !IsHover)
            return;

        var hoverAnchor = (HoverItem() as PiecewiseAnchorItem)?.AnchorPoint;
        IAnchorGroup? hoverAnchorOnFirstGroup = null;
        IAnchorGroup? hoverAnchorOnLastGroup = null;
        int hoverAnchorGroupIndex = -1;
        for (int i = 0; i < piecewise.AnchorGroups.Count; i++)
        {
            var anchorGroup = piecewise.AnchorGroups[i];
            if (anchorGroup.IsEmpty())
                continue;

            if (anchorGroup.ConstFirst() == hoverAnchor)
            {
                hoverAnchorOnFirstGroup = anchorGroup;
                hoverAnchorGroupIndex = i;
            }
            if (anchorGroup.ConstLast() == hoverAnchor)
            {
                hoverAnchorOnLastGroup = anchorGroup;
                hoverAnchorGroupIndex = i;
            }
            if (hoverAnchorGroupIndex != -1)
                break;
        }

        // 悬浮在某组的中间锚点上：那是纯拖拽目标，不给连线预览。
        if (hoverAnchor != null && hoverAnchorOnFirstGroup == null && hoverAnchorOnLastGroup == null)
            return;

        double pos = TickAxis.X2Tick(MousePosition.X) - Part.Pos.Value;
        var areaID = piecewise.GetAreaID(pos);
        int[] previewIndex = hoverAnchor == null
            ? areaID.IsInGroup ? [areaID.Index] : [areaID.LeftIndex, areaID.RightIndex]
            : [.. (hoverAnchorOnFirstGroup == null ? Array.Empty<int>() : [hoverAnchorGroupIndex - 1]), hoverAnchorGroupIndex, .. (hoverAnchorOnLastGroup == null ? Array.Empty<int>() : [hoverAnchorGroupIndex + 1])];
        var previewInfo = previewIndex
            .Where(index => (uint)index < piecewise.AnchorGroups.Count)
            .Select(index => piecewise.AnchorGroups[index])
            .Where(anchorGroup => anchorGroup.HasSelectedItem() || anchorGroup == hoverAnchorOnFirstGroup || anchorGroup == hoverAnchorOnLastGroup)
            .Select(anchorGroup => anchorGroup.GetInfo().Select(p => p.ToPoint()).ToList()).ToList();
        if (previewInfo.Count == 0)
            return;

        var preview = new PiecewisePreviewItem(this) { PiecewiseAutomation = new PiecewiseAutomation(), Scale = config.Scale };
        preview.PiecewiseAutomation.SetInfo(previewInfo);
        if (hoverAnchor == null)
        {
            // 预览副本里把各组首点标选中，使临时 InsertPoint 走与真数据相同的并组分支。
            foreach (var anchorGroup in preview.PiecewiseAutomation.AnchorGroups)
                anchorGroup[0].Select();

            double value = YToValue(MousePosition.Y, config.Scale);
            preview.PiecewiseAutomation.InsertPoint(new AnchorPoint(pos, value));
            preview.OnDown = (e, ctrl) =>
            {
                if (!TryGetActivePiecewise(out var target, out var targetConfig, true))
                    return;

                var anchor = new AnchorPoint(TickAxis.X2Tick(e.Position.X) - Part!.Pos.Value, YToValue(e.Position.Y, targetConfig.Scale)) { IsSelected = true };
                target.InsertPoint(anchor);
                target.DeselectAllAnchors();
                anchor.Select();
                mPiecewiseAnchorMoveOperation.Down(e.Position, ctrl, target, anchor, targetConfig.Scale, true);
            };
        }
        else
        {
            // 先处理向后连接的，顺序不能乱（预览副本里 index 恒从 0 起算）！
            if (hoverAnchorOnLastGroup != null)
            {
                preview.PiecewiseAutomation.ConnectAnchorGroup(0);
                if (hoverAnchorGroupIndex + 1 < piecewise.AnchorGroups.Count && piecewise.AnchorGroups[hoverAnchorGroupIndex + 1].HasSelectedItem())
                    preview.OnDown += (_, _) => piecewise.ConnectAnchorGroup(hoverAnchorGroupIndex);
            }
            if (hoverAnchorOnFirstGroup != null)
            {
                preview.PiecewiseAutomation.ConnectAnchorGroup(0);
                if (hoverAnchorGroupIndex - 1 >= 0 && piecewise.AnchorGroups[hoverAnchorGroupIndex - 1].HasSelectedItem())
                    preview.OnDown += (_, _) => piecewise.ConnectAnchorGroup(hoverAnchorGroupIndex - 1);
            }
            // 连接在此只改数据、不单独提交：一次按下抬起是**一个**用户动作，该是一个撤销单元。
            // 提交由紧接的 AnchorMove.Up 统一做（它未拖动时只 DiscardTo(mHead) 撤自己的变更、保留连接，再 Commit）。
            if (preview.OnDown != null)
                preview.OnDown += (e, ctrl) => mPiecewiseAnchorMoveOperation.Down(e.Position, ctrl, piecewise, hoverAnchor, config.Scale);
        }

        mPiecewisePreviewItem = preview;
        items.Add(preview);
    }

    class PiecewisePreviewItem(AutomationRenderer automationRenderer) : AutomationRenderItem(automationRenderer)
    {
        public required IPiecewiseAutomation PiecewiseAutomation { get; set; }
        public required INormalizedScale Scale { get; set; }
        public Action<MouseDownEventArgs, bool>? OnDown { get; set; }

        // 纯视觉预览，不参与命中（命中仍归真锚点 / 空白）。
        public override bool Raycast(Avalonia.Point point) => false;

        public override void Render(DrawingContext context)
        {
            AutomationRenderer.DrawPiecewiseCurve(context, PiecewiseAutomation, Scale, Colors.White);
        }
    }

    PiecewisePreviewItem? mPiecewisePreviewItem;

    readonly PiecewiseDrawOperation mPiecewiseDrawOperation;
    readonly PiecewiseClearOperation mPiecewiseClearOperation;
    readonly PiecewiseAnchorDeleteOperation mPiecewiseAnchorDeleteOperation;
    readonly PiecewiseAnchorMoveOperation mPiecewiseAnchorMoveOperation;
    readonly PiecewiseAnchorSelectOperation mPiecewiseAnchorSelectOperation;

    class PiecewiseAnchorItem(AutomationRenderer automationRenderer) : AutomationRenderItem(automationRenderer)
    {
        public required IPiecewiseAutomation Automation { get; set; }
        public required AnchorPoint AnchorPoint { get; set; }
        public required INormalizedScale Scale { get; set; }
        public required Color Color { get; set; }

        public Avalonia.Point Position()
        {
            return AutomationRenderer.TickAndValueToPoint(AnchorPoint.Pos, AnchorPoint.Value, Scale);
        }

        public override bool Raycast(Avalonia.Point point)
        {
            return Avalonia.Point.Distance(Position(), point) <= 6;
        }

        public override void Render(DrawingContext context)
        {
            var hoverAnchor = (AutomationRenderer.HoverItem() as PiecewiseAnchorItem)?.AnchorPoint;
            var center = Position();
            var pointBrush = new SolidColorBrush(Color);
            context.DrawEllipse(pointBrush, null, center, 2.5, 2.5);
            if (AnchorPoint.IsSelected)
                context.DrawEllipse(null, new Pen(pointBrush), center, 5.5, 5.5);
            else if (AnchorPoint == hoverAnchor)
                context.DrawEllipse(null, new Pen(Brushes.White), center, 5.5, 5.5);
        }
    }

    class PiecewiseDrawOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        [MemberNotNullWhen(true, nameof(mAutomation))]
        public bool IsOperating => mAutomation != null && State == State.Drawing;

        public void Down(Avalonia.Point mousePosition, bool constantValue)
        {
            if (IsOperating || AutomationRenderer.Part == null)
                return;

            if (!AutomationRenderer.TryGetActivePiecewise(out mAutomation, out var config, true))
                return;

            mScale = config.Scale;
            State = State.Drawing;
            AutomationRenderer.Part.BeginMergeDirty();
            mHead = mAutomation.Head;
            mDownValue = AutomationRenderer.YToValue(mousePosition.Y, mScale);   // 锁定按下时的 y，供定值绘制
            mPointLines.Add([ToTickAndValue(mousePosition, constantValue)]);
            mAutomation.AddLine(mPointLines[0], Settings.ParameterBoundaryExtension);
        }

        public void Move(Avalonia.Point mousePosition, bool constantValue)
        {
            if (!IsOperating)
                return;

            var point = ToTickAndValue(mousePosition, constantValue);
            var lastLine = mPointLines.Last();
            var lastPoint = mDirection ? lastLine.Last() : lastLine.First();
            if (lastPoint.X == point.X)
            {
                if (lastPoint.Y == point.Y)
                    return;

                lastLine[mDirection ? lastLine.Count - 1 : 0] = point;
            }
            else
            {
                bool direction = point.X > lastPoint.X;
                if (lastLine.Count == 1)
                {
                    lastLine.Insert(direction ? 1 : 0, point);
                }
                else
                {
                    if (direction == mDirection)
                        lastLine.Insert(direction ? lastLine.Count : 0, point);
                    else
                        mPointLines.Add(direction ? [lastPoint, point] : [point, lastPoint]);
                }

                mDirection = direction;
            }

            mAutomation.DiscardTo(mHead);
            foreach (var line in mPointLines)
                mAutomation.AddLine(line.Simplify(5, 2), Settings.ParameterBoundaryExtension);
        }

        public void Up()
        {
            if (!IsOperating || AutomationRenderer.Part == null)
                return;

            mAutomation.DiscardTo(mHead);
            foreach (var line in mPointLines)
                mAutomation.AddLine(line.Simplify(5, 2), Settings.ParameterBoundaryExtension);
            AutomationRenderer.Part.EndMergeDirty();
            mAutomation.Commit();
            mAutomation = null;
            mPointLines.Clear();
            State = State.None;
        }

        Point ToTickAndValue(Avalonia.Point mousePosition, bool constantValue)
        {
            double value = constantValue ? mDownValue : AutomationRenderer.YToValue(mousePosition.Y, mScale);
            return new(AutomationRenderer.TickAxis.X2Tick(mousePosition.X) - AutomationRenderer.Part!.Pos.Value, value);
        }

        IPiecewiseAutomation? mAutomation;
        INormalizedScale mScale = null!;
        double mDownValue;   // 定值绘制锁定的值（按下时捕获）
        bool mDirection;
        Head mHead;
        readonly List<List<Point>> mPointLines = new();
    }

    class PiecewiseClearOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        [MemberNotNullWhen(true, nameof(mAutomation))]
        public bool IsOperating => mAutomation != null && State == State.Clearing;

        public void Down(double x)
        {
            if (IsOperating || AutomationRenderer.Part == null)
                return;

            if (!AutomationRenderer.TryGetActivePiecewise(out mAutomation, out _, false))
                return;

            State = State.Clearing;
            AutomationRenderer.Part.BeginMergeDirty();
            mHead = mAutomation.Head;
            double tick = AutomationRenderer.TickAxis.X2Tick(x) - AutomationRenderer.Part.Pos.Value;
            mStart = tick;
            mEnd = tick;
            mAutomation.Clear(mStart, mEnd);
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            mAutomation.DiscardTo(mHead);
            double tick = AutomationRenderer.TickAxis.X2Tick(x) - AutomationRenderer.Part!.Pos.Value;
            mStart = Math.Min(mStart, tick);
            mEnd = Math.Max(mEnd, tick);
            mAutomation.Clear(mStart, mEnd);
        }

        public void Up()
        {
            if (!IsOperating || AutomationRenderer.Part == null)
                return;

            mAutomation.DiscardTo(mHead);
            mAutomation.Clear(mStart, mEnd);
            AutomationRenderer.Part.EndMergeDirty();
            mAutomation.Commit();
            mAutomation = null;
            State = State.None;
        }

        IPiecewiseAutomation? mAutomation;
        double mStart;
        double mEnd;
        Head mHead;
    }

    class PiecewiseAnchorDeleteOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        [MemberNotNullWhen(true, nameof(mAutomation))]
        public bool IsOperating => mAutomation != null && State == State.AnchorDeleting;

        public void Down(double x)
        {
            if (IsOperating || AutomationRenderer.Part == null)
                return;

            if (!AutomationRenderer.TryGetActivePiecewise(out mAutomation, out _, false))
                return;

            State = State.AnchorDeleting;
            AutomationRenderer.Part.BeginMergeDirty();
            mHead = mAutomation.Head;
            double tick = AutomationRenderer.TickAxis.X2Tick(x) - AutomationRenderer.Part.Pos.Value;
            mStart = tick;
            mEnd = tick;
            mAutomation.DeletePoints(mStart, mEnd);
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            mAutomation.DiscardTo(mHead);
            double tick = AutomationRenderer.TickAxis.X2Tick(x) - AutomationRenderer.Part!.Pos.Value;
            mStart = Math.Min(mStart, tick);
            mEnd = Math.Max(mEnd, tick);
            mAutomation.DeletePoints(mStart, mEnd);
        }

        public void Up()
        {
            if (!IsOperating || AutomationRenderer.Part == null)
                return;

            mAutomation.DiscardTo(mHead);
            mAutomation.DeletePoints(mStart, mEnd);
            AutomationRenderer.Part.EndMergeDirty();
            mAutomation.Commit();
            mAutomation = null;
            State = State.None;
        }

        IPiecewiseAutomation? mAutomation;
        double mStart;
        double mEnd;
        Head mHead;
    }

    class PiecewiseAnchorMoveOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        public bool IsOperating => State == State.AnchorMoving && mAnchor != null;

        public void Down(Avalonia.Point point, bool ctrl, IPiecewiseAutomation automation, AnchorPoint anchor, INormalizedScale scale, bool keepChangeWithoutMove = false)
        {
            if (AutomationRenderer.Part == null)
                return;

            mAutomation = automation;
            mAnchor = anchor;
            mCtrl = ctrl;
            mIsSelected = anchor.IsSelected;
            mKeepChangeWithoutMove = keepChangeWithoutMove;
            mScale = scale;
            if (!mCtrl && !mIsSelected)
                mAutomation.DeselectAllAnchors();
            anchor.Select();

            State = State.AnchorMoving;
            AutomationRenderer.Part.BeginMergeDirty();
            mHead = AutomationRenderer.Part.Head;
            mXOffset = point.X - AutomationRenderer.TickAxis.Tick2X(AutomationRenderer.Part.Pos.Value + anchor.Pos);
            mYOffset = point.Y - AutomationRenderer.ValueToY(anchor.Value, mScale);
            AutomationRenderer.InvalidateVisual();
        }

        public void Move(Avalonia.Point point)
        {
            var part = AutomationRenderer.Part;
            if (part == null || mAutomation == null || mAnchor == null)
                return;

            double pos = AutomationRenderer.TickAxis.X2Tick(point.X - mXOffset) - part.Pos.Value;
            double posOffset = pos - mAnchor.Pos;
            double value = AutomationRenderer.YToValue(point.Y - mYOffset, mScale);
            double valueOffset = value - mAnchor.Value;

            mMoved = true;
            part.DiscardTo(mHead);
            mAutomation.MoveSelectedPoints(posOffset, valueOffset);
        }

        public void Up()
        {
            State = State.None;

            if (mAnchor == null || mAutomation == null || AutomationRenderer.Part == null)
                return;

            if (!mMoved && !mKeepChangeWithoutMove)
            {
                // 只撤**本操作**产生的变更（DiscardTo(mHead)），不是 Discard() 的"撤销全部未提交命令"——
                // 同一次按下里可能有先行的离散动作（如预览落笔的 ConnectAnchorGroup），那属于用户这一下的意图，
                // 不能被"没拖动"连坐撤掉。
                //
                // 【顺序不变量，勿调换】DiscardTo 必须在 EndMergeDirty **之前**：mHead 取在 BeginMergeDirty
                // 之后（见 Down），若先 End 再 DiscardTo，那条 End 命令本身会被撤掉——其逆动作是 Begin，
                // 括号因此被重新打开且再无人关闭。后果是静默失效而非崩溃：该 part 的曲线变更全被批量缓冲
                // 吞掉、调度器永久跳过它（"画完一笔就再也不渲染"）。同一陷阱见 PianoScrollViewOperation
                // 的音高锚点移动。
                AutomationRenderer.Part.DiscardTo(mHead);
                if (mCtrl)
                {
                    if (mIsSelected)
                        mAnchor.Inselect();
                }
                else
                {
                    mAutomation.DeselectAllAnchors();
                    mAnchor.Select();
                }
            }
            // 末尾照样 Commit：把先行动作收成**一个**撤销单元（纯点击无先行动作时 Commit 是 no-op）。
            AutomationRenderer.Part.EndMergeDirty();
            AutomationRenderer.Part.Commit();

            mMoved = false;
            mKeepChangeWithoutMove = false;
            mAutomation = null;
            mAnchor = null;
            AutomationRenderer.InvalidateVisual();
        }

        IPiecewiseAutomation? mAutomation;
        AnchorPoint? mAnchor;
        bool mCtrl;
        bool mIsSelected;
        bool mMoved = false;
        bool mKeepChangeWithoutMove = false;
        double mXOffset;
        double mYOffset;
        INormalizedScale mScale = null!;
        Head mHead;
    }

    // 选区值轴比较在归一化域进行（同连续轨 AnchorSelectOperation）：纯几何操作不过标度取值，
    // 吸附标度下选框边界才不跳格；锚点值经 ToNormalized（连续逆）落到同一域比较——所见即所选。
    class PiecewiseAnchorSelectOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        public bool IsOperating => State == State.AnchorSelecting && mAutomation != null;

        public void Down(Avalonia.Point point, bool ctrl)
        {
            if (State != State.None)
                return;

            if (!AutomationRenderer.TryGetActivePiecewise(out mAutomation, out var config, false))
                return;

            State = State.AnchorSelecting;
            mScale = config.Scale;
            mDownTick = AutomationRenderer.TickAxis.X2Tick(point.X) - AutomationRenderer.Part!.Pos.Value;
            mDownNormalized = AutomationRenderer.YToNormalized(point.Y);
            if (ctrl)
                mSelectedItems = AllAnchors().Where(a => a.IsSelected).ToList();
            Move(point);
        }

        public void Move(Avalonia.Point point)
        {
            if (!IsOperating || mAutomation == null)
                return;

            mTick = AutomationRenderer.TickAxis.X2Tick(point.X) - AutomationRenderer.Part!.Pos.Value;
            mNormalized = AutomationRenderer.YToNormalized(point.Y);
            mAutomation.DeselectAllAnchors();
            if (mSelectedItems != null)
            {
                foreach (var item in mSelectedItems)
                    item.Select();
            }

            double minTick = Math.Min(mTick, mDownTick);
            double maxTick = Math.Max(mTick, mDownTick);
            double minNormalized = Math.Min(mNormalized, mDownNormalized);
            double maxNormalized = Math.Max(mNormalized, mDownNormalized);
            foreach (var anchor in AllAnchors())
            {
                double normalized = mScale.ToNormalized(anchor.Value);
                if (anchor.Pos >= minTick && anchor.Pos <= maxTick && normalized >= minNormalized && normalized <= maxNormalized)
                    anchor.Select();
            }

            AutomationRenderer.InvalidateVisual();
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;
            mSelectedItems = null;
            mAutomation = null;
            AutomationRenderer.InvalidateVisual();
        }

        public Avalonia.Rect SelectionRect()
        {
            if (mAutomation == null)
                return new Avalonia.Rect();

            double minTick = Math.Min(mTick, mDownTick);
            double maxTick = Math.Max(mTick, mDownTick);
            double left = AutomationRenderer.TickAxis.Tick2X(AutomationRenderer.Part!.Pos.Value + minTick);
            double right = AutomationRenderer.TickAxis.Tick2X(AutomationRenderer.Part.Pos.Value + maxTick);
            double top = AutomationRenderer.NormalizedToY(Math.Max(mNormalized, mDownNormalized));
            double bottom = AutomationRenderer.NormalizedToY(Math.Min(mNormalized, mDownNormalized));
            return new Avalonia.Rect(left, top, right - left, bottom - top);
        }

        IEnumerable<AnchorPoint> AllAnchors() => mAutomation!.AnchorGroups.SelectMany(group => group);

        IPiecewiseAutomation? mAutomation;
        IReadOnlyCollection<AnchorPoint>? mSelectedItems;
        INormalizedScale mScale = null!;
        double mDownTick;
        double mDownNormalized;
        double mTick;
        double mNormalized;
    }
}
