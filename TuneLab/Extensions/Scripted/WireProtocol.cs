namespace TuneLab.Extensions.Scripted;

// 脚本 sidecar 的 wire 协议版本轴（设计文档 docs/scripted-plugin-sidecar-design.md §1.2）。
// 三条版本轴各管各的：AssemblyVersion（.NET 绑定，钉死 2.0.0.0）、SdkVersion（.NET 插件编译面）、
// ProtocolVersion（本轴：manifest `protocol-version` × 宿主 + sidecar 握手）。互不对齐、互不换算。
//
// 提档纪律（仿 sdk-api-evolution §0 的"一个动作、两处台账"）：帧/方法表有不兼容改动时，此常量
// 与 scripting/wire/v1.json（发布 M1 起）同批提档；加性新方法不提档。
internal static class WireProtocol
{
    public const int Version = 1;

    // wire JSON 规范里 Multiple（三态哨兵）不占形态——PropertyType 注释原文"瞬态、永不序列化"，
    // 它是 UI 多选合并层的概念，跨边界值恒为定值或 Null。
    public const string PropertyTags = "b n s o a";
}
