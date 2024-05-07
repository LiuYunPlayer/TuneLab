using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
