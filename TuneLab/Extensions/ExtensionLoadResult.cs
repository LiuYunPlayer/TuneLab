using System.Collections.Generic;

namespace TuneLab.Extensions;

internal enum ExtensionGeneration
{
    V1,      // 含 id 的新版插件，走 manifest 先导 + per-folder ALC 加载
    Legacy,  // 无 id（老 schema 或无 manifest.json），走兼容层 / 盲扫 fallback
}

internal enum ExtensionLoadStatus
{
    Loaded,          // 全部生效
    PartiallyLoaded, // 部分生效（个别 extension 平台不匹配 / effect 暂不支持 / 程序集失败 / 被用户禁用）
    Skipped,         // 整体跳过（平台不匹配 / sdk-version 不兼容 / Legacy 无 compat 且无匹配）
    Failed,          // 解析或加载异常
    Disabled,        // 用户关掉了整个包（或包内条目全被关掉）：不是故障，本次运行不加载它，见 ExtensionActivation
}

// 单个条目的加载结局。包级 Status 是全包的汇总，回答不了"这个包里到底哪一个能力没起来"——
// 而那正是排障（与启停开关）要落到的粒度：用户关的是某个能力，agent 也得说清哪个能力当前不可用。
internal enum ExtensionEntryStatus
{
    Registered,   // 已注册（资源类 = 已登记目录）
    Disabled,     // 用户禁用（整包被禁或本条目被禁），未尝试加载
    Skipped,      // 平台不匹配 / 声明了代码但宿主不认这个 type
    Failed,       // 程序集缺失 / SDK ABI 不兼容 / 入口类未命中 / 实例化抛异常
}

// manifest 声明的单个条目（= 一个能力位；资源类不占能力位、Identity 为空）在展示与 agent 侧所需的信息。
// 【声明即入列】：不论该条目最终是否注册成功（平台不匹配 / 程序集缺失 / 入口类未命中），都如实反映
// 「这个包里有什么」——加载成败由 ExtensionLoadResult 的 Status/Error 汇总承载，两者各司其职。
// Legacy 包无 manifest 条目（能力靠盲扫发现），故其 Entries 恒空。
internal sealed record ExtensionEntryInfo(
    string Kind,                // 类别 = manifest 声明的 type 原样：format / voice / instrument / effect，或资源类的自定 type
    // 该条目占的能力位身份：engine 类恰好一个（engine id）；format 是它认的全部后缀（一个格式可有多个别名，
    // 各自独立路由）；资源类为空。多身份共用本条目的显示名与说明。
    IReadOnlyList<string> Identities,
    string DisplayName,         // 本地化显示名（作者未写则回退身份 id）
    string? IntroductionPath)   // introduction 解析后的绝对路径；未声明或文件不存在为 null
{
    // 本次运行该条目的结局。声明期先入列（值为 Registered），加载循环走到哪一支就地改写——
    // 故只读消费方（UI/agent）拿到的恒是终值。
    public ExtensionEntryStatus Status { get; set; } = ExtensionEntryStatus.Registered;
    // 非 Registered 时的一句话原因（与包级 Error 里对应那一段同文）；Disabled 无需原因故为 null。
    public string? Error { get; set; }
}

// 结构化加载结果——sidebar 直接消费（取代字符串猜测），亦供诊断。
// 物理键是 DirectoryPath（与安装/卸载一致）；Id 是 V1 逻辑标识（Legacy 为 null）。
internal sealed class ExtensionLoadResult
{
    public required string DirectoryPath { get; init; }
    public required string Name { get; init; }
    public string? Id { get; init; }
    public string Version { get; init; } = "1.0.0";
    public string Author { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? IconPath { get; init; }   // 解析后的绝对路径（包目录 + manifest 的 icon），文件不存在则为 null
    public ExtensionGeneration Generation { get; init; }
    public ExtensionLoadStatus Status { get; set; }
    public List<string> Types { get; } = [];   // 声明/发现的类别：format / voice / effect / 资源类
    public List<ExtensionEntryInfo> Entries { get; } = [];   // 逐条目信息（V1 按 manifest 声明序；Legacy 恒空）
    public string? Error { get; set; }
}
