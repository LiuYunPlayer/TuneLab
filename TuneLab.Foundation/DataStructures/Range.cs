using System;
using System.Diagnostics.CodeAnalysis;

namespace TuneLab.Foundation;

// 一段数值区间：只有起点与终点，不承载别的含义；单位写在用它的地方（参数名 / 注释），如时间轴 lane 画布提交时报的改动范围（局部 tick）。
// 可能不止起止的概念给自己的类型（如 SynthesisRange），别拿它替。与 System.Range 撞名：同时用到 System 时写别名
// `using Range = TuneLab.Foundation.Range;`。
public readonly struct Range(double start, double end) : IEquatable<Range>
{
    public double Start { get; } = start;
    public double End { get; } = end;

    public override string ToString() => string.Format("[{0}, {1})", Start, End);

    // 逐分量 double.Equals（自反，同 Point）。
    public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Range other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Start, End);
}
