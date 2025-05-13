using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;

namespace TuneLab.Base.Science;

internal interface IHermiteSlopeCalculator
{
    double SlopeAt(IReadOnlyList<Point> points, int index);
}

internal class MonotonicHermiteSlopeCalculator : IHermiteSlopeCalculator
{
    public double SlopeAt(IReadOnlyList<Point> points, int index)
    {
        if (index == 0 || index == points.Count - 1)
            return 0;

        var point = points[index];
        var lastk = MathUtility.Slope(point, points[index - 1]);
        var nextk = MathUtility.Slope(point, points[index + 1]);
        var kk = lastk * nextk;

        return kk <= 0 ? 0 : 2 / (1 / lastk + 1 / nextk);
    }
}

internal static class HermiteInterpolation
{
    public static double[] Calculate(IReadOnlyList<Point> points, IReadOnlyList<double> xs, IHermiteSlopeCalculator slopeCalculator)
    {
        if (points.Count < 2)
            return points.LinearInterpolation(xs);

        double[] ys = new double[xs.Count];

        int pointIndex = 0;
        for (int i = 0; i < xs.Count; i++)
        {
            while (pointIndex < points.Count && points[pointIndex].X < xs[i])
            {
                pointIndex++;
            }

            Point last;
            Point next;
            double lastDelta;
            double nextDelta;
            if (pointIndex == 0)
            {
                ys[i] = points[0].Y;
                continue;
            }

            if (pointIndex == points.Count)
            {
                ys[i] = points[points.Count - 1].Y;
                continue;
            }

            last = points[pointIndex - 1];
            lastDelta = slopeCalculator.SlopeAt(points, pointIndex - 1);
            next = points[pointIndex];
            nextDelta = slopeCalculator.SlopeAt(points, pointIndex);

            double delta1 = xs[i] - last.X;
            double delta2 = xs[i] - next.X;
            double T1 = delta1 / (next.X - last.X);
            double T2 = delta2 / (last.X - next.X);
            ys[i] =
                F1(T1, T2) * last.Y +
                F2(T1, T2) * next.Y +
                F3(T2, delta1) * lastDelta +
                F4(T1, delta2) * nextDelta;
        }
        return ys;
    }

    static double F1(double T1, double T2)
    {
        return (1.0 + 2.0 * T1) * T2 * T2;
    }

    static double F2(double T1, double T2)
    {
        return (1.0 + 2.0 * T2) * T1 * T1;
    }

    static double F3(double T2, double D1)
    {
        return D1 * T2 * T2;
    }

    static double F4(double T1, double D2)
    {
        return D2 * T1 * T1;
    }
}
