using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

internal class CurveRenderer : Component
{
    public double Step
    {
        get => mStep;
        set
        {
            mStep = value;
            InvalidateVisual();
        }
    }
    public double LineWidth
    {
        get => mLineWidth;
        set
        {
            mLineWidth = value;
            InvalidateVisual();
        }
    }
    public Color LineColor
    {
        get => mLineColor;
        set
        {
            mLineColor = value;
            InvalidateVisual();
        }
    }
    public Color BackColor
    {
        get => mBackColor;
        set
        {
            mBackColor = value;
            InvalidateVisual();
        }
    }
    public Func<IReadOnlyList<double>, double[]>? GetOrdinates { get; set; }

    public override void Render(DrawingContext context)
    {
        if (GetOrdinates == null)
            return;

        if (BackColor.A != 0)
        {
            context.FillRectangle(BackColor.ToBrush(), this.Rect());
        }

        int n = (int)Math.Ceiling(Width / Step);
        if (n <= 0)
            return;

        double[] xs = new double[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = i * Step;
        }

        var ys = GetOrdinates(xs);
        var path = new PathGeometry();
        using (var pathContext = path.Open())
        {
            pathContext.BeginFigure(new(xs[0], ys[0]), false);
            for (int i = 1; i < n; i++)
            {
                pathContext.LineTo(new(xs[i], ys[i]));
            }
            pathContext.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(LineColor.ToBrush(), LineWidth, null, PenLineCap.Round, PenLineJoin.Round), path);

    }

    double mStep = 1;
    double mLineWidth = 1;
    Color mLineColor = Colors.White;
    Color mBackColor = Colors.Transparent;
}
