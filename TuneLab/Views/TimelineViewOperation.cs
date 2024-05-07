using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Base.Utils;
using TuneLab.Data;
using TuneLab.GUI.Input;
using TuneLab.Animation;

namespace TuneLab.Views;

internal partial class TimelineView
{
    protected override void OnScroll(WheelEventArgs e)
    {
        switch (e.KeyModifiers)
        {
            case ModifierKeys.Shift:
                TickAxis.AnimateMove(240 * e.Delta.Y);
                break;
            default:
                TickAxis.AnimateScale(TickAxis.Coor2Pos(e.Position.X), e.Delta.Y);
                break;
        }
    }

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        switch (mState)
        {
            case State.None:
                switch (e.MouseButtonType)
                {
                    case MouseButtonType.PrimaryButton:
                        mSeekOperation.Down(e.Position.X);
                        break;
                    default:
                        break;
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
            case State.Seeking:
                mSeekOperation.Move(e.Position.X);
                break;
            default:
                break;
        }
    }

    protected override void OnMouseUp(MouseUpEventArgs e)
    {
        switch (mState)
        {
            case State.Seeking:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mSeekOperation.Up();
                break;
            default:
                break;
        }

        if (e.MouseButtonType == MouseButtonType.MiddleButton)
            mMiddleDragOperation.Up();
    }

    class Operation(TimelineView pianoTimelineView)
    {
        public TimelineView TimelineView => pianoTimelineView;
        public State State { get => TimelineView.mState; set => TimelineView.mState = value; }
    }

    class MiddleDragOperation(TimelineView pianoTimelineView) : Operation(pianoTimelineView)
    {
        public bool IsOperating => mIsDragging;

        public void Down(double x)
        {
            if (mIsDragging)
                return;

            mIsDragging = true;
            mDownTick = TimelineView.TickAxis.X2Tick(x);
            TimelineView.TickAxis.StopMoveAnimation();
        }

        public void Move(double x)
        {
            if (!mIsDragging)
                return;

            TimelineView.TickAxis.MoveTickToX(mDownTick, x);
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

    class SeekOperation(TimelineView pianoTimelineView) : Operation(pianoTimelineView)
    {
        public bool IsOperating => State == State.Seeking;

        public void Down(double x)
        {
            if (State != State.None)
                return;

            State = State.Seeking;
            mIsPlaying = AudioEngine.IsPlaying;
            if (mIsPlaying) AudioEngine.Pause();
            Move(x);
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            TimelineView.Playhead.Pos = TimelineView.TickAxis.X2Tick(x);
            if (x <= 0)
            {
                if (!TimelineView.TickAxis.IsMoveAnimating)
                    TimelineView.TickAxis.AnimateRun(1920);
            }
            else if (x >= TimelineView.Bounds.Width)
            {
                if (!TimelineView.TickAxis.IsMoveAnimating)
                    TimelineView.TickAxis.AnimateRun(-1920);
            }
            else
            {
                TimelineView.TickAxis.StopMoveAnimation();
            }
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;
            TimelineView.TickAxis.StopMoveAnimation();
            if (mIsPlaying) AudioEngine.Play();
        }

        bool mIsPlaying;
    }

    readonly SeekOperation mSeekOperation;

    enum State
    {
        None,
        Seeking,
    }

    State mState = State.None;
}
