using Avalonia.Media;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Science;
using TuneLab.GUI.Input;
using TuneLab.Utils;

namespace TuneLab.Views;

internal partial class PianoRoll
{
    protected override void OnScroll(WheelEventArgs e)
    {
        PitchAxis.AnimateScale(PitchAxis.Coor2Pos(e.Position.Y), e.Delta.Y);
    }

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        switch (e.MouseButtonType)
        {
            case MouseButtonType.MiddleButton:
                mMiddleDragOperation.Down(e.Position.Y);
                break;
            default:
                break;
        }
    }

    protected override void OnMouseAbsoluteMove(MouseMoveEventArgs e)
    {
        mMiddleDragOperation.Move(e.Position.Y);
    }

    protected override void OnMouseUp(MouseUpEventArgs e)
    {
        switch (e.MouseButtonType)
        {
            case MouseButtonType.MiddleButton:
                mMiddleDragOperation.Up();
                break;
            default:
                break;
        }
    }

    protected override void OnMouseRelativeMoveToView(MouseMoveEventArgs e)
    {
        InvalidateVisual();
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
        double hideHeight = PitchAxis.LargeEndHideLength;
        double keyHeight = PitchAxis.KeyHeight;
        double groupHeight = keyHeight * 12;
        double whiteKeyHeight = groupHeight / 7;
        double c0hide = hideHeight - (MusicTheory.C0_PITCH - MusicTheory.MIN_PITCH) * keyHeight;
        int minWhite = (int)Math.Floor(c0hide / whiteKeyHeight);
        int maxWhite = (int)Math.Ceiling((c0hide + Bounds.Height) / whiteKeyHeight);
        for (int i = minWhite; i < maxWhite; i++)
        {
            double bottom = PitchAxis.Pitch2Y(MusicTheory.C0_PITCH + (double)i * 12 / 7) - 0.5;
            double top = PitchAxis.Pitch2Y(MusicTheory.C0_PITCH + (double)(i + 1) * 12 / 7) + 0.5;
            items.Add(new WhiteKeyItem(this) { Rect = new Rect(-4, top, Bounds.Width + 4, bottom - top) });
        }

        int minBlack = (int)Math.Floor(PitchAxis.MinVisiblePitch);
        int maxBlack = (int)Math.Ceiling(PitchAxis.MaxVisiblePitch);
        for (int i = minBlack; i < maxBlack; i++)
        {
            if (MusicTheory.IsBlack(i))
            {
                double top = PitchAxis.Pitch2Y(i + 1);
                items.Add(new BlackKeyItem(this) { Rect = new Rect(0, top, 32, keyHeight) });
            }
        }
    
        int minText = (int)Math.Floor(c0hide / groupHeight);
        int maxText = (int)Math.Ceiling((c0hide + Bounds.Height) / groupHeight);
        for (int i = minText; i < maxText; i++)
        {
            double bottom = PitchAxis.Pitch2Y(MusicTheory.C0_PITCH + i * 12);
            items.Add(new TextItem(this) { Bottom = bottom, Text = "C" + i });
        }
    }

    class Operation
    {
        public PianoRoll PianoRoll => mPianoRoll;
        //public State State { get => PianoRoll.mState; set => PianoRoll.mState = value; }
        public Operation(PianoRoll pianoRoll)
        {
            mPianoRoll = pianoRoll;
        }

        readonly PianoRoll mPianoRoll;
    }

    class MiddleDragOperation(PianoRoll pianoRoll) : Operation(pianoRoll)
    {
        public bool IsOperating => mIsDragging;

        public void Down(double y)
        {
            if (mIsDragging)
                return;

            mIsDragging = true;
            mDownPitch = PianoRoll.PitchAxis.Y2Pitch(y);
            PianoRoll.PitchAxis.StopMoveAnimation();
        }

        public void Move(double y)
        {
            if (!mIsDragging)
                return;

            PianoRoll.PitchAxis.MovePitchToY(mDownPitch, y);
        }

        public void Up()
        {
            if (!mIsDragging)
                return;

            mIsDragging = false;
        }

        double mDownPitch;
        bool mIsDragging = false;
    }

    readonly MiddleDragOperation mMiddleDragOperation;
}
