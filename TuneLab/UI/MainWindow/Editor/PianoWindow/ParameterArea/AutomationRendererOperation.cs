using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using TuneLab.Animation;
using TuneLab.Base.Data;
using TuneLab.Base.Structures;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.Base.Science;
using TuneLab.Utils;
using TuneLab.Extensions.Voices;
using TuneLab.Base.Utils;
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
                var item = ItemAt(e.Position);
                if (mDependency.PianoTool.Value == PianoTool.Anchor)
                {
                    Part?.Pitch.DeselectAllAnchors();
                    mDependency.PianoScrollView.InvalidateVisual();
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

                                var automationID = mDependency.ActiveAutomation;
                                if (automationID == null)
                                    break;

                                foreach (var vibrato in Part.Vibratos.AllSelectedItems())
                                {
                                    vibrato.AffectedAutomations.Remove(automationID);
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
                            mDrawOperation.Down(e.Position);
                            break;
                        case MouseButtonType.SecondaryButton:
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
        switch (mState)
        {
            case State.Drawing:
                mDrawOperation.Move(e.Position);
                break;
            case State.Clearing:
                mClearOperation.Move(e.Position.X);
                break;
            case State.AnchorSelecting:
                mAnchorSelectOperation.Move(e.Position);
                break;
            case State.AnchorDeleting:
                mAnchorDeleteOperation.Move(e.Position.X);
                break;
            case State.AnchorMoving:
                mAnchorMoveOperation.Move(e.Position);
                break;
            case State.VibratoAmplitudeAdjusting:
                mVibratoAmplitudeOperation.Move(e.Position.Y);
                break;
            default:
                var item = ItemAt(e.Position);
                if (item is VibratoItem)
                {
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeNorthSouth);
                }
                else if (item is AutomationAnchorItem)
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
            case State.Drawing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mDrawOperation.Up();
                break;
            case State.Clearing:
                if (e.MouseButtonType == MouseButtonType.SecondaryButton)
                    mClearOperation.Up();
                break;
            case State.AnchorSelecting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mAnchorSelectOperation.Up();
                break;
            case State.AnchorDeleting:
                if (e.MouseButtonType == MouseButtonType.SecondaryButton)
                    mAnchorDeleteOperation.Up();
                break;
            case State.AnchorMoving:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mAnchorMoveOperation.Up();
                break;
            case State.VibratoAmplitudeAdjusting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mVibratoAmplitudeOperation.Up();
                break;
            default:
                break;
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
                var automationID = mDependency.ActiveAutomation;
                if (automationID == ConstantDefine.VibratoEnvelopeID)
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

                if (!Part.Automations.TryGetValue(activeAutomation, out var automation))
                    break;

                var config = Part.GetEffectiveAutomationConfig(activeAutomation);
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

        var automationID = mDependency.ActiveAutomation;
        if (automationID == null || !Part.IsEffectiveAutomation(automationID))
            return false;

        config = Part.GetEffectiveAutomationConfig(automationID);
        if (!Part.Automations.TryGetValue(automationID, out automation) && createIfMissing)
        {
            automation = Part.AddAutomation(automationID);
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

    class DrawOperation(AutomationRenderer automationRenderer) : Operation(automationRenderer)
    {
        [MemberNotNullWhen(true, nameof(mAutomation))]
        public bool IsOperating => mAutomation != null && State == State.Drawing;

        public void Down(Avalonia.Point mousePosition)
        {
            if (IsOperating)
                return;

            if (AutomationRenderer.Part == null)
                return;

            var automationID = AutomationRenderer.mDependency.ActiveAutomation;
            if (automationID == null)
                return;

            if (!AutomationRenderer.Part.Automations.TryGetValue(automationID, out mAutomation))
            {
                mAutomation = AutomationRenderer.Part.AddAutomation(automationID);
            }

            if (mAutomation == null)
                return;

            State = State.Drawing;
            AutomationRenderer.Part.BeginMergeDirty();
            mHead = mAutomation.Head;
            var config = AutomationRenderer.Part.GetEffectiveAutomationConfig(automationID);
            mMin = config.MinValue;
            mMax = config.MaxValue;

            mPointLines.Add([ToTickAndValue(mousePosition)]);
            mAutomation.AddLine(mPointLines[0], Settings.ParameterBoundaryExtension);
        }

        public void Move(Avalonia.Point mousePosition)
        {
            if (!IsOperating)
                return;

            var point = ToTickAndValue(mousePosition);
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

        TuneLab.Base.Structures.Point ToTickAndValue(Avalonia.Point mousePosition)
        {
            return new(AutomationRenderer.TickAxis.X2Tick(mousePosition.X) - mAutomation!.Part.Pos.Value,
                mMax - (mousePosition.Y / AutomationRenderer.Bounds.Height) * (mMax - mMin));
        }

        IAutomation? mAutomation = null;
        double mMax;
        double mMin;
        bool mDirection;
        Head mHead;
        readonly List<List<TuneLab.Base.Structures.Point>> mPointLines = new();
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

            var automationID = AutomationRenderer.mDependency.ActiveAutomation;
            if (automationID == null)
                return;

            if (!AutomationRenderer.Part.Automations.TryGetValue(automationID, out mAutomation))
                return;

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
        public bool IsOperating => State == State.AnchorSelecting;

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
            AutomationRenderer.Part.DisableAutoPrepare();
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

            AutomationRenderer.Part.EnableAutoPrepare();
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

            var automationID = AutomationRenderer.mDependency.ActiveAutomation;
            if (automationID == null)
                return;

            if (!AutomationRenderer.Part.IsEffectiveAutomation(automationID))
                return;

            State = State.VibratoAmplitudeAdjusting;
            AutomationRenderer.Part.DisableAutoPrepare();
            mVibratos = vibratos;
            mAutomationID = automationID;
            foreach (var vibrato in mVibratos)
            {
                if (!vibrato.AffectedAutomations.ContainsKey(automationID))
                    vibrato.AffectedAutomations.Add(automationID, 0);
            }
            mHead = AutomationRenderer.Part.Head;
            var config = AutomationRenderer.Part.GetEffectiveAutomationConfig(automationID);
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
            AutomationRenderer.Part.EnableAutoPrepare();
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
    }

    State mState = State.None;
}
