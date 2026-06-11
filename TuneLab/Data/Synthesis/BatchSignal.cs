using System;

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

    public void Begin()
    {
        if (mDepth++ == 0)
            BatchBegin?.Invoke();
    }

    public void End()
    {
        if (--mDepth == 0)
            BatchEnd?.Invoke();
    }

    int mDepth = 0;
}
