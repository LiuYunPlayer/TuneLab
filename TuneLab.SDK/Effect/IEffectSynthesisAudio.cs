using TuneLab.Foundation;

namespace TuneLab.SDK;

// effect 合成域的音频面（只读活视图）：整段不可分割的音频内容 + 内容变更的区间账本。
// 当前唯一站位是 IEffectSynthesisContext.Input（上游 voice 输出或链上前一个 effect 的输出）；
// 角色由站位的属性名承载，本类型只命名能力——将来如兄弟段只读视图等新站位可直接复用。
public interface IEffectSynthesisAudio
{
    // 段起始（全局采样位置，按 SampleRate 计；全局 0 时刻 = 采样点 0。全局轴 long、段内 int）。
    long SampleOffset { get; }
    int SampleCount { get; }
    int SampleRate { get; }

    // 段内 [offset, offset+destination.Length) 拷出到调用方缓冲；越界非法。
    // 插件心智模型是随机访问一个完整数组（知总长、任取区间读、自带缓冲），但永远拿不到宿主内部
    // 存储的引用——宿主存储形态（连续数组/分页）是实现细节，可无缝演进。
    // 仅数据线程调用（Process 同步前缀物化）——worker 只读拷出后的自有缓冲；局部重合成的引擎只读脏区间±上下文窗。
    void Read(int offset, Span<float> destination);

    // 内容变更的区间账本（可选缓存信息源，数据线程触发）：重 Commit 时按段内区间上报，
    // 参数 = (offset, count)，即 [offset, offset+count) 已更新（区间平铺为参数对，不引入冻结区间类型——
    // 与 automation RangeModified、状态段同一判例；区间运算归消费方）。
    // 账本语义：缓存型引擎累积进自己的待处理集、在**成功产出后**才清账（取消/失败不清、不丢更新），
    // 配合电平式 Process 决定「重算哪块」；宿主对来源不明/整段重建的提交如实报整段区间。
    // 无局部能力的引擎可零订阅（被调到就整段重处理，Process 被调到即内容已就绪——调度归宿主，无「已提交」脉冲）。
    IActionEvent<int, int> RangeModified { get; }
}
