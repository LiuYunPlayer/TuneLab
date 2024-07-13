using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI;
using TuneLab.Base.Science;
using TuneLab.Animation;

namespace TuneLab.UI;

internal class PitchAxis : AnimationScalableScrollAxis
{
    public double KeyHeight => Factor;
    public double MinVisiblePitch => Pos2Pitch(MaxVisiblePos);
    public double MaxVisiblePitch => Pos2Pitch(MinVisiblePos);

    public PitchAxis()
    {
        ContentSize = MusicTheory.PITCH_COUNT;
        Factor = ScaleLevel2Factor(ScaleLevel);
        PivotPos = Pitch2Pos(DEFAULT_VIEW_MID_PITCH + 0.5);
        MovePivotToCoor(0);
        ViewPivotRatio = 0.5;
    }
    public double Pitch2Y(double pitch)
    {
        return Pos2Coor(Pitch2Pos(pitch));
    }
    public double Y2Pitch(double y)
    {
        return Pos2Pitch(Coor2Pos(y));
    }
    public void MovePitchToY(double pitch, double y)
    {
        MovePosToCoor(Pitch2Pos(pitch), y);
    }
    public void AnimateMovePitchToY(double pitch, double y, double millisec = 150, IAnimationCurve? curve = null)
    {
        AnimateMovePosToCoor(Pitch2Pos(pitch), y, millisec, curve);
    }

    protected override double ScaleLevel2Factor(double level)
    {
        return DEFAULT_KEY_HEIGHT * Math.Pow(SAMPLE_KEY_HEIGHT / DEFAULT_KEY_HEIGHT, ScaleLevel / SAMPLE_SCALE_LEVEL); ;
    }

    protected override double MinScaleLevel => MIN_SCALE_LEVEL;
    protected override double MaxScaleLevel => MAX_SCALE_LEVEL;

    double Pitch2Pos(double pitch) { return MusicTheory.MAX_PITCH + 1 - pitch; }
    double Pos2Pitch(double pos) { return MusicTheory.MAX_PITCH + 1 - pos; }

    const double MIN_SCALE_LEVEL = -4;
    const double MAX_SCALE_LEVEL = 12;
    const double DEFAULT_KEY_HEIGHT = 28;
    const double SAMPLE_KEY_HEIGHT = 14;
    const double SAMPLE_SCALE_LEVEL = -4;

    const double DEFAULT_VIEW_MID_PITCH = MusicTheory.C0_PITCH - MusicTheory.MIN_PITCH + 12 * 4;
}
