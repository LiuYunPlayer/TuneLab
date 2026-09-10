using System;
using TuneLab.Data;

namespace TuneLab.Commands;

// 入口注入的一切「环境」。handler 只从这里取，不碰静态单例、不碰 Dispatcher——这是同一条命令能同时
// 服务侧栏 agent、CLI（attach / headless）、MCP 的前提。
//
internal sealed record CommandContext
{
    // 当前工程。可为 null（宿主还没开工程 / headless 尚未载入）——需要工程的命令自己检查并如实报错，
    // 好过让命令从工具面消失：后者让调用方连"为什么没有"都问不到。
    public IProject? Project { get; init; }

    // 界面语言（本地化标签、给用户看的措辞按它取）。实时读取而非快照——用户随时可改。
    public Func<string?> Language { get; init; } = () => null;

    // 授权策略（可选）：Edit 命令落地前一律先问它。**null = 这个入口没配授权 → 一条 Edit 都不做**
    // （见 AuthorizationExtensions.Authorize）——headless/CI 必须显式给，静默放开是不能接受的默认值。
    public IAuthorizationPolicy? Authorization { get; init; }

    // 编辑器态（可选）：当前 part / 量化 / 选区。侧栏 agent 有；headless 没有（null），依赖它的命令
    // 按"用户什么也没选"处理或要求显式传参，不猜（见 IEditorStateAccess）。
    public IEditorStateAccess? EditorState { get; init; }

    // 编辑器此刻的界面状态（可选）：在播吗 / 播到哪 / 拿着哪支笔 / 面板开着没 / 焦点在哪个编辑面。
    // 与 EditorState 分立的理由见 IEditorStatusAccess。有编辑器的进程才有；null = 这里没有编辑器，
    // 动作面与状态读据此如实回答做不到（措辞见 EditorStatusText.NoEditor）。
    public IEditorStatusAccess? EditorStatus { get; init; }

    // 换工程文件的能力（`project open`）。只有有窗口的宿主给得出——见 IProjectFileAccess。
    public IProjectFileAccess? ProjectFile { get; init; }

    // 挪视野的能力（`editor reveal`）。只有有窗口的宿主给得出——见 IEditorViewAccess。
    public IEditorViewAccess? EditorView { get; init; }
    // 旁路模型（可选）：`extension list` 补能力位摘要时要发一次一次性请求。**只有内置 agent 入口有**，
    // CLI / MCP / headless 一律 null——用到它的命令按 §5.3 降级（缓存能用就用、用不上如实标注），
    // 不是消失。
    public ISideModelAccess? SideModel { get; init; }

    // 把活儿送上宿主主线程的调度器（数据层改动、引擎/字体枚举都要求在那儿跑）。入口注入：
    // 有界面的进程给 UiThreadDispatcher，headless 给泵驱动的等价物。null = 就地执行（无 UI 的测试进程）。
    public IMainThreadDispatcher? MainThread { get; init; }
}
