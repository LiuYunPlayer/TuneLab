using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Animation;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.Extensions.Voices;
using Avalonia.Media;
using TuneLab.GUI.Input;
using TuneLab.GUI.Components;
using Avalonia;
using TuneLab.Base.Event;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;
using TuneLab.Utils;
using Avalonia.Controls;

namespace TuneLab.Views;

internal partial class AutomationRenderer : View
{
    public TickAxis TickAxis => mDependency.TickAxis;
    public IMidiPart? Part => mDependency.PartProvider.Object;

    public interface IDependency
    {
        IActionEvent PianoToolChanged { get; }
        event Action? ActiveAutomationChanged;
        event Action? VisibleAutomationChanged;
        IProvider<MidiPart> PartProvider { get; }
        TickAxis TickAxis { get; }
        string? ActiveAutomation { get; }
        bool IsAutomationVisible(string automationID);
        IReadOnlyList<string> VisibleAutomations { get; }
        PianoTool PianoTool { get; }
    }

    public AutomationRenderer(IDependency dependency)
    {
        mDependency = dependency;

        mMiddleDragOperation = new(this);
        mDrawOperation = new(this);
        mClearOperation = new(this);
        mVibratoAmplitudeOperation = new(this);

        mDependency.PartProvider.ObjectChanged.Subscribe(InvalidateVisual, s);
        mDependency.PartProvider.When(p => p.Automations.Modified).Subscribe(InvalidateVisual, s);
        mDependency.PartProvider.When(p => p.Vibratos.Modified).Subscribe(InvalidateVisual, s);
        mDependency.PartProvider.When(p => p.Pos.Modified).Subscribe(InvalidateVisual, s);
        mDependency.PianoToolChanged.Subscribe(Update, s);
        mDependency.ActiveAutomationChanged += InvalidateVisual;
        mDependency.VisibleAutomationChanged += InvalidateVisual;
        mDependency.TickAxis.AxisChanged += Update;
    }

    ~AutomationRenderer()
    {
        s.DisposeAll();
        mDependency.ActiveAutomationChanged -= InvalidateVisual;
        mDependency.VisibleAutomationChanged += InvalidateVisual;
        mDependency.TickAxis.AxisChanged -= Update;
    }

    protected override void OnRender(DrawingContext context)
    {
        context.FillRectangle(Colors.Black.Opacity(0.25).ToBrush(), this.Rect());
        if (Part == null)
            return;

        double step = 1;
        int n = (int)Math.Ceiling(Bounds.Width / step);
        if (n <= 0)
            return;

        double[] xs = new double[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = i * step;
        }

        double pos = Part.Pos.Value;
        var ticks = new double[n];
        for (int i = 0; i < n; i++)
        {
            ticks[i] = TickAxis.X2Tick(xs[i]) - pos;
        }

        double lineWidth = 1;

        void Draw(string automationID)
        {
            if (!Part.IsEffectiveAutomation(automationID))
                return;

            var config = Part.GetEffectiveAutomationConfig(automationID);
            double min = config.MinValue;
            double max = config.MaxValue;
            double range = max - min;
            double r = Bounds.Height / range;

            double[] values = Part.GetFinalAutomationValues(ticks, automationID);

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (max - values[i].Limit(min, max)) * r;
            }

            var points = new Point[n];
            for (int i = 0; i < n; i++)
            {
                points[i] = new(xs[i], values[i]);
            }

            context.DrawCurve(points, Color.Parse(config.Color), lineWidth);
        }

        var activeAutomation = mDependency.ActiveAutomation;
        foreach (var automation in mDependency.VisibleAutomations)
        {
            if (automation == activeAutomation)
                continue;

            if (!mDependency.IsAutomationVisible(automation))
                continue;

            Draw(automation);
        }

        context.FillRectangle(Colors.Black.Opacity(0.25).ToBrush(), this.Rect());

        if (activeAutomation == null)
            return;

        if (!Part.IsEffectiveAutomation(activeAutomation))
            return;

        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;
        var config = Part.GetEffectiveAutomationConfig(activeAutomation);
        double min = config.MinValue;
        double max = config.MaxValue;
        foreach (var vibrato in Part.Vibratos)
        {
            if (vibrato.GlobalEndPos() <= minVisibleTick)
                continue;

            if (vibrato.GlobalStartPos() >= maxVisibleTick)
                break;

            double startX = TickAxis.Tick2X(vibrato.GlobalStartPos());
            double endX = TickAxis.Tick2X(vibrato.GlobalEndPos());
            double[] vxs = new double[(int)(endX - startX) + 1];
            for (int i = 0; i < vxs.Length; i++)
            {
                vxs[i] = startX + i;
            }

            var vticks = new double[vxs.Length];
            for (int i = 0; i < vxs.Length; i++)
            {
                vticks[i] = TickAxis.X2Tick(vxs[i]) - pos;
            }

            double range = max - min;
            double r = Bounds.Height / range;

            double[] values = Part.GetAutomationValues(vticks, activeAutomation);

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (max - values[i].Limit(min, max)) * r;
            }

            var points = new Point[vxs.Length];
            for (int i = 0; i < vxs.Length; i++)
            {
                points[i] = new(vxs[i], values[i]);
            }

            context.DrawCurve(points, Color.Parse(config.Color).Opacity(0.5), lineWidth);
        }

        if (IsHover && ItemAt(MousePosition) is VibratoItem vibratoItem)
        {
            if (!vibratoItem.Vibrato.AffectedAutomations.ContainsKey(activeAutomation))
            {
                var vibrato = vibratoItem.Vibrato;
                double x = TickAxis.Tick2X(vibrato.GlobalStartPos() + vibrato.Dur / 2);
                context.DrawString("Drag to associate the vibrato.", new Point(x, 8), Brushes.White, 12, Alignment.CenterTop);
            }
        }

        lineWidth = 2;
        Draw(activeAutomation);

        context.DrawString(max.ToString("+0.00;-0.00"), new Point(8, 12), Style.LIGHT_WHITE.ToBrush(), 12, Alignment.LeftCenter);
        context.DrawString(min.ToString("+0.00;-0.00"), new Point(8, Bounds.Height - 12), Style.LIGHT_WHITE.ToBrush(), 12, Alignment.LeftCenter);
    }

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
