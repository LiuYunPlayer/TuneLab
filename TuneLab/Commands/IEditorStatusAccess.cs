using System.Collections.Generic;

namespace TuneLab.Commands;

// 「编辑器此刻的界面状态」——在播吗、播到哪、手里拿着哪支笔、参数面板开着没、键盘焦点在哪个编辑面。
//
// 与 IEditorStateAccess 的分工（两者刻意分立，别往对方身上加字段）：
//  · EditorState  = **脚本求值要的隐式上下文**（当前 part / 量化 / 两个选区），本就是喂给 tl.* 的那几样；
//  · EditorStatus = **报给外部的界面状态**，`editor status` 与 `action run` 的回报读它。
//
// 【动作面为什么非要它】动作里有一半是即发即忘的（transport.play 是切换、tool.* 是选择），调用方触发完
// 必须能问"现在到底是什么状态"，否则这个动作面是瞎的（docs/command-surface.md §5.5）。
//
// 取属性而非快照：实现方每次现读，故命令拿到的恒是当刻值（与 ctx 里其余访问器同理）。
// 没有编辑器的进程（headless / 桥未连）里整个为 null——同 EditorState，命令据此如实回答"这里做不到"。
internal interface IEditorStatusAccess
{
    bool IsPlaying { get; }

    // 播放头位置：秒，以及同一位置的 tick。tick 要靠工程的曲速表换算，故没有工程时为 null。
    double PlayheadTime { get; }
    double? PlayheadTick { get; }

    // 工程音频的末端（秒）：transport.gotoEnd 去的就是这里。
    double EndTime { get; }

    // 钢琴窗当前工具，用**动作 id** 表示（tool.note / tool.pitch …）——调用方因此能把它与 `action list`
    // 里那几条切工具的动作直接对上，不必猜工具名怎么拼。
    string CurrentToolActionId { get; }

    bool IsParameterPanelVisible { get; }

    // 波形带显隐（参数区标题栏那个开关）。
    bool IsWaveformVisible { get; }

    // 开着的侧栏面，用【动作 id】表示（sidebar.showAgent …）；null = 侧栏没开。
    // 同 CurrentToolActionId：报 id 而不是页签名，调用方因此能把它与 `action list` 里的 show 动作对上。
    string? SidebarPanelActionId { get; }

    // 此刻挡在界面前面、**非人不能答**的东西（模态框 / 系统文件选择器），各一句给人看的说明；空 = 没有。
    //
    // 【为什么这一条非有不可】模态框期间 Avalonia 跑的是嵌套消息循环，Dispatcher 照常泵，于是命令桥
    // 照常应答——外部因此可以在用户屏幕被一个框锁着的时候若无其事地跑一串命令、条条回报成功，而用户
    // 一个字都看不见、也点不了。那是最难被发现的一种假装成功（每一条单独看都没错）。它同时解释了
    // 另外两件事：`action run` 的 allowPrompt 为什么存在，以及此刻按下去的动作为什么可能没有反应。
    IReadOnlyList<string> BlockingDialogs { get; }

    // 键盘焦点落在哪个编辑面："arrangement" / "pianoRoll" / null（都不在）。剪贴板类动作作用的就是它，
    // 故这一条同时解释了那批动作此刻为什么可用或不可用。
    string? FocusedSurface { get; }
}
