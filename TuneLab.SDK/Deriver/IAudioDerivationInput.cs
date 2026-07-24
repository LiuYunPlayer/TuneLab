using TuneLab.Foundation;

namespace TuneLab.SDK;

// deriver 的输入面（宿主实现、插件只读）：一份冻结的多声道音频快照 + 冻结的参数值。
// 与 effect 的 IEffectSynthesisAudio 有意不共类型：那面是单声道、只读活视图、且带内容变更账本 RangeModified
// （为反应式局部重合成的缓存服务）；deriver 是离线长耗时、输入固定的一次性任务——不需要变更信号与活视图语义，
// 反而必须支持多声道。强行共用会把 effect 的反应式包袱塞给 deriver、又把多声道需求压给 effect，故各自独立输入面。
//
// 宿主在数据线程物化本快照（音频 copy-out、参数冻结），再 offload 给 worker 跑 Derive——worker 只读快照、
// 永不回碰宿主活数据。冻结不可变：Derive 全程音频不变，故不带任何 RangeModified 之类信号。
//
// 时间基：样本索引即音频内容自身时间（采样点 0 = 内容 0 秒），位置无关（不含工程落点）；产物坐标同此音频相对秒。
public interface IAudioDerivationInput
{
    int SampleRate { get; }
    // 声道数；宿主不下混，交由插件决定如何使用各声道。
    int ChannelCount { get; }
    // 每声道样本数。
    long SampleCount { get; }
    // 把某声道 [offset, offset+destination.Length) 拷出到调用方缓冲；越界（channel/offset/长度）非法。
    // 插件心智模型是随机访问一个完整数组（知总长、任取区间读、自带缓冲），但拿不到宿主内部存储引用——
    // 宿主存储形态（连续数组 / 分页）是实现细节、可无缝演进。
    void Read(int channel, long offset, Span<float> destination);
    // 冻结的参数值（对应 GetPropertyConfig 声明的参数面板在提交时的取值）。
    PropertyObject Properties { get; }
}
