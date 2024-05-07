using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.GUI;
using TuneLab.Animation;
using TuneLab.Data;
using TuneLab.Utils;
using TuneLab.Base.Utils;

namespace TuneLab.Views;

internal class TickAxis : AnimationScalableScrollAxis
{
    public double PixelsPerTick => Factor;
    public double TicksPerPixel => 1 / Factor;
    public double MinVisibleTick => Pos2Tick(MinVisiblePos);
    public double MaxVisibleTick => Pos2Tick(MaxVisiblePos);

    public TickAxis()
    {
        ContentSize = int.MaxValue;
        Factor = ScaleLevel2Factor(ScaleLevel);
    }

    ~TickAxis()
    {
        s.DisposeAll();
    }

    public double Tick2X(double tick)
    {
        return Pos2Coor(tick);
    }
    public double X2Tick(double x)
    {
        return Coor2Pos(x);
    }

    public void MoveTickToX(double tick, double x)
    {
        MovePosToCoor(Tick2Pos(tick), x);
    }

    public void AnimateMoveTickToX(double tick, double x, double millisec = 150, IAnimationCurve? curve = null)
    {
        AnimateMovePosToCoor(Tick2Pos(tick), x, millisec, curve);
    }

    protected override double ScaleLevel2Factor(double level)
    {
        return DEFAULT_PPT * Math.Pow(SAMPLE_PPT / DEFAULT_PPT, ScaleLevel / SAMPLE_SCALE_LEVEL);
    }

    protected override double MinScaleLevel => MIN_SCALE_LEVEL;
    protected override double MaxScaleLevel => MAX_SCALE_LEVEL;

    double Tick2Pos(double tick) => tick;
    double Pos2Tick(double pos) => pos;

    const double MIN_SCALE_LEVEL = -8;
    const double MAX_SCALE_LEVEL = 8;
    const double DEFAULT_PPT = 0.25;
    const double SAMPLE_PPT = 4;
    const double SAMPLE_SCALE_LEVEL = 8;

    readonly DisposableManager s = new();
}
