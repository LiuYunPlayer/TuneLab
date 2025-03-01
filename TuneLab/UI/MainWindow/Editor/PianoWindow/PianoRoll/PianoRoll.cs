using Avalonia.Media;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Utils;

namespace TuneLab.UI;

internal partial class PianoRoll : View
{
    public interface IDependency
    {
        PitchAxis PitchAxis { get; }
    }

    public PianoRoll(IDependency dependency)
    {
        mDependency = dependency;

        mMiddleDragOperation = new(this);
        mPlayKeySampleOperation = new(this);

        PitchAxis.AxisChanged += InvalidateVisual;
    }

    ~PianoRoll()
    {
        PitchAxis.AxisChanged -= InvalidateVisual;
    }

    protected override void OnRender(DrawingContext context)
    {
        context.FillRectangle(Style.BACK.ToBrush(), this.Rect());
    }

    PitchAxis PitchAxis => mDependency.PitchAxis;

    readonly IDependency mDependency;
}
