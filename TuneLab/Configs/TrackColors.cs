using System;
using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Configs;

// 参数面板轨色的宿主分配：插件没指定颜色的轨（AutomationConfig.Color 默认全透明 = 不指定）、用户钉进参数面板的 lane，
// 都从同一块调色板里挑与已占用色相距离最远的色。分配是确定的：同样的声明、同样的顺序，每次得到同样的颜色。
//
// 分配在宿主物化声明的入口统一做（声源的轨集合、效果器的轨集合），下游直接读 config.Color 的十几处消费点
// 永远看不到透明色——漏判一处就是一条画不出来的曲线，所以不能交给各消费点自己判断。
internal static class TrackColors
{
    // 插件没指定颜色：alpha 为 0（全透明的轨色画不出来，没有别的意义）。解析不了的颜色串不算——它照旧走显示时的兜底色。
    public static bool IsUnassigned(string? color)
    {
        if (color == null || color.Length != 9 || color[0] != '#')
            return false;

        return int.TryParse(color.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out int alpha) && alpha == 0;
    }

    // 给集合里没指定颜色的轨按声明序分配颜色；已占用 = occupied + 本集合里指定了的颜色 + 先分到的颜色。
    // 都指定了就原样返回同一个集合。
    public static IReadOnlyOrderedMap<PropertyKey, AutomationConfig> Assign(IReadOnlyOrderedMap<PropertyKey, AutomationConfig> configs, IEnumerable<string> occupied)
    {
        bool any = false;
        var used = new List<string>(occupied);
        foreach (var kvp in configs)
        {
            if (IsUnassigned(kvp.Value.Color))
                any = true;
            else
                used.Add(kvp.Value.Color);
        }
        if (!any)
            return configs;

        var result = new OrderedMap<PropertyKey, AutomationConfig>();
        foreach (var kvp in configs)
        {
            var config = kvp.Value;
            if (IsUnassigned(config.Color))
            {
                var color = Pick(used);
                used.Add(color);
                config = config.WithColor(color);
            }
            result.Add(kvp.Key, config);
        }
        return result;
    }

    // 回显轨（合成参数）：与同 id 的可编辑轨同色（同一个参数的两种呈现），没有同 id 的再从调色板分。
    public static IReadOnlyOrderedMap<PropertyKey, AutomationConfig> AssignSynthesized(IReadOnlyOrderedMap<PropertyKey, AutomationConfig> configs, IReadOnlyOrderedMap<PropertyKey, AutomationConfig> editable)
    {
        bool any = false;
        foreach (var kvp in configs)
        {
            if (IsUnassigned(kvp.Value.Color))
                any = true;
        }
        if (!any)
            return configs;

        var paired = new OrderedMap<PropertyKey, AutomationConfig>();
        foreach (var kvp in configs)
        {
            var config = kvp.Value;
            if (IsUnassigned(config.Color) && editable.TryGetValue(kvp.Key, out var twin))
                config = config.WithColor(twin.Color);
            paired.Add(kvp.Key, config);
        }

        var occupied = new List<string>();
        foreach (var kvp in editable)
            occupied.Add(kvp.Value.Color);
        return Assign(paired, occupied);
    }

    // 从固定调色板挑与已占用色相距离最远的色（灰/近灰的占用色无稳定色相，忽略）。全占满后仍取距离最大者（可重复）。
    public static string Pick(IReadOnlyList<string> occupiedColors)
    {
        var occupiedHues = new List<double>();
        foreach (var color in occupiedColors)
        {
            if (TryGetHue(color, out var hue))
                occupiedHues.Add(hue);
        }

        string best = Palette[0];
        double bestDistance = -1;
        foreach (var candidate in Palette)
        {
            TryGetHue(candidate, out var hue);   // 调色板恒高饱和，必有色相
            double distance = double.MaxValue;
            foreach (var occupied in occupiedHues)
            {
                double d = Math.Abs(hue - occupied);
                distance = Math.Min(distance, Math.Min(d, 360 - d));
            }
            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    // "#RRGGBB" / "#AARRGGBB" → HSV 色相（度）。解析失败、全透明或饱和度过低（近灰、色相不稳定）返回 false。
    static bool TryGetHue(string hex, out double hue)
    {
        hue = 0;
        if (string.IsNullOrEmpty(hex) || IsUnassigned(hex))
            return false;

        var span = hex.AsSpan(hex[0] == '#' ? 1 : 0);
        if (span.Length == 8)
            span = span[2..];   // 跳过 alpha
        if (span.Length < 6
            || !int.TryParse(span[..2], System.Globalization.NumberStyles.HexNumber, null, out int r)
            || !int.TryParse(span[2..4], System.Globalization.NumberStyles.HexNumber, null, out int g)
            || !int.TryParse(span[4..6], System.Globalization.NumberStyles.HexNumber, null, out int b))
            return false;

        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        int delta = max - min;
        if (max == 0 || delta * 255 < max * 30)   // 饱和度 < ~0.12：近灰
            return false;

        double h;
        if (max == r)
            h = 60.0 * (g - b) / delta;
        else if (max == g)
            h = 60.0 * (b - r) / delta + 120;
        else
            h = 60.0 * (r - g) / delta + 240;
        hue = (h + 360) % 360;
        return true;
    }

    // 深底友好的高饱和候选色（色相尽量铺开）。
    static readonly string[] Palette =
    [
        "#F1595B",   // 红
        "#F5A623",   // 橙
        "#F7DC5C",   // 黄
        "#8AC926",   // 绿
        "#2EC4B6",   // 青
        "#4CC9F0",   // 天蓝
        "#3A86FF",   // 蓝
        "#9B5DE5",   // 紫
        "#F15BB5",   // 品红
    ];
}
