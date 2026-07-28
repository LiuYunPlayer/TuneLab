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
    PartiallyLoaded, // 部分生效（个别 extension 平台不匹配 / effect 暂不支持 / 程序集失败）
    Skipped,         // 整体跳过（平台不匹配 / sdk-version 不兼容 / Legacy 无 compat 且无匹配）
    Failed,          // 解析或加载异常
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
    string? IntroductionPath);  // introduction 解析后的绝对路径；未声明或文件不存在为 null

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
