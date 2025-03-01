using System;

namespace TuneLab.Animation;

internal interface IAnimationPath
{
    double GetValue(double millisec);
    void Translate(double distance);
}

internal class AnimationPath : IAnimationPath
{
    public AnimationPath(Func<double, double> funcGetValue)
    {
        mGetValue = funcGetValue;
    }

    public double GetValue(double millisec)
    {
        return mGetValue(millisec) + mOffset;
    }

    public void Translate(double distance)
    {
        mOffset += distance;
    }

    readonly Func<double, double> mGetValue;
    double mOffset = 0;
}
