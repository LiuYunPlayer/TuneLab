using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using TuneLab.Animation;
using TuneLab.Foundation;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.Utils;
using TuneLab.SDK;
using TuneLab.Configs;

namespace TuneLab.UI;

internal partial class AutomationRenderer
{
    protected override void OnScroll(WheelEventArgs e)
    {
        switch (e.KeyModifiers)
        {
            case ModifierKeys.Shift:
                TickAxis.AnimateMove(240 * e.Delta.Y);
                break;
            case ModifierKeys.Ctrl:
                TickAxis.AnimateScale(TickAxis.Coor2Pos(e.Position.X), e.Delta.Y);
                break;
            default:
                break;
        }
    }

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        switch (mState)
        {
            case State.None:
                bool ctrl = (e.KeyModifiers & ModifierKeys.Ctrl) != 0;
                bool alt = (e.KeyModifiers & ModifierKeys.Alt) != 0;
                bool shift = (e.KeyModifiers & ModifierKeys.Shift) != 0;
                var item = ItemAt(e.Position);

                // Shift+主键拖 = 画范围选区（与音符区共用同一条 tick 带，零工具切换）。先于工具/锚点/绘制逻辑拦截。
                // 未拖(点击)的清空交给 OnMouseUp 的点击阈值统一判定（mPrimaryDownPos 记于此）。
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                {
                    mPrimaryDownPos = e.Position;
                    if (shift)
                    {
                        if (Part != null)
                            mRegionSelectionOperation.Down(e.Position.X, alt);
                        break;
                    }
                }

                // 右键 → 弹范围选区菜单：有激活选区 或 按住 Shift（同音符区；Shift 让"复制后清了区仍可粘贴"）。优先于工具右键。
                if (e.MouseButtonType == MouseButtonType.SecondaryButton && (mDependency.PianoScrollView.HasRegionSelection || shift))
                {
                    mDependency.PianoScrollView.OpenRegionMenu(e.Position.X);
                    break;
                }

                if (mDependency.PianoTool.Value == PianoTool.Anchor)
                {
                    Part?.Pitch.DeselectAllAnchors();
                    mDependency.PianoScrollView.InvalidateVisual();
                    if (ActiveIsPiecewise())
                    {
                        OnPiecewiseAnchorMouseDown(e, ctrl, item);
                        break;
                    }
                    switch (e.MouseButtonType)
                    {
                        case MouseButtonType.PrimaryButton:
                            if (item is AutomationAnchorItem anchorItem)
                            {
                                mAnchorMoveOperation.Down(e.Position, ctrl, anchorItem.Automation, anchorItem.AnchorPoint, anchorItem.MinValue, anchorItem.MaxValue);
                            }
                            else if (e.IsDoubleClick)
                            {
                                if (!TryGetActiveAutomation(out var automation, out var config, true))
                                    break;

                                var anchor = new AnchorPoint(TickAxis.X2Tick(e.Position.X) - automation.Part.Pos.Value, YToValue(e.Position.Y, config.MinValue, config.MaxValue)) { IsSelected = true };
                                automation.InsertPoint(anchor);
                                var insertedAnchor = automation.Points.FirstOrDefault(point => point.Pos == anchor.Pos);
                                if (insertedAnchor == null)
                                    break;

                                automation.Points.DeselectAllItems();
                                insertedAnchor.Select();
                                mAnchorMoveOperation.Down(e.Position, ctrl, automation, insertedAnchor, config.MinValue, config.MaxValue, true);
                            }
                            else
                            {
                                mAnchorSelectOperation.Down(e.Position, ctrl);
                            }
                            break;
                        case MouseButtonType.SecondaryButton:
                            if (item is AutomationAnchorItem deleteAnchorItem)
                            {
                                deleteAnchorItem.Automation.Points.DeselectAllItems();
                                deleteAnchorItem.Automation.DeletePoints([deleteAnchorItem.AnchorPoint]);
                            }
                            mAnchorDeleteOperation.Down(e.Position.X);
                            break;
                        default:
                            break;
                    }
                    break;
                }

                if (item is VibratoItem vibratoItem)
                {
                    switch (e.MouseButtonType)
                    {
                        case MouseButtonType.PrimaryButton:
                            if (e.IsDoubleClick)
                            {
                                if (Part == null)
                                    break;

                                var automationKey = mDependency.ActiveAutomation;
                                if (automationKey == null || automationKey.Value.IsEffect)
                                    break;

                                foreach (var vibrato in Part.Vibratos.AllSelectedItems())
                                {
                                    vibrato.AffectedAutomations.Remove(automationKey.Value.Id);
                                }
                                Part.Commit();
                            }
                            else
                            {
                                mVibratoAmplitudeOperation.Down(e.Position.Y, vibratoItem.Vibrato);
                            }
                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    switch (e.MouseButtonType)
                    {
                        case MouseButtonType.PrimaryButton:
                            // Ctrl = 定值绘制：锁住按下时的 y 画水平线（保持参数值不变的重要画法）。
                            if (ActiveIsPiecewise())
                                mPiecewiseDrawOperation.Down(e.Position, ctrl);
                            else
                                mDrawOperation.Down(e.Position, ctrl);
                            break;
                        case MouseButtonType.SecondaryButton:
                            if (ActiveIsPiecewise())
                                mPiecewiseClearOperation.Down(e.Position.X);
                            else
                                mClearOperation.Down(e.Position.X);
                            break;
                        default:
                            break;
                    }
                }
                break;
            default:
                break;
        }

        if (e.MouseButtonType == MouseButtonType.MiddleButton)
            mMiddleDragOperation.Down(e.Position.X);
    }

    protected override void OnMouseAbsoluteMove(MouseMoveEventArgs e)
    {
        if (mMiddleDragOperation.IsOperating)
            mMiddleDragOperation.Move(e.Position.X);
    }

    protected override void OnMouseRelativeMoveToView(MouseMoveEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & ModifierKeys.Ctrl) != 0;
        bool alt = (e.KeyModifiers & ModifierKeys.Alt) != 0;
        bool shift = (e.KeyModifiers & ModifierKeys.Shift) != 0;
        switch (mState)
        {
            case State.RegionSelecting:
                mRegionSelectionOperation.Move(e.Position.X, alt);
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Ibeam);
                break;
            case State.Drawing:
                if (mDrawOperation.IsOperating) mDrawOperation.Move(e.Position, ctrl);
                else mPiecewiseDrawOperation.Move(e.Position, ctrl);
                break;
            case State.Clearing:
                if (mClearOperation.IsOperating) mClearOperation.Move(e.Position.X);
                else mPiecewiseClearOperation.Move(e.Position.X);
                break;
            case State.AnchorSelecting:
                if (mAnchorSelectOperation.IsOperating) mAnchorSelectOperation.Move(e.Position);
                else mPiecewiseAnchorSelectOperation.Move(e.Position);
                break;
            case State.AnchorDeleting:
                if (mAnchorDeleteOperation.IsOperating) mAnchorDeleteOperation.Move(e.Position.X);
                else mPiecewiseAnchorDeleteOperation.Move(e.Position.X);
                break;
            case State.AnchorMoving:
                if (mPiecewiseAnchorMoveOperation.IsOperating) mPiecewiseAnchorMoveOperation.Move(e.Position);
                else mAnchorMoveOperation.Move(e.Position);
                break;
            case State.VibratoAmplitudeAdjusting:
                mVibratoAmplitudeOperation.Move(e.Position.Y);
                break;
            default:
                if (shift)
                {
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Ibeam);   // Shift = 画范围选区模式
                    break;
                }
                var item = ItemAt(e.Position);
                if (item is VibratoItem)
                {
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeNorthSouth);
                }
                else if (item is AutomationAnchorItem or PiecewiseAnchorItem)
                {
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeAll);
                }
                else
                {
                    Cursor = null;
                }
                break;
        }

        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseUpEventArgs e)
    {
        switch (mState)
        {
            case State.RegionSelecting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mRegionSelectionOperation.Up();
                break;
            case State.Drawing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                {
                    if (mDrawOperation.IsOperating) mDrawOperation.Up();
                    else mPiecewiseDrawOperation.Up();
                }
                break;
            case State.Clearing:
                if (e.MouseButtonType == MouseButtonType.SecondaryButton)
                {
                    if (mClearOperation.IsOperating) mClearOperation.Up();
                    else mPiecewiseClearOperation.Up();
                }
                break;
            case State.AnchorSelecting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                {
                    if (mAnchorSelectOperation.IsOperating) mAnchorSelectOperation.Up();
                    else mPiecewiseAnchorSelectOperation.Up();
                }
                break;
            case State.AnchorDeleting:
                if (e.MouseButtonType == MouseButtonType.SecondaryButton)
                {
                    if (mAnchorDeleteOperation.IsOperating) mAnchorDeleteOperation.Up();
                    else mPiecewiseAnchorDeleteOperation.Up();
                }
                break;
            case State.AnchorMoving:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                {
                    if (mPiecewiseAnchorMoveOperation.IsOperating) mPiecewiseAnchorMoveOperation.Up();
                    else mAnchorMoveOperation.Up();
                }
                break;
            case State.VibratoAmplitudeAdjusting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mVibratoAmplitudeOperation.Up();
                break;
            default:
                break;
        }

        // 主键点击(未拖，位移 ≤ 阈值)即清空范围选区。真拖(画区/绘制/选锚)不触发；右键不参与。镜像音符区。
        if (e.MouseButtonType == MouseButtonType.PrimaryButton)
        {
            var d = e.Position - mPrimaryDownPos;
            if (d.X * d.X + d.Y * d.Y <= ClickThreshold * ClickThreshold)
                mDependency.PianoScrollView.ClearRegionSelection();
        }

        if (mMiddleDragOperation.IsOperating)
            mMiddleDragOperation.Up();
    }

    protected override void OnMouseEnter(MouseEnterEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseLeaveEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void UpdateItems(IItemCollection items)
    {
        if (Part == null)
            return;

        double startPos = TickAxis.MinVisibleTick;
        double endPos = TickAxis.MaxVisibleTick;

        switch (mDependency.PianoTool.Value)
        {
            case PianoTool.Vibrato:
                var vibratoActive = mDependency.ActiveAutomation;
                if (vibratoActive == AutomationKey.Voice(ConstantDefine.VibratoEnvelopeID))
                    break;

                foreach (var vibrato in Part.Vibratos)
                {
                    if (vibrato.GlobalEndPos() < startPos)
                        continue;

                    if (vibrato.GlobalStartPos() > endPos)
                        break;

                    items.Add(new VibratoItem(this) { Vibrato = vibrato });
                }
                break;
            case PianoTool.Anchor:
                var activeAutomation = mDependency.ActiveAutomation;
                if (activeAutomation == null)
                    break;

                if (ActiveIsPiecewise())
                {
                    var piecewise = Part.GetEffectivePiecewiseAutomation(activeAutomation.Value);
                    if (piecewise == null)
                        break;

                    var pconfig = Part.GetEffectivePiecewiseAutomationConfig(activeAutomation.Value);
                    var pcolor = Color.Parse(pconfig.Color);
                    foreach (var group in piecewise.AnchorGroups)
                    {
                        foreach (var point in group)
                        {
                            double t = Part.Pos.Value + point.Pos;
                            if (t < startPos || t > endPos)
                                continue;

                            items.Add(new PiecewiseAnchorItem(this)
                            {
                                Automation = piecewise,
                                AnchorPoint = point,
                                MinValue = pconfig.MinValue,
                                MaxValue = pconfig.MaxValue,
                                Color = pcolor,
                            });
                        }
                    }
                    break;
                }

                var automation = Part.GetEffectiveAutomation(activeAutomation.Value);
                if (automation == null)
                    break;

                var config = Part.GetEffectiveAutomationConfig(activeAutomation.Value);
                var color = Color.Parse(config.Color);
                foreach (var point in automation.Points)
                {
                    double tick = Part.Pos.Value + point.Pos;
                    if (tick < startPos)
                        continue;

                    if (tick > endPos)
                        break;

                    items.Add(new AutomationAnchorItem(this)
                    {
                        Automation = automation,
                        AnchorPoint = point,
                        MinValue = config.MinValue,
                        MaxValue = config.MaxValue,
                        Color = color,
                    });
                }
                break;
            default:
                break;
        }
    }

    bool TryGetActiveAutomation([NotNullWhen(true)] out IAutomation? automation, [NotNullWhen(true)] out AutomationConfig? config, bool createIfMissing)
    {
        automation = null;
        config = null;
        if (Part == null)
            return false;

        var automationKey = mDependency.ActiveAutomation;
        if (automationKey == null || !Part.IsEffectiveAutomation(automationKey.Value))
            return false;

        config = Part.GetEffectiveAutomationConfig(automationKey.Value);
        automation = Part.GetEffectiveAutomation(automationKey.Value);
        if (automation == null && createIfMissing)
        {
            automation = Part.AddEffectiveAutomation(automationKey.Value);
        }

        return automation != null;
    }

    class Operation(AutomationRenderer automationRenderer)
    {
        public AutomationRenderer AutomationRenderer => automationRenderer;
        public State State { get => AutomationRenderer.mState; set => AutomationRenderer.mState = value; }
    }

    class MiddleDragOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        public bool IsOperating => mIsDragging;

        public void Down(double x)
        {
            if (mIsDragging)
                return;

            mIsDragging = true;
            mDownTick = AutomationRenderer.TickAxis.X2Tick(x);
            AutomationRenderer.TickAxis.StopMoveAnimation();
        }

        public void Move(double x)
        {
            if (!mIsDragging)
                return;

            AutomationRenderer.TickAxis.MoveTickToX(mDownTick, x);
        }

        public void Up()
        {
            if (!mIsDragging)
                return;

            mIsDragging = false;
        }

        double mDownTick;
        bool mIsDragging = false;
    }

    readonly MiddleDragOperation mMiddleDragOperation;

    // 参数区的范围选区操作：与音符区共用同一条 tick 带（状态归 PianoScrollView）。本操作只算 x→tick（吸附复用 PianoScrollView）、
    // 写进 PianoScrollView.SetRegionSelection；纵向(值轴)与范围无关、贯穿全高。未拖(点击)不建区——清/留交给 OnMouseUp 点击阈值。
    class RegionSelectionOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        public bool IsOperating => State == State.RegionSelecting;

        public void Down(double x, bool noSnap)
        {
            if (State != State.None || AutomationRenderer.Part == null)
                return;

            State = State.RegionSelecting;
            mDownTick = TickAt(x, noSnap);
            AutomationRenderer.mDependency.PianoScrollView.SetRegionSelection(mDownTick, mDownTick);
        }

        public void Move(double x, bool noSnap)
        {
            if (!IsOperating)
                return;

            AutomationRenderer.mDependency.PianoScrollView.SetRegionSelection(mDownTick, TickAt(x, noSnap));
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;
            var pianoScrollView = AutomationRenderer.mDependency.PianoScrollView;
            if (pianoScrollView.CurrentRegionSelection is { } sel && sel.EndTick <= sel.StartTick)
                pianoScrollView.ClearRegionSelection();
        }

        double TickAt(double x, bool noSnap)
        {
            double tick = AutomationRenderer.TickAxis.X2Tick(x);
            if (!noSnap)
                tick = AutomationRenderer.mDependency.PianoScrollView.GetQuantizedTick(tick);
            return Math.Max(0, tick);
        }

        double mDownTick;
    }

    readonly RegionSelectionOperation mRegionSelectionOperation;

    class DrawOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        [MemberNotNullWhen(true, nameof(mAutomation))]
        public bool IsOperating => mAutomation != null && State == State.Drawing;

        public void Down(Avalonia.Point mousePosition, bool constantValue)
        {
            if (IsOperating)
                return;

            if (AutomationRenderer.Part == null)
                return;

            var automationKey = AutomationRenderer.mDependency.ActiveAutomation;
            if (automationKey == null)
                return;

            mAutomation = AutomationRenderer.Part.GetEffectiveAutomation(automationKey.Value)
                ?? AutomationRenderer.Part.AddEffectiveAutomation(automationKey.Value);

            if (mAutomation == null)
                return;

            State = State.Drawing;
            AutomationRenderer.Part.BeginMergeDirty();
            mHead = mAutomation.Head;
            var config = AutomationRenderer.Part.GetEffectiveAutomationConfig(automationKey.Value);
            mMin = config.MinValue;
            mMax = config.MaxValue;
            mDownValue = mMax - (mousePosition.Y / AutomationRenderer.Bounds.Height) * (mMax - mMin);   // 锁定按下时的 y，供定值绘制

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
            {
                mAutomation.AddLine(line.Simplify(5, 2), Settings.ParameterBoundaryExtension);
            }
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            if (AutomationRenderer.Part == null)
                return;

            mAutomation.DiscardTo(mHead);
            foreach (var line in mPointLines)
            {
                mAutomation.AddLine(line.Simplify(5, 2), Settings.ParameterBoundaryExtension);
            }
            AutomationRenderer.Part.EndMergeDirty();
            mAutomation.Commit();
            mAutomation = null;
            mPointLines.Clear();
            State = State.None;
        }

        TuneLab.Foundation.Point ToTickAndValue(Avalonia.Point mousePosition, bool constantValue)
        {
            double value = constantValue ? mDownValue : mMax - (mousePosition.Y / AutomationRenderer.Bounds.Height) * (mMax - mMin);
            return new(AutomationRenderer.TickAxis.X2Tick(mousePosition.X) - mAutomation!.Part.Pos.Value, value);
        }

        IAutomation? mAutomation = null;
        double mMax;
        double mMin;
        double mDownValue;   // 定值绘制锁定的值（按下时捕获）
        bool mDirection;
        Head mHead;
        readonly List<List<TuneLab.Foundation.Point>> mPointLines = new();
    }

    readonly DrawOperation mDrawOperation;

    class ClearOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        [MemberNotNullWhen(true, nameof(mAutomation))]
        public bool IsOperating => mAutomation != null && State == State.Clearing;

        public void Down(double x)
        {
            if (IsOperating)
                return;

            if (AutomationRenderer.Part == null)
                return;

            var automationKey = AutomationRenderer.mDependency.ActiveAutomation;
            if (automationKey == null)
                return;

            mAutomation = AutomationRenderer.Part.GetEffectiveAutomation(automationKey.Value);
            if (mAutomation == null)
                return;

            State = State.Clearing;
            AutomationRenderer.Part.BeginMergeDirty();
            mHead = AutomationRenderer.Part.Head;
            double tick = AutomationRenderer.TickAxis.X2Tick(x) - mAutomation.Part.Pos.Value;
            mStart = tick;
            mEnd = tick;
            mAutomation.Clear(mStart, mEnd, Settings.ParameterBoundaryExtension);
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            mAutomation.DiscardTo(mHead);
            double tick = AutomationRenderer.TickAxis.X2Tick(x) - mAutomation.Part.Pos.Value;
            mStart = Math.Min(mStart, tick);
            mEnd = Math.Max(mEnd, tick);
            mAutomation.Clear(mStart, mEnd, Settings.ParameterBoundaryExtension);
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            if (AutomationRenderer.Part == null)
                return;

            mAutomation.DiscardTo(mHead);
            mAutomation.Clear(mStart, mEnd, Settings.ParameterBoundaryExtension);
            AutomationRenderer.Part.EndMergeDirty();
            mAutomation.Commit();
            mAutomation = null;
            State = State.None;
        }

        IAutomation? mAutomation = null;
        double mStart;
        double mEnd;
        Head mHead;
    }

    readonly ClearOperation mClearOperation;

    class AnchorSelectOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        // 须同时判 mAutomation：分段轨选区操作也用 State.AnchorSelecting，靠 mAutomation 区分是否本（连续）操作在跑。
        public bool IsOperating => State == State.AnchorSelecting && mAutomation != null;

        public void Down(Avalonia.Point point, bool ctrl)
        {
            if (State != State.None)
                return;

            if (!AutomationRenderer.TryGetActiveAutomation(out mAutomation, out var config, false))
                return;

            State = State.AnchorSelecting;
            mMinValue = config.MinValue;
            mMaxValue = config.MaxValue;
            mDefaultValue = mAutomation.DefaultValue.Value;
            mDownTick = AutomationRenderer.TickAxis.X2Tick(point.X) - mAutomation.Part.Pos.Value;
            mDownValue = AutomationRenderer.YToValue(point.Y, mMinValue, mMaxValue);
            if (ctrl)
            {
                mSelectedItems = mAutomation.Points.AllSelectedItems();
            }
            Move(point);
        }

        public void Move(Avalonia.Point point)
        {
            if (!IsOperating || mAutomation == null)
                return;

            mTick = AutomationRenderer.TickAxis.X2Tick(point.X) - mAutomation.Part.Pos.Value;
            mValue = AutomationRenderer.YToValue(point.Y, mMinValue, mMaxValue);
            mAutomation.Points.DeselectAllItems();
            if (mSelectedItems != null)
            {
                foreach (var item in mSelectedItems)
                    item.Select();
            }

            double minTick = Math.Min(mTick, mDownTick);
            double maxTick = Math.Max(mTick, mDownTick);
            double minValue = Math.Min(mValue, mDownValue);
            double maxValue = Math.Max(mValue, mDownValue);
            foreach (var pointItem in mAutomation.Points)
            {
                double value = pointItem.Value + mDefaultValue;
                if (pointItem.Pos >= minTick && pointItem.Pos <= maxTick && value >= minValue && value <= maxValue)
                    pointItem.Select();
            }

            AutomationRenderer.UpdateAnchorValueInput();
            AutomationRenderer.InvalidateVisual();
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;
            mSelectedItems = null;
            mAutomation = null;
            AutomationRenderer.UpdateAnchorValueInput();
            AutomationRenderer.InvalidateVisual();
        }

        public Rect SelectionRect()
        {
            if (mAutomation == null)
                return new Rect();

            double minTick = Math.Min(mTick, mDownTick);
            double maxTick = Math.Max(mTick, mDownTick);
            double minValue = Math.Min(mValue, mDownValue);
            double maxValue = Math.Max(mValue, mDownValue);
            double left = AutomationRenderer.TickAxis.Tick2X(mAutomation.Part.Pos.Value + minTick);
            double right = AutomationRenderer.TickAxis.Tick2X(mAutomation.Part.Pos.Value + maxTick);
            double top = AutomationRenderer.ValueToY(maxValue, mMinValue, mMaxValue);
            double bottom = AutomationRenderer.ValueToY(minValue, mMinValue, mMaxValue);
            return new Rect(left, top, right - left, bottom - top);
        }

        IAutomation? mAutomation;
        IReadOnlyCollection<AnchorPoint>? mSelectedItems;
        double mMinValue;
        double mMaxValue;
        double mDefaultValue;
        double mDownTick;
        double mDownValue;
        double mTick;
        double mValue;
    }

    readonly AnchorSelectOperation mAnchorSelectOperation;

    class AnchorDeleteOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        [MemberNotNullWhen(true, nameof(mAutomation))]
        public bool IsOperating => mAutomation != null && State == State.AnchorDeleting;

        public void Down(double x)
        {
            if (IsOperating)
                return;

            if (!AutomationRenderer.TryGetActiveAutomation(out mAutomation, out _, false))
                return;

            State = State.AnchorDeleting;
            AutomationRenderer.Part!.BeginMergeDirty();
            mHead = AutomationRenderer.Part.Head;
            double tick = AutomationRenderer.TickAxis.X2Tick(x) - mAutomation.Part.Pos.Value;
            mStart = tick;
            mEnd = tick;
            mAutomation.DeletePoints(mStart, mEnd);
            AutomationRenderer.UpdateAnchorValueInput();
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            mAutomation.DiscardTo(mHead);
            double tick = AutomationRenderer.TickAxis.X2Tick(x) - mAutomation.Part.Pos.Value;
            mStart = Math.Min(mStart, tick);
            mEnd = Math.Max(mEnd, tick);
            mAutomation.DeletePoints(mStart, mEnd);
            AutomationRenderer.UpdateAnchorValueInput();
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            if (AutomationRenderer.Part == null)
                return;

            mAutomation.DiscardTo(mHead);
            mAutomation.DeletePoints(mStart, mEnd);
            AutomationRenderer.Part.EndMergeDirty();
            mAutomation.Commit();
            mAutomation = null;
            State = State.None;
            AutomationRenderer.UpdateAnchorValueInput();
            AutomationRenderer.InvalidateVisual();
        }

        IAutomation? mAutomation = null;
        double mStart;
        double mEnd;
        Head mHead;
    }

    readonly AnchorDeleteOperation mAnchorDeleteOperation;

    class AnchorMoveOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        public void Down(Avalonia.Point point, bool ctrl, IAutomation automation, AnchorPoint anchor, double minValue, double maxValue, bool keepChangeWithoutMove = false)
        {
            if (AutomationRenderer.Part == null)
                return;

            mAutomation = automation;
            mAnchor = anchor;
            mCtrl = ctrl;
            mIsSelected = anchor.IsSelected;
            mKeepChangeWithoutMove = keepChangeWithoutMove;
            mMin = minValue;
            mMax = maxValue;
            if (!mCtrl && !mIsSelected)
            {
                mAutomation.Points.DeselectAllItems();
            }
            anchor.Select();

            State = State.AnchorMoving;
            AutomationRenderer.Part.BeginMergeDirty();
            mHead = AutomationRenderer.Part.Head;
            mXOffset = point.X - AutomationRenderer.TickAxis.Tick2X(AutomationRenderer.Part.Pos.Value + anchor.Pos);
            mYOffset = point.Y - AutomationRenderer.ValueToY(anchor.Value + automation.DefaultValue.Value, mMin, mMax);
            AutomationRenderer.UpdateAnchorValueInput();
            AutomationRenderer.InvalidateVisual();
        }

        public void Move(Avalonia.Point point)
        {
            var part = AutomationRenderer.Part;
            if (part == null || mAutomation == null || mAnchor == null)
                return;

            double pos = AutomationRenderer.TickAxis.X2Tick(point.X - mXOffset) - part.Pos.Value;
            double posOffset = pos - mAnchor.Pos;
            double value = AutomationRenderer.YToValue(point.Y - mYOffset, mMin, mMax);
            double valueOffset = value - (mAnchor.Value + mAutomation.DefaultValue.Value);

            mMoved = true;
            part.DiscardTo(mHead);
            mAutomation.MoveSelectedPoints(posOffset, valueOffset);
            AutomationRenderer.UpdateAnchorValueInput();
        }

        public void Up()
        {
            State = State.None;

            if (mAnchor == null || mAutomation == null)
                return;

            if (AutomationRenderer.Part == null)
                return;

            AutomationRenderer.Part.EndMergeDirty();
            if (mMoved || mKeepChangeWithoutMove)
            {
                AutomationRenderer.Part.Commit();
            }
            else
            {
                AutomationRenderer.Part.Discard();
                if (mCtrl)
                {
                    if (mIsSelected)
                    {
                        mAnchor.Inselect();
                    }
                }
                else
                {
                    mAutomation.Points.DeselectAllItems();
                    mAnchor.Select();
                }
            }

            mMoved = false;
            mKeepChangeWithoutMove = false;
            mAutomation = null;
            mAnchor = null;
            AutomationRenderer.UpdateAnchorValueInput();
            AutomationRenderer.InvalidateVisual();
        }

        IAutomation? mAutomation;
        AnchorPoint? mAnchor;
        bool mCtrl;
        bool mIsSelected;
        bool mMoved = false;
        bool mKeepChangeWithoutMove = false;
        double mXOffset;
        double mYOffset;
        double mMin;
        double mMax;
        Head mHead;
    }

    readonly AnchorMoveOperation mAnchorMoveOperation;

    class VibratoAmplitudeOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        public void Down(double y, Vibrato downVibrato)
        {
            if (AutomationRenderer.Part == null)
                return;

            if (!downVibrato.IsSelected)
            {
                AutomationRenderer.Part.Vibratos.DeselectAllItems();
                downVibrato.Select();
            }

            var vibratos = AutomationRenderer.Part.Vibratos.AllSelectedItems();
            if (vibratos.IsEmpty())
                return;

            var automationKey = AutomationRenderer.mDependency.ActiveAutomation;
            // vibrato 振幅调节只作用于 voice 自动化（effect 不参与颤音）。
            if (automationKey == null || automationKey.Value.IsEffect)
                return;

            if (!AutomationRenderer.Part.IsEffectiveAutomation(automationKey.Value))
                return;

            var automationID = automationKey.Value.Id;
            State = State.VibratoAmplitudeAdjusting;
            AutomationRenderer.Part.BeginMergeDirty();
            mVibratos = vibratos;
            mAutomationID = automationID;
            foreach (var vibrato in mVibratos)
            {
                if (!vibrato.AffectedAutomations.ContainsKey(automationID))
                    vibrato.AffectedAutomations.Add(automationID, 0);
            }
            mHead = AutomationRenderer.Part.Head;
            var config = AutomationRenderer.Part.GetEffectiveAutomationConfig(automationKey.Value);
            mMin = config.MinValue;
            mMax = config.MaxValue;
            mValue = ValueAt(y);
        }

        public void Move(double y)
        {
            if (AutomationRenderer.Part == null)
                return;

            if (mVibratos == null)
                return;

            AutomationRenderer.Part.DiscardTo(mHead);
            double value = ValueAt(y);
            double offset = value - mValue;
            foreach (var vibrato in mVibratos)
            {
                vibrato.AffectedAutomations[mAutomationID] = vibrato.AffectedAutomations[mAutomationID] + offset;
            }
        }

        public void Up()
        {
            if (AutomationRenderer.Part == null)
                return;

            State = State.None;
            var head = AutomationRenderer.Part.Head;
            AutomationRenderer.Part.EndMergeDirty();
            if (head == mHead)
            {
                AutomationRenderer.Part.Discard();
            }
            else
            {
                AutomationRenderer.Part.Commit();
            }
            mVibratos = null;
        }

        double ValueAt(double y)
        {
            return mMax - (y / AutomationRenderer.Bounds.Height) * (mMax - mMin);
        }

        IReadOnlyCollection<Vibrato>? mVibratos;
        string mAutomationID;
        double mValue;
        double mMax;
        double mMin;
        Head mHead;
    }

    readonly VibratoAmplitudeOperation mVibratoAmplitudeOperation;

    enum State
    {
        None,
        Drawing,
        Clearing,
        AnchorSelecting,
        AnchorDeleting,
        AnchorMoving,
        VibratoAmplitudeAdjusting,
        RegionSelecting,
    }

    State mState = State.None;

    // 主键按下点 + 点击判定阈值：抬起时位移 ≤ 阈值即视为"点击(未拖)"，用于清空范围选区。镜像音符区 PianoScrollViewOperation。
    Avalonia.Point mPrimaryDownPos;
    const double ClickThreshold = 4;
}
