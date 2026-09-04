using System;
using TuneLab.Data;

namespace TuneLab.Commands;

// 入口注入的一切「环境」。handler 只从这里取，不碰静态单例、不碰 Dispatcher——这是同一条命令能同时
// 服务侧栏 agent、CLI（attach / headless）、MCP 的前提。
//
// 成员按需增长（每一步只加当步真正要用的，见 docs/command-surface.md §5）：
//  · 搬 edit 命令时加 Authorization（按入口注入的授权策略）；
//  · 搬脚本类命令时加 EditorState（当前 part / 量化 / 选区）。
internal sealed class CommandContext
{
    // 当前工程。可为 null（宿主还没开工程 / headless 尚未载入）——需要工程的命令自己检查并如实报错，
    // 好过让命令从工具面消失：后者让调用方连"为什么没有"都问不到。
    public IProject? Project { get; init; }

    // 界面语言（本地化标签、给用户看的措辞按它取）。实时读取而非快照——用户随时可改。
    public Func<string?> Language { get; init; } = () => null;

    // 旁路模型（可选）：`extension list` 补能力位摘要时要发一次一次性请求。**只有内置 agent 入口有**，
    // CLI / MCP / headless 一律 null——用到它的命令按 §5.3 降级（缓存能用就用、用不上如实标注），
    // 不是消失。
    public ISideModelAccess? SideModel { get; init; }

    // 把活儿送上宿主主线程的调度器（数据层改动、引擎/字体枚举都要求在那儿跑）。入口注入：
    // 有界面的进程给 UiThreadDispatcher，headless 给泵驱动的等价物。null = 就地执行（无 UI 的测试进程）。
    public IMainThreadDispatcher? MainThread { get; init; }
}
