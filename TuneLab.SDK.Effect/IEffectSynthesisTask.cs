using System;

namespace TuneLab.SDK.Effect;

// 一次效果器合成任务（一次性处理器）：Start 后异步处理整段音频，完成时写好 output 并触发 Complete。
// 失效/参数变化由宿主负责——宿主丢弃旧任务、用更新后的输入重建任务，引擎自身无需感知 dirty。
public interface IEffectSynthesisTask
{
    event Action? Complete;
    event Action<double>? Progress;
    event Action<string>? Error;

    void Start();
    void Stop();
}
