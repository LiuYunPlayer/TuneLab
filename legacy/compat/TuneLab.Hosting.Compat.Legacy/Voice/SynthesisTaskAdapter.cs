using System;
using LVoice = TuneLab.Extensions.Voices;
using VVoice = TuneLab.SDK.Voice;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

// 把老 ISynthesisTask 适配成 V1 ISynthesisTask。
// 事件桥接适配器 IDisposable、dispose 退订（§三.15 升级不变量——effect 分支漏此点致泄漏，
//   且正是 collectible 卸载的钉子）。宿主在 SynthesisPiece.Dispose 经 (task as IDisposable)?.Dispose() 触发退订。
//   任务可复用（SetDirty→Stop→重 Start，Complete 多次触发），故仅在 Dispose 退订，不在 Complete/Stop 退订。
internal sealed class SynthesisTaskAdapter : VVoice.ISynthesisTask, IDisposable
{
    public event Action<VVoice.SynthesisResult>? Complete;
    public event Action<double>? Progress;
    public event Action<string>? Error;

    public SynthesisTaskAdapter(LVoice.ISynthesisTask task)
    {
        mTask = task;
        mOnComplete = result => Complete?.Invoke(result.ToV1());
        mOnProgress = progress => Progress?.Invoke(progress);
        mOnError = error => Error?.Invoke(error);
        mTask.Complete += mOnComplete;
        mTask.Progress += mOnProgress;
        mTask.Error += mOnError;
    }

    public void Start() => mTask.Start();
    public void Suspend() => mTask.Suspend();
    public void Resume() => mTask.Resume();
    public void Stop() => mTask.Stop();
    public void SetDirty(string dirtyType) => mTask.SetDirty(dirtyType);

    public void Dispose()
    {
        if (mDisposed)
            return;
        mDisposed = true;
        mTask.Complete -= mOnComplete;
        mTask.Progress -= mOnProgress;
        mTask.Error -= mOnError;
    }

    readonly LVoice.ISynthesisTask mTask;
    readonly Action<LVoice.SynthesisResult> mOnComplete;
    readonly Action<double> mOnProgress;
    readonly Action<string> mOnError;
    bool mDisposed;
}
