using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 一个厚 IEffectProcessor 的输入上下文：宿主实现、绑定「该 effect 实例 × 一个上游音频段」、随处理器死。
// 处理器订阅它自管失效（见 IEffectProcessor）。仅数据线程访问（活视图纪律；offload 前在同步前缀物化）。
//
// 坐标系约定：自动化查询轴 = 全局秒，与音频产物、状态段同一时间系（与 ISynthesisContext 一致）。
public interface IEffectContext
{
    // 本段输入：整段不可分割（上游 voice 输出，或链上前一个 effect 的输出）。worker 直读其不可变 PCM
    // （按 CommitVersion，重 Commit 换新缓冲）——不剪切、不开窗、无快照拷贝。
    IUpstreamAudioSegment Input { get; }

    // 该 effect 自身参数活视图（订阅 Modified 标参数脏；同步前缀读值物化）。
    IReadOnlyNotifiablePropertyObject Properties { get; }

    // 按标识取该 effect 声明的一条自动化轨活视图（查询轴 = 全局秒）；不存在该轨时返回 false。
    // 处理器订阅 ILiveAutomation.RangeModified、按本段时间界自筛标脏。
    bool TryGetAutomation(string key, [MaybeNullWhen(false)] out ILiveAutomation automation);

    // 产出（与 voice 同一握柄 IAudioSegment）：自由重分段——输出段起始/长度/采样率均可与输入不同，
    // 可一段进多段出。宿主把输出段接成下游 effect 的 Input。仅数据线程调用。
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate);

    // 逻辑编辑收口（同 ISynthesisContext.Committed）：颗粒事件（Input.Committed/Properties.Modified/
    // automation.RangeModified）标脏后此处一次性收口，处理器在此判脏触发 ProcessingRequested、做重活。
    event Action? Committed;
}

// 上游音频段的只读视图（voice 输出，或链上前一个 effect 的输出）：整段、不可分割。
// 已提交版本的 PCM 不可变（重 Commit = 换新缓冲、版本递增），故 worker 在同步前缀抓住引用后可直读。
public interface IUpstreamAudioSegment
{
    long SampleOffset { get; }
    int SampleCount { get; }
    int SampleRate { get; }

    // 已提交版本不可变整段 PCM；同步前缀抓引用、worker 直读。
    ReadOnlyMemory<float> Samples { get; }

    // 重 Commit 递增，处理器据此判是否需重处理。
    int CommitVersion { get; }

    // 内容变（未来可加性补 RangeCommitted 局部更新信号）。
    event Action? Committed;
}
