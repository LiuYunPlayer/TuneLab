using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Animation;

internal abstract class AnimationProperty<T> where T : struct
{
    public event Action? ValueChanged;

    public static implicit operator T(AnimationProperty<T> p)
    {
        return p.Value;
    }

    public T Value { get; set; } = default;


    public AnimationProperty()
    {
        mAnimationController.ValueChanged += (value) => { Value = Lerp(mStart, mEnd, value); ValueChanged?.Invoke(); };
    }

    public void SetTo(T to, double millisec, IAnimationCurve? curve = null)
    {
        mStart = Value;
        mEnd = to;

        mAnimationController.SetFromTo(0, 1, millisec, curve);
    }

    protected abstract T Lerp(T t1, T t2, double ratio);

    T mStart;
    T mEnd;
    readonly AnimationController mAnimationController = new();
}
