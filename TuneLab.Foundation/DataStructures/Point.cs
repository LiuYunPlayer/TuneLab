using System;
using System.Diagnostics.CodeAnalysis;

namespace TuneLab.Foundation;

public struct Point(double x, double y) : IEquatable<Point>
{
    public double X = x;
    public double Y = y;

    public static Point operator +(Point p1, Point p2)
    {
        return new(p1.X + p2.X, p1.Y + p2.Y);
    }
    public static Point operator -(Point p1, Point p2)
    {
        return new(p1.X - p2.X, p1.Y - p2.Y);
    }

    // == / != 保留 IEEE 语义（NaN != NaN，含 NaN 坐标的点经运算符判不等）——与 double / 元组 / 记录的
    // 运算符约定一致。与下方 Equals 的自反语义刻意分叉（BCL 惯例：double 自身即运算符 IEEE、Equals 自反）。
    public static bool operator ==(Point p1, Point p2)
    {
        return p1.X == p2.X && p1.Y == p2.Y;
    }

    public static bool operator !=(Point p1, Point p2)
    {
        return p1.X != p2.X || p1.Y != p2.Y;
    }

    public override readonly string ToString()
    {
        return string.Format("({0}, {1})", X, Y);
    }

    // 逐分量 double.Equals（NaN.Equals(NaN)==true）→ 自反：p.Equals(p) 恒真，含 NaN 坐标的点也能在
    // 集合 / 字典 / EqualityComparer<Point>.Default 里被找回。实现 IEquatable<Point> 免去泛型场景的装箱。
    public readonly bool Equals(Point other) => X.Equals(other.X) && Y.Equals(other.Y);

    public override readonly bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is Point other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 23) + X.GetHashCode();
            hash = (hash * 23) + Y.GetHashCode();
            return hash;
        }
    }
}
