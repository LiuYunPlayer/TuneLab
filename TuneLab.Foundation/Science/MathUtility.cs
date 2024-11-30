using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Utils;

namespace TuneLab.Foundation.Science;

public static class MathUtility
{
    public static int Floor(this double value)
    {
        return (int)Math.Floor(value);
    }

    public static int Ceil(this double value)
    {
        return (int)Math.Ceiling(value);
    }

    public static int Round(this double value)
    {
        return (int)Math.Round(value);
    }

    public static int Limit(this int value, int min, int max)
    {
        return Math.Max(Math.Min(value, max), min);
    }

    public static double Limit(this double value, double min, double max)
    {
        return Math.Max(Math.Min(value, max), min);
    }

    public static int PositiveMod(int number, int divisor)
    {
        int mod = number % divisor;
        return mod < 0 ? mod + divisor : mod;
    }

    public static double Lerp(double v1, double v2, double ratio)
    {
        return v1 + (v2 - v1) * ratio;
    }

    public static double CubicInterpolation(double r)
    {
        return (3 - 2 * r) * r * r;
    }

    public static double[] LinearInterpolation(this IReadOnlyList<Point> points, IReadOnlyList<double> xs)
    {
        double[] ys = new double[xs.Count];

        if (points.Count == 0)
        {
            for (int i = 0; i < xs.Count; i++)
            {
                ys[i] = 0;
            }

            return ys;
        }

        if (points.Count == 1)
        {
            var y0 = points[0].Y;
            for (int i = 0; i < xs.Count; i++)
            {
                ys[i] = y0;
            }

            return ys;
        }

        int pointIndex = 0;
        for (int i = 0; i < xs.Count; i++)
        {
            while (pointIndex < points.Count && points[pointIndex].X < xs[i])
            {
                pointIndex++;
            }

            if (pointIndex == 0)
            {
                ys[i] = points[0].Y;
                continue;
            }
            else if (pointIndex == points.Count)
            {
                ys[i] = points[points.Count - 1].Y;
                continue;
            }

            ys[i] = LineValue(points[pointIndex - 1], points[pointIndex], xs[i]);
        }

        return ys;
    }

    public static double[] MonotonicHermiteInterpolation(this IReadOnlyList<Point> points, IReadOnlyList<double> xs)
    {
        return HermiteInterpolation.Calculate(points, xs, mMonotonicHermiteSlopeCalculator);
    }

    public static List<Point> Simplify(this IReadOnlyList<Point> points, double gap, double tolerance)
    {
        if (points.Count <= 2)
            return points.ToList();

        var last = points.ConstFirst();
        var result = new List<Point>() { last };
        for (int i = 1; i < points.Count - 1; i++)
        {
            var point = points[i];
            var next = points[i + 1];
            var kLast = Slope(point, last);
            var kNext = Slope(point, next);
            if (kLast == 0 && kNext == 0)
                continue;

            if (point.X - last.X < gap)
            {
                if (kLast * kNext > 0)
                {
                    if (Math.Abs(Math.Log2(kLast / kNext)) < tolerance)
                        continue;
                }
            }

            last = point;
            result.Add(point);
        }
        result.Add(points.ConstLast());

        return result;
    }

    public static List<Point> Simplify(this IReadOnlyList<Point> points, double gap)
    {
        if (points.Count <= 2)
            return points.ToList();

        var first = points.ConstFirst();
        var last = points.ConstLast();
        int start = (first.X / gap).Floor() + 1;
        int end = (last.X / gap).Ceil() - 1;
        int count = end - start + 1;
        if (count <= 0)
            return points.ToList();

        var xs = new double[count];
        for (int i = start; i <= end; i++)
        {
            xs[i - start] = i * gap;
        }
        var values = points.LinearInterpolation(xs);
        var result = new List<Point>();
        result.Add(first);
        for (int i = start; i <= end; i++)
        {
            result.Add(new(xs[i - start], values[i - start]));
        }
        result.Add(last);
        return result;
    }

    public static List<Point> Simplify(this IReadOnlyList<Point> points, double start, double end, double gap)
    {
        var simplifiedPoints = new List<Point>();
        if (start >= points.Last().X || end <= points.First().X)
            return simplifiedPoints;

        if (points.Count <= 1)
        {
            for (int i = 0; i < points.Count; i++)
            {
                simplifiedPoints.Add(points[i]);
            }
            return simplifiedPoints;
        }

        int pointIndex = 1;
        while (points[pointIndex].X <= start)
        {
            pointIndex++;
        }
        var last = points[pointIndex - 1];
        var next = points[pointIndex];
        bool lastDirection = next.Y > last.Y;
        var lastPoint = last;
        simplifiedPoints.Add(lastPoint);
        pointIndex++;
        for (; pointIndex < points.Count; pointIndex++)
        {
            last = next;
            if (last.X >= end)
            {
                simplifiedPoints.Add(last);
                return simplifiedPoints;
            }

            next = points[pointIndex];
            bool direction = next.Y > last.Y;
            if (direction != lastDirection)
            {
                lastPoint = last;
                simplifiedPoints.Add(lastPoint);
                lastDirection = direction;
                continue;
            }

            if (next.X - lastPoint.X >= gap)
            {
                lastPoint = last;
                simplifiedPoints.Add(lastPoint);
                continue;
            }
        }

        simplifiedPoints.Add(last);
        return simplifiedPoints;
    }

    public static List<Point> Simplify(this IReadOnlyList<Point> points, IReadOnlyList<double> xs)
    {
        var simplifiedPoints = new List<Point>();
        if (xs.Count <= 1)
            return simplifiedPoints;

        double start = xs.First();
        double end = xs.Last();
        if (start >= points.Last().X || end <= points.First().X)
            return simplifiedPoints;

        if (points.Count <= 1)
        {
            for (int i = 0; i < points.Count; i++)
            {
                simplifiedPoints.Add(points[i]);
            }
            return simplifiedPoints;
        }

        int pointIndex = 1;
        while (pointIndex < points.Count && points[pointIndex].X <= start)
        {
            pointIndex++;
        }
        var last = points[pointIndex - 1];
        var next = points[pointIndex];
        simplifiedPoints.Add(last);
        bool lastDirection = next.Y > last.Y;
        pointIndex++;
        for (; pointIndex < points.Count - 1; pointIndex++)
        {
            last = next;
            next = points[pointIndex];
            bool direction = next.Y > last.Y;
        }
        for (int i = 0; i < xs.Count; i++)
        {
            while (pointIndex < points.Count && points[pointIndex].X < xs[i])
            {
                last = next;
                next = points[pointIndex];
                bool direction = next.Y > last.Y;
                if (direction != lastDirection)
                    simplifiedPoints.Add(last);
                pointIndex++;
            }

            if (pointIndex == points.Count)
                return simplifiedPoints;

            simplifiedPoints.Add(new(xs[i], LineValue(points[pointIndex - 1], points[pointIndex], xs[i])));
        }

        return simplifiedPoints;
    }

    public static double LineValue(double x1, double y1, double x2, double y2, double x)
    {
        if (x1 == x2)
            return double.NaN;

        return Lerp(y1, y2, (x - x1) / (x2 - x1));
    }

    public static double LineValue(Point p1, Point p2, double x)
    {
        return LineValue(p1.X, p1.Y, p2.X, p2.Y, x);
    }

    public static double Slope(Point p1, Point p2)
    {
        return (p2.Y - p1.Y) / (p2.X - p1.X);
    }

    public static Point PerpendicularFoot(Point p1, Point p2, Point p)
    {
        double a = p2.Y - p1.Y;
        double b = p1.X - p2.X;
        double c = p2.X * p1.Y - p1.X * p2.Y;

        double s = a * a + b * b;

        double x = (b * b * p.X - a * b * p.Y - a * c) / s;
        double y = (a * a * p.Y - a * b * p.X - b * c) / s;
        return new Point(x, y);
    }

    public static double Distance(Point p1, Point p2)
    {
        double x = p1.X - p2.X;
        double y = p1.Y - p2.Y;
        return Math.Sqrt(x * x + y * y);
    }

    public static int Factorial(int n)
    {
        int product = 1;
        for (int i = 1; i <= n; i++)
        {
            product *= i;
        }
        return product;
    }

    public static double Sinc(double x)
    {
        return Math.Sin(x) / x;
    }

    public static double[] KaiserWin(int length, double beta)
    {
        double[] win = new double[length];
        for (int i = 0; i < length; i++)
        {
            win[i] = I0(20, beta * Math.Sqrt(1 - Math.Pow(2.0 * i / (length - 1) - 1, 2))) / I0(20, beta);
        }
        return win;
    }

    public static FastSinc GetFastSinc(int sincSamples)
    {
        if (!mFastSincs.TryGetValue(sincSamples, out var fastSinc))
        {
            fastSinc = new FastSinc(sincSamples);
            mFastSincs.Add(sincSamples, fastSinc);
        }

        return fastSinc;
    }

    static double I0(int n, double x)
    {
        double I0_x = 1.0;

        for (int i = 1; i <= n; i++)
        {
            I0_x += Math.Pow(Math.Pow(x / 2, i) / Factorial(i), 2);
        }
        return I0_x;
    }

    static MonotonicHermiteSlopeCalculator mMonotonicHermiteSlopeCalculator = new();
    static Dictionary<int, FastSinc> mFastSincs = new();
}
