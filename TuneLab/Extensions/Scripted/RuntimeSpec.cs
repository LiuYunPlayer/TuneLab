using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TuneLab.Extensions.Scripted;

// 脚本插件已发现的运行时信息（探测结果；设计文档 §8.2）。
// Arch 仅作呈现/诊断记录（跨进程子进程可借模拟层跑异架构，如 win-arm64 跑 x64 python），
// 门禁看 flavor + 版本区间与 manifest `platforms`，不按 arch 拒载。
internal readonly record struct RuntimeInfo(string Exe, string Flavor, string Version, string Arch);

// manifest `runtime` 字段的结构：flavor（python|node|lua|luajit）+ 逗号分隔的版本约束
// （">=3.10,<4"；操作符 >= > <= < == =，版本 1–3 段，缺段补 0）。解析失败回错误串，供 Failed 文案。
internal readonly record struct RuntimeSpec(string Flavor, Version Min, bool MinInclusive, Version Max, bool MaxInclusive)
{
    // 裸 flavor（"python"，无约束）合法；约束段整体可空。交替序讲究：luajit 必须排在 lua 之前，
    // 否则正则前缀会吞成 "lua" + 残留 "jit>=…"（交错缺陷，测试 NormalizesDeclaration 钉住）。
    static readonly Regex sFlavorPrefix = new(@"^(python|node|luajit|lua)\s*(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex sConstraint = new(@"^(>=|<=|==|>|<|=)\s*(\d+(?:\.\d+){0,2})$", RegexOptions.Compiled);

    public bool HasMin { get; init; }
    public bool HasMax { get; init; }

    public bool SatisfiedBy(string versionString)
    {
        if (!TryParseVersion(versionString, out var v))
            return false;
        if (HasMin && (v < Min || (!MinInclusive && v == Min)))
            return false;
        if (HasMax && (v > Max || (!MaxInclusive && v == Max)))
            return false;
        return true;
    }

    // 人话描述（"python>=3.10,<4"），供 Skipped/Failed 文案回显声明。
    public string Describe()
    {
        var s = Flavor.ToLowerInvariant();
        if (HasMin)
            s += (MinInclusive ? ">=" : ">") + Min.ToString(3);
        if (HasMax)
            s += (HasMin ? "," : "") + (MaxInclusive ? "<=" : "<") + Max.ToString(3);
        return s;
    }

    // 解析 "python>=3.10,<4" 之类。失败：ok=false + error（作者可见的人话）。
    public static bool TryParse(string? text, out RuntimeSpec spec, out string? error)
    {
        spec = default;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "'runtime' must not be empty (expected e.g. \"python>=3.10\")";
            return false;
        }
        var m = sFlavorPrefix.Match(text.Trim());
        if (!m.Success)
        {
            error = string.Format(CultureInfo.InvariantCulture,
                "malformed 'runtime' '{0}': expected <flavor> with optional comma-separated constraints; " +
                "flavors = python | node | lua | luajit, operators = >= > <= < == = (e.g. \"python>=3.10,<4\")", text);
            return false;
        }
        var flavor = m.Groups[1].Value.ToLowerInvariant();
        var min = new Version(0, 0); var minInc = true; var hasMin = false;
        var max = new Version(int.MaxValue, 0); var maxInc = true; var hasMax = false;
        var rest = m.Groups[2].Value.Trim();
        if (rest.Length > 0)
        {
            foreach (var token in rest.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var c = sConstraint.Match(token);
                if (!c.Success || !TryParseVersion(c.Groups[2].Value, out var v))
                {
                    error = string.Format(CultureInfo.InvariantCulture, "bad constraint '{0}' in 'runtime' (expected <op><version>, op one of >= > <= < == =)", token);
                    return false;
                }
                switch (c.Groups[1].Value)
                {
                    case ">=": if (!hasMin || v > min) { min = v; minInc = true; } hasMin = true; break;
                    case ">": if (!hasMin || v > min) { min = v; minInc = false; } hasMin = true; break;
                    case "<=": if (!hasMax || v < max) { max = v; maxInc = true; } hasMax = true; break;
                    case "<": if (!hasMax || v < max) { max = v; maxInc = false; } hasMax = true; break;
                    case "=":
                    case "==":
                        min = v; minInc = true; hasMin = true;
                        max = v; maxInc = true; hasMax = true;
                        break;
                }
            }
        }
        spec = new RuntimeSpec(flavor, min, minInc, max, maxInc) { HasMin = hasMin, HasMax = hasMax };
        return true;
    }

    static bool TryParseVersion(string s, out Version v)
    {
        var parts = s.Trim().Split('.');
        var nums = new int[3];
        for (int i = 0; i < parts.Length && i < 3; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out nums[i]))
            {
                v = new Version(0, 0);
                return false;
            }
        }
        v = new Version(nums[0], nums[1], nums[2]);
        return true;
    }
}
