using System;
using System.Collections.Generic;
using System.Threading;

namespace TuneLab.Scripting;

// 探测沙箱的驱动线程用的【可泵 SynchronizationContext】。
//
// 合成管线是异步的：VoiceSynthesisPipeline 的 session 出方向事件（音素/参数/状态变化）与 Dispatch 的
// await 续体都经建管线时的 SynchronizationContext.Current marshal 回「数据线程」。编辑器里那条数据线程
// = Avalonia UI 线程（其 Dispatcher 自动泵）。无头沙箱没有窗口/Dispatcher，故自带这个上下文并【手动泵】
// ——DrainAll 排空当前待办、WaitForWork 阻塞等下一个。仿 Editor 的合成调度循环，但零 UI 依赖。
//
// 仅在沙箱那一条专用线程上安装与泵。Post 可被任意线程调用（插件续体所在线程）；泵只在沙箱线程调。
internal sealed class PumpableSynchronizationContext : SynchronizationContext
{
    readonly object mLock = new();
    readonly Queue<(SendOrPostCallback Callback, object? State)> mQueue = new();
    readonly AutoResetEvent mSignal = new(false);
    readonly int mThreadId = Environment.CurrentManagedThreadId;

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (mLock)
            mQueue.Enqueue((d, state));
        mSignal.Set();
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        // 同线程同步发送直接执行，避免"入队后等自己泵"的自死锁。
        if (Environment.CurrentManagedThreadId == mThreadId)
        {
            d(state);
            return;
        }

        // 跨线程同步发送：入队 + 等它被泵执行完（异常透传）。
        using var done = new ManualResetEventSlim(false);
        Exception? error = null;
        Post(_ =>
        {
            try { d(state); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        }, null);
        done.Wait();
        if (error != null)
            throw error;
    }

    // 执行当前队列里所有待办回调（非阻塞）。返回是否至少执行了一个——供驱动循环判断"是否有进展"。
    public bool DrainAll()
    {
        bool any = false;
        while (true)
        {
            (SendOrPostCallback Callback, object? State) item;
            lock (mLock)
            {
                if (mQueue.Count == 0)
                    break;
                item = mQueue.Dequeue();
            }
            item.Callback(item.State);
            any = true;
        }
        return any;
    }

    // 阻塞至多 timeout，等有新回调入队（或超时）；返回后调用方应再 DrainAll。
    // 用 AutoResetEvent：Post 的 Set 是粘性的（直到一次 WaitOne 消费），故 DrainAll 与 WaitForWork 之间
    // 到来的 Post 不会丢唤醒。调用方仍以小步长（如 50ms）轮询，兼容插件用 ConfigureAwait(false) 令续体
    // 不回到本上下文的情形（那时靠轮询重查 IsBusy）。
    public void WaitForWork(TimeSpan timeout) => mSignal.WaitOne(timeout);
}
