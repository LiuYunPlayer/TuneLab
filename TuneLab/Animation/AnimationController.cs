using System;
using System.Diagnostics.CodeAnalysis;

namespace TuneLab.Animation;

internal class AnimationController : IAnimationController
{
    public event Action<double>? ValueChanged;
    [MemberNotNullWhen(true, nameof(mPath))]
    public bool IsPlaying => mPath != null;
    public double Destination => IsPlaying ? mPath.GetValue(mMillisec) : double.NaN;
    public AnimationController(AnimationManager? manager = null)
    {
        manager ??= AnimationManager.SharedManager;

        mAnimationManager = manager;
    }

    public void Play(double millisec, IAnimationPath path)
    {
        if (IsPlaying)
            Stop();

        mMillisec = millisec;
        mPath = path;
        Value = mPath.GetValue(0);
        mAnimationManager.StartAnimation(this);
    }

    public void Translate(double distance)
    {
        if (!IsPlaying)
            return;

        mPath.Translate(distance);
    }

    public void Stop()
    {
        if (!IsPlaying)
            return;

        mAnimationManager.StopAnimation(this);
        mPath = null;
    }

    void IAnimation.Update(double millisec)
    {
        if (!IsPlaying)
            return;

        if (millisec > mMillisec)
        {
            Value = Destination;
            Stop();
            return;
        }

        Value = mPath.GetValue(millisec);
    }

    double Value { set { ValueChanged?.Invoke(value); } }

    double mMillisec;
    IAnimationPath? mPath = null;

    readonly AnimationManager mAnimationManager;
}
