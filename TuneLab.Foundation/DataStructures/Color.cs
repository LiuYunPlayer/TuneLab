using System;
using System.Globalization;

namespace TuneLab.Foundation;

// 颜色：A、R、G、B 各一字节（sRGB，Alpha 非预乘）。SDK 里颜色的分量顺序一律 Alpha 在前——与工程文件、AutomationConfig.WithColor
// 等处的 8 位十六进制 #AARRGGBB 同一种语义。只走静态工厂，工厂名即参数顺序（FromRgb / FromArgb），不给按位置的构造函数，
// 免得调用处看不出 Alpha 在第几位。default(Color) 是全 0 = 完全透明的黑色。
// 只读值：相等即四个分量逐一相等（没有"颜色名"之类的附带状态）。不带具名颜色表——插件自己定义常量即可，冻结面越小越好。
public readonly record struct Color
{
    public byte A { get; }
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    Color(byte a, byte r, byte g, byte b)
    {
        A = a;
        R = r;
        G = g;
        B = b;
    }

    // 不透明色。
    public static Color FromRgb(byte r, byte g, byte b) => new(255, r, g, b);
    public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    // 十六进制："#RRGGBB"（不透明）或 "#AARRGGBB"，# 可省、大小写不限——与 SDK 别处的十六进制颜色同一种写法。
    // 格式不对抛 ArgumentException。
    public static Color FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var digits = hex.StartsWith('#') ? hex.AsSpan(1) : hex.AsSpan();
        if ((digits.Length == 6 || digits.Length == 8) && uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            if (digits.Length == 6)
                value |= 0xFF000000;
            return new((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
        throw new ArgumentException($"\"{hex}\" is not a hex color: expected #RRGGBB or #AARRGGBB.", nameof(hex));
    }

    public Color WithAlpha(byte alpha) => new(alpha, R, G, B);
}
