using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.Utils;
using TuneLab.Base.Science;

namespace TuneLab.Views;

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
