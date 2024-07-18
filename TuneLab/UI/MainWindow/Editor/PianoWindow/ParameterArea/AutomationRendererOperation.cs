using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
                var item = ItemAt(e.Position);
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
            case State.VibratoAmplitudeAdjusting:
                mVibratoAmplitudeOperation.Move(e.Position.Y);
                break;
            default:
                var item = ItemAt(e.Position);
                if (item is VibratoItem)
                {
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeNorthSouth);
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
            default:
                break;
        }
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

        Point ToTickAndValue(Avalonia.Point mousePosition)
        {
            return new(AutomationRenderer.TickAxis.X2Tick(mousePosition.X) - mAutomation!.Part.Pos.Value,
                mMax - (mousePosition.Y / AutomationRenderer.Bounds.Height) * (mMax - mMin));
        }

        IAutomation? mAutomation = null;
        double mMax;
        double mMin;
        bool mDirection;
        Head mHead;
        readonly List<List<Point>> mPointLines = new();
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
        VibratoAmplitudeAdjusting,
    }

    State mState = State.None;
}
