using System;

namespace TuneLab.Commands;

// 【有界面的宿主进程】里的编辑器状态实现：把"界面此刻怎么样"的几处读法收成一个对象。
// 每次现调委托，故用户按了播放 / 换了笔 / 点进另一个面之后，下一条命令看到的就是新值。
// headless 不用它（那里没有编辑器，整个 EditorStatus 为 null）。
//
// 取委托而不是直接读宿主静态单例（AudioEngine 等）是命令面的规矩：命令只从 ctx 取环境，这样同一条命令
// 才能同时服务侧栏 agent、命令桥与无头进程（见 CommandContext）。
internal sealed class EditorStatusAccess(
    Func<bool> isPlaying,
    Func<double> playheadTime,
    Func<double?> playheadTick,
    Func<double> endTime,
    Func<string> currentToolActionId,
    Func<bool> isParameterPanelVisible,
    Func<string?> focusedSurface) : IEditorStatusAccess
{
    public bool IsPlaying => isPlaying();
    public double PlayheadTime => playheadTime();
    public double? PlayheadTick => playheadTick();
    public double EndTime => endTime();
    public string CurrentToolActionId => currentToolActionId();
    public bool IsParameterPanelVisible => isParameterPanelVisible();
    public string? FocusedSurface => focusedSurface();
}
