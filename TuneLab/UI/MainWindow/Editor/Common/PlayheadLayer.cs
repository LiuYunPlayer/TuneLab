using Avalonia;
using Avalonia.Media;
using TuneLab.GUI.Components;
using TuneLab.Data;

namespace TuneLab.UI;

internal class PlayheadLayer : Component
{
    public interface IDependency
    {
        IPlayhead Playhead { get; }
        TickAxis TickAxis { get; }
    }

    public PlayheadLayer(IDependency dependency)
    {
        mDependency = dependency;

        IsHitTestVisible = false;

        Playhead.PosChanged.Subscribe(InvalidateVisual);
        TickAxis.AxisChanged += InvalidateVisual;
    }

    ~PlayheadLayer()
    {
        Playhead.PosChanged.Unsubscribe(InvalidateVisual);
        TickAxis.AxisChanged -= InvalidateVisual;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.White, new Rect(TickAxis.Tick2X(Playhead.Pos), 0, 1, Bounds.Height));
    }

    IPlayhead Playhead => mDependency.Playhead;
    TickAxis TickAxis => mDependency.TickAxis;

    readonly IDependency mDependency;
}
