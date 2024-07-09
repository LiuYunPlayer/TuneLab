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
using TuneLab.Base.Data;
using Avalonia.Controls;
using TuneLab.Utils;
using TuneLab.I18N;

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
        bool alt = (e.KeyModifiers & ModifierKeys.Alt) != 0;
        switch (mState)
        {
            case State.None:
                var item = ItemAt(e.Position);
                switch (e.MouseButtonType)
                {
                    case MouseButtonType.PrimaryButton:
                        {
                            if (item is TempoItem tempoItem)
                            {
                                if (e.IsDoubleClick)
                                {
                                    EnterInputBpm(tempoItem.Tempo);
                                }
                                else
                                {
                                    mTempoMovingOperation.Down(e.Position.X, tempoItem);
                                }
                            }
                            else
                            {
                                mSeekOperation.Down(e.Position.X);
                            }
                        }
                        break;
                    case MouseButtonType.SecondaryButton:
                        {
                            if (Timeline == null)
                                break;

                            if (item is TempoItem tempoItem)
                            {
                                var menu = new ContextMenu();
                                {
                                    var menuItem = new MenuItem().SetName("Edit Tempo".Tr(TC.Menu)).SetAction(() =>
                                    {
                                        EnterInputBpm(tempoItem.Tempo);
                                    });
                                    menu.Items.Add(menuItem);
                                }
                                if (tempoItem.TempoIndex != 0)
                                {
                                    var menuItem = new MenuItem().SetName("Delete Tempo".Tr(TC.Menu)).SetAction(() =>
                                    {
                                        Timeline.TempoManager.RemoveTempoAt(tempoItem.TempoIndex);
                                        Timeline.TempoManager.Project.Commit();
                                    });
                                    menu.Items.Add(menuItem);
                                }
                                menu.Open(this);
                            }
                            else
                            {
                                var pos = TickAxis.X2Tick(e.Position.X);
                                if (!alt) pos = GetQuantizedTick(pos);
                                var menu = new ContextMenu();
                                var menuItem = new MenuItem().SetName("Add Tempo".Tr(TC.Menu)).SetAction(() =>
                                {
                                    var bpm = Timeline.TempoManager.GetBpmAt(pos);
                                    Timeline.TempoManager.AddTempo(pos, bpm);
                                    Timeline.TempoManager.Project.Commit();
                                });
                                menu.Items.Add(menuItem);
                                menu.Open(this);
                            }
                        }
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
        bool alt = (e.KeyModifiers & ModifierKeys.Alt) != 0;
        switch (mState)
        {
            case State.Seeking:
                mSeekOperation.Move(e.Position.X);
                break;
            case State.TempoMoving:
                mTempoMovingOperation.Move(e.Position.X, alt);
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
            case State.TempoMoving:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mTempoMovingOperation.Up();
                break;
            default:
                break;
        }

        if (e.MouseButtonType == MouseButtonType.MiddleButton)
            mMiddleDragOperation.Up();
    }

    protected override void UpdateItems(IItemCollection items)
    {
        if (Timeline == null)
            return;

        double startPos = TickAxis.X2Tick(-48);
        double endPos = TickAxis.MaxVisibleTick;

        var tempoManager = Timeline.TempoManager;

        for (int i = 0; i < tempoManager.Tempos.Count; i++)
        {
            var tempo = tempoManager.Tempos[i];

            if (tempo.Pos < startPos)
                continue;

            if (tempo.Pos > endPos)
                break;

            items.Add(new TempoItem(this) { TempoManager = tempoManager, TempoIndex = i });
        }
    }

    class Operation(TimelineView timelineView)
    {
        public TimelineView TimelineView => timelineView;
        public State State { get => TimelineView.mState; set => TimelineView.mState = value; }
    }

    class MiddleDragOperation(TimelineView timelineView) : Operation(timelineView)
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

    class SeekOperation(TimelineView timelineView) : Operation(timelineView)
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

    class TempoMovingOperation(TimelineView timelineView) : Operation(timelineView)
    {
        public ITempo Tempo => mTempoItem.TempoManager.Tempos[mTempoIndexAfterMove];

        public void Down(double x, TempoItem tempoItem)
        {
            if (tempoItem.TempoIndex == 0)
                return;

            State = State.TempoMoving;
            mTempoItem = tempoItem;
            mTempoIndexAfterMove = tempoItem.TempoIndex;
            mOffset = x - tempoItem.Left;

            mTempoItem.TempoManager.Project.DisableAutoPrepare();
            mHead = tempoItem.TempoManager.Head;

            TimelineView.InvalidateVisual();
        }

        public void Move(double x, bool alt)
        {
            mTempoItem.TempoManager.DiscardTo(mHead);

            double pos = TimelineView.TickAxis.X2Tick(x - mOffset);
            if (!alt) pos = TimelineView.GetQuantizedTick(pos);
            double bpm = mTempoItem.Tempo.Bpm;

            mTempoItem.TempoManager.Project.BeginMergeReSegment();
            mTempoItem.TempoManager.RemoveTempoAt(mTempoItem.TempoIndex);
            mTempoIndexAfterMove = mTempoItem.TempoManager.AddTempo(pos, bpm);
            mTempoItem.TempoManager.Project.EndMergeReSegment();
        }

        public void Up()
        {
            State = State.None;

            var head = mTempoItem.TempoManager.Head;

            mTempoItem.TempoManager.Project.EnableAutoPrepare();
            if (head == mHead)
            {
                mTempoItem.TempoManager.Discard();
            }
            else
            {
                mTempoItem.TempoManager.Commit();
            }

            TimelineView.InvalidateVisual();
        }

        Head mHead;
        TempoItem mTempoItem;
        int mTempoIndexAfterMove;
        double mOffset;
    }

    readonly TempoMovingOperation mTempoMovingOperation;

    enum State
    {
        None,
        Seeking,
        TempoMoving,
    }

    State mState = State.None;
}
