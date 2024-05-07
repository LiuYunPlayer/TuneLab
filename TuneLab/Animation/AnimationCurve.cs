using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Animation;

internal interface IAnimationCurve
{
    double GetRatio(double timeRatio);
}

internal class AnimationCurve : IAnimationCurve
{
    public static readonly AnimationCurve Linear = new(x => x);
    public static readonly AnimationCurve QuadIn = new(x => x * x);
    public static readonly AnimationCurve QuadOut = new(x => 1 - (x - 1) * (x - 1));
    public static readonly AnimationCurve CubicIn = new(x => x * x * x);
    public static readonly AnimationCurve CubicOut = new(x => 1 + (x - 1) * (x - 1) * (x - 1));
    //public static readonly AnimationCurve CubicInOut;
    //public static readonly AnimationCurve BounceOut;
    //public static readonly AnimationCurve BounceIn;
    //public static readonly AnimationCurve SpringIn;
    //public static readonly AnimationCurve SpringOut;
    //public static readonly AnimationCurve SinOut;
    //public static readonly AnimationCurve SinIn;
    public static readonly AnimationCurve SinInOut = new(x => (1 - Math.Cos(x * Math.PI)) * 0.5);
    public static AnimationCurve Default => CubicOut;

    public static implicit operator AnimationCurve(Func<double, double> funcGetRatio)
    {
        return new AnimationCurve(funcGetRatio);
    }

    public AnimationCurve(Func<double, double> funcGetRatio)
    {
        mGetRatio = funcGetRatio;
    }

    public double GetRatio(double timeRatio)
    {
        return mGetRatio(timeRatio);
    }

    readonly Func<double, double> mGetRatio;
}
