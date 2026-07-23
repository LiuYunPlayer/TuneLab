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
                if (item is PiecewiseAnchorItem deleteAnchorItem)
                {
                    deleteAnchorItem.Automation.DeselectAllAnchors();
                    deleteAnchorItem.Automation.DeletePoints([deleteAnchorItem.AnchorPoint]);
                }
                mPiecewiseAnchorDeleteOperation.Down(e.Position.X);
                break;
            default:
                break;
        }
    }

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
            mAutomation.Commit("Edit Automation");
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
            mAutomation.Commit("Edit Automation");
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
            mAutomation.Commit("Edit Automation");
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

            AutomationRenderer.Part.EndMergeDirty();
            if (mMoved || mKeepChangeWithoutMove)
            {
                AutomationRenderer.Part.Commit("Edit Automation");
            }
            else
            {
                AutomationRenderer.Part.Discard();
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
