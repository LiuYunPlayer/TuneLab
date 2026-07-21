using System;
using System.Globalization;

namespace TuneLab.SDK;

// 数字呈现：值 ↔ 显示文本的双向封装（两个方向不分家）。不限滑条——任何呈现 number 的控件都可复用。
public interface INumberFormat
{
    string Format(double value);
    double? Parse(string text);   // null = 解析失败（如带单位却无对应解析）
}

// INumberFormat 的语法糖工厂。Custom 是逃生口；Decimals 仅为便利。
public static class NumberFormat
{
    public static INumberFormat Decimals(int digits) => new DecimalsFormat(digits);
    public static INumberFormat Custom(Func<double, string> format, Func<string, double?> parse)
        => new CustomFormat(format, parse);

    sealed class DecimalsFormat(int digits) : INumberFormat
    {
        // 恒用 InvariantCulture（小数点恒为句点）：值本质是数据（double，持久层按句点存），显示 / 解析须确定性、
        // 可往返；用 CurrentCulture 会随机器区域出逗号小数点，且解析时把句点当千位分隔误读（如逗号区 "3.14"→314）。
        // 需本地化显示的走 Custom 逃生口自造。要本地化数字请勿复用本便利实现。
        readonly string mFormat = "F" + Math.Max(0, digits);
        public string Format(double value) => value.ToString(mFormat, CultureInfo.InvariantCulture);
        public double? Parse(string text)
            => double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    sealed class CustomFormat(Func<double, string> format, Func<string, double?> parse) : INumberFormat
    {
        public string Format(double value) => format(value);
        public double? Parse(string text) => parse(text);
    }
}
