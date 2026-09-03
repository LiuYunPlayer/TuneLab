using System;
using TuneLab.Data;

namespace TuneLab.Commands;

// 入口注入的一切「环境」。handler 只从这里取，不碰静态单例、不碰 Dispatcher——这是同一条命令能同时
// 服务侧栏 agent、CLI（attach / headless）、MCP 的前提。
//
// 成员按需增长（每一步只加当步真正要用的，见 docs/command-surface.md §5）：
//  · 搬 edit 命令时加 Authorization（按入口注入的授权策略）；
//  · 搬脚本类命令时加 EditorState（当前 part / 量化 / 选区）；
//  · 搬 extension list 时加 SideModel（补能力位摘要要发旁路模型请求，外部入口没有 → 降级）。
internal sealed class CommandContext
{
    // 当前工程。可为 null（宿主还没开工程 / headless 尚未载入）——需要工程的命令自己检查并如实报错，
    // 好过让命令从工具面消失：后者让调用方连"为什么没有"都问不到。
    public IProject? Project { get; init; }

    // 界面语言（本地化标签、给用户看的措辞按它取）。实时读取而非快照——用户随时可改。
    public Func<string?> Language { get; init; } = () => null;
}
