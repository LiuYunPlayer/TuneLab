using System;

namespace TuneLab.SDK;

// 归一化标度：归一化位置 [0,1] 与实际值的双向映射。唯一约束就是这条归一化轴——故 slider 拖柄定位、
// automation 值轴等凡"有界值轴"皆共用。线性/整数是内置便利实现；要对数轴、2^n 等任意映射，
// 插件自行实现本接口即可，功能不受 SDK 是否提供工厂影响。
public interface INormalizedScale
{
    // 0..1 → 值。可离散化（如整数标度四舍五入），故未必是连续双射。
    double ToValue(double normalized);
    // 值 → 0..1，用于在轴上定位。离散化不可逆，逆其底层连续映射即可（定位足够）。
    double ToNormalized(double value);
}

// INormalizedScale 的语法糖工厂。约定底层标度单调递增。Custom 是逃生口；Linear/Integer 仅为便利。
public static class NormalizedScale
{
    public static INormalizedScale Linear(double min, double max) => new LinearScale(min, max);

    // 整数：= Rounded(Linear)，最常见组合的捷径，取代原 SliderConfig.IsInteger（默认取最近整数）。
    public static INormalizedScale Integer(double min, double max) => Rounded(Linear(min, max));

    // 整数吸附装饰器（三种舍入向）。与轴形正交——可套在 Linear / Custom / 用户自实现的标度上，
    // 故插件自带对数轴等也能直接吸附取整，不必各自重写吸附逻辑。
    public static INormalizedScale Rounded(INormalizedScale scale) => new SnappedScale(scale, Math.Round);  // 最近
    public static INormalizedScale Floor(INormalizedScale scale) => new SnappedScale(scale, Math.Floor);    // 向下
    public static INormalizedScale Ceil(INormalizedScale scale) => new SnappedScale(scale, Math.Ceiling);   // 向上

    public static INormalizedScale Custom(Func<double, double> toValue, Func<double, double> toNormalized)
        => new CustomScale(toValue, toNormalized);

    sealed class LinearScale(double min, double max) : INormalizedScale
    {
        public double ToValue(double normalized) => min + (max - min) * normalized;
        public double ToNormalized(double value) => max == min ? 0 : (value - min) / (max - min);
    }

    sealed class SnappedScale(INormalizedScale inner, Func<double, double> snap) : INormalizedScale
    {
        // 仅吸附输出值；定位仍用底层连续逆（吸附不可逆，逆其底层映射即可）。
        public double ToValue(double normalized) => snap(inner.ToValue(normalized));
        public double ToNormalized(double value) => inner.ToNormalized(value);
    }

    sealed class CustomScale(Func<double, double> toValue, Func<double, double> toNormalized) : INormalizedScale
    {
        public double ToValue(double normalized) => toValue(normalized);
        public double ToNormalized(double value) => toNormalized(value);
    }
}
