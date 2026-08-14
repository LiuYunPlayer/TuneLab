using System;
using System.IO;
using System.Runtime.CompilerServices;
using TuneLab.Foundation;

namespace TuneLab.Data.Synthesis;

// 批量变更括号的宿主侧信号源：嵌套计数，最外层进/出各触发一次。
// 挂在 part 上（生命周期随 part），合成会话的 context 订阅它转发给插件——
// 括号不是宿主缓冲，是让插件延迟昂贵状态修正（如重分片）的作用域信号。
// 宿主在批量编辑入口（含 undo/redo 重放）成对调用 Begin/End。
internal sealed class BatchSignal
{
    public event Action? BatchBegin;
    public event Action? BatchEnd;
    public bool IsBatching => mDepth > 0;

    // —— 诊断面（只读，供调度器解释「有待办却没派活」）——
    // 括号是跨鼠标手势的（Down 开、Up 关），漏配平就会让这个 part 永久停摆而不抛任何异常，
    // 故记下最外层是谁开的、开了多久：一旦出现长时间未关，日志能直接点名调用点。
    public int Depth => mDepth;
    public string? OpenedBy => mOpenedBy;
    public double OpenSeconds => mDepth > 0 ? (Environment.TickCount64 - mOpenedAt) / 1000.0 : 0;

    public void Begin([CallerFilePath] string? file = null, [CallerLineNumber] int line = 0, [CallerMemberName] string? member = null)
    {
        if (mDepth++ == 0)
        {
            mOpenedBy = $"{Path.GetFileName(file)}:{line} {member}";
            mOpenedAt = Environment.TickCount64;
            BatchBegin?.Invoke();
        }
    }

    public void End()
    {
        if (--mDepth == 0)
        {
            mOpenedBy = null;
            BatchEnd?.Invoke();
        }
        else if (mDepth < 0)
        {
            // 配平多了一次：本身即缺陷（后续 Begin/End 全部错位，BatchBegin/BatchEnd 都不再触发，
            // 曲线变更的缓冲与冲刷随之失灵）。计数钳回 0，让状态可自愈，但如实留痕。
            Log.Warning($"Unbalanced batch end (depth {mDepth}); clamped to 0. Last opened by {mOpenedBy ?? "(unknown)"}.");
            mDepth = 0;
        }
    }

    long mOpenedAt;
    string? mOpenedBy;
    int mDepth = 0;
}
