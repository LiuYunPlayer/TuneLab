using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Science;

namespace TuneLab.Animation;

internal class AnimationValue : AnimationProperty<double>
{
    protected override double Lerp(double t1, double t2, double ratio)
    {
        return MathUtility.Lerp(t1, t2, ratio);
    }
}
