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
    public string? Error { get; set; }
}
