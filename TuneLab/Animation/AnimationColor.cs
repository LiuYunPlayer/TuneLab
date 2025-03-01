using Avalonia.Media;
using TuneLab.Utils;

namespace TuneLab.Animation;

internal class AnimationColor : AnimationProperty<Color>
{
    protected override Color Lerp(Color t1, Color t2, double ratio)
    {
        return t1.Lerp(t2, ratio);
    }
}
