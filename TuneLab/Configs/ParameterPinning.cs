using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Configs;

// 参数面板钉选条目的种类：决定值挂在哪层数据（note.Properties / phoneme.Properties）与钉选键的 token。
// 只描述"钉的是什么"、不预设呈现/操作形式（当前各种类恰好都物化成 lane，见 TuneLab.Data.LaneEntry；
// 未来的新种类可以是别的操作形式）——追加枚举值 + 键 token 即可，互不歧义。
internal enum ParameterPinKind
{
    NoteProperty,
    PhonemeProperty,
}

// 参数面板钉选存储 + 轨色分配：只管"用户往参数面板钉了什么、什么颜色"（呈现快照/值形态判别在 TuneLab.Data.LaneEntry）。
// 【为何是用户偏好而非插件声明】"哪个属性值得占参数面板"是工作流偏好、不是插件语义：SDK 声明面零变动，
//   所有现有 voice/instrument 插件即刻可用。
// 【存法】不是用户可调项，不进 Settings（Settings 仅承用户可调项）；也非窗口布局，不进 EditorState——
//   与 RecentSoundSourceManager 同模式：自带独立 JSON 持久化（Configs/ParameterPins.json），改后即时存盘。
//   键 "kind:声源Type:laneKind:属性id"，【键存在即钉选、值即轨色】——一条存储承载两件事。
internal static class ParameterPinning
{
    public static void Init(string path)
    {
        Dictionary<string, string>? lanes = null;
        if (File.Exists(path))
        {
            try
            {
                lanes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.OpenRead(path));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to deserialize pinned lanes: " + ex);
            }
        }
        mLanes = lanes ?? new();
        mPath = path;
    }

    static void Save()
    {
        try
        {
            var folder = Path.GetDirectoryName(mPath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllText(mPath, JsonSerializer.Serialize(mLanes, JsonSerializerOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save pinned lanes: " + ex);
        }
    }

    public static bool IsPinned(ISoundSource source, ParameterPinKind kind, string id)
        => mLanes.ContainsKey(LaneKey(source, kind, id));

    // 该声源该种类下全部钉选（id → 轨色）。lane 的呈现序不存这里——由声明面（属性 config）的声明序决定，本存储只管成员与颜色。
    public static Dictionary<string, string> GetPinned(ISoundSource source, ParameterPinKind kind)
    {
        var prefix = LaneKey(source, kind, string.Empty);
        var result = new Dictionary<string, string>();
        foreach (var kvp in mLanes)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                result[kvp.Key.Substring(prefix.Length)] = kvp.Value;
        }
        return result;
    }

    // 钉选：分配轨色（避开 occupiedColors——该声源参数面板已占用的 automation 轨色 + 各种类既有 lane 色）并即时落盘。
    public static void Pin(ISoundSource source, ParameterPinKind kind, string id, IEnumerable<string> occupiedColors)
    {
        var occupied = new List<string>(occupiedColors);
        foreach (ParameterPinKind existing in Enum.GetValues<ParameterPinKind>())
        {
            foreach (var color in GetPinned(source, existing).Values)
                occupied.Add(color);
        }
        mLanes[LaneKey(source, kind, id)] = PickColor(occupied);
        Save();
    }

    public static void Unpin(ISoundSource source, ParameterPinKind kind, string id)
    {
        if (mLanes.Remove(LaneKey(source, kind, id)))
            Save();
    }

    // 键跟声源身份走（kind + Type + laneKind + 属性 id）、不跟工程走：用户偏好跨工程生效；换引擎后 id 对不上则自然不显示，无需清理。
    static string LaneKey(ISoundSource source, ParameterPinKind kind, string id)
        => (source.Kind == SourceKind.Voice ? "voice:" : "instrument:") + source.Type
            + (kind == ParameterPinKind.NoteProperty ? ":note-property:" : ":phoneme-property:") + id;

    // 从固定调色板挑与已占用色相距离最远的色（灰/近灰的占用色无稳定色相，忽略）。全占满后仍取距离最大者（可重复）。
    static string PickColor(IReadOnlyList<string> occupiedColors)
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

    // "#RRGGBB" → HSV 色相（度）。解析失败或饱和度过低（近灰、色相不稳定）返回 false。
    static bool TryGetHue(string hex, out double hue)
    {
        hue = 0;
        if (string.IsNullOrEmpty(hex))
            return false;

        var span = hex.AsSpan(hex[0] == '#' ? 1 : 0);
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

    static Dictionary<string, string> mLanes = new();
    static string mPath = string.Empty;
    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}
