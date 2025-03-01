using TuneLab.Foundation.Science;

namespace TuneLab.Animation;

internal interface IAnimationController : IAnimation
{
    void Play(double millisec, IAnimationPath path);
    void Translate(double distance);
}

internal static class IAnimationControllerExtension
{
    public static void SetFromTo(this IAnimationController animationController, double from, double to, double millisec, IAnimationCurve? curve = null)
    {
        curve ??= AnimationCurve.Default;
        animationController.Play(millisec, millisec <= 0 ? new AnimationPath((t) => to) : new AnimationPath((t) => MathUtility.Lerp(from, to, curve.GetRatio(t / millisec))));
    }
}
