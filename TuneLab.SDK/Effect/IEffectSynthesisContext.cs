using TuneLab.Foundation;

namespace TuneLab.SDK;

// 一个厚 IEffectSynthesisSession 的输入上下文：宿主实现、绑定「该 effect 实例 × 一个上游音频段」、随处理器死。
// 失效判定权归宿主（见 IEffectSynthesisSession）——本 context 的颗粒事件只是缓存型引擎的可选信息源。
// 仅数据线程访问（活视图纪律；offload 前在同步前缀物化）。
//
// 坐标系约定：自动化查询轴 = 全局秒，与音频产物、状态段同一时间系（与 IVoiceSynthesisContext 一致）；
// 段内寻址 int、全局轴位置 long（全局 0 时刻 = 采样点 0）。
public interface IEffectSynthesisContext
{
    // 本段输入（上游 voice 输出，或链上前一个 effect 的输出）：整段不可分割的只读音频面。
    // Process 被调到即输入已就绪（调度归宿主，无「已提交」脉冲）；Input.RangeModified 是内容变更的
    // 区间账本（可选缓存信息源）。经 Input.Read 按区间拷出（同步前缀物化到处理器自有缓冲）。
    IEffectSynthesisAudio Input { get; }

    // 该 effect 自身参数活视图（同步前缀读值物化；Modified 为可选的缓存刷新提示）。
    IReadOnlyNotifiablePropertyObject Properties { get; }

    // 该 effect 声明的连续自动化轨活视图（按 key；查询轴 = 全局秒）：只读 map，可枚举可点取
    // （分段轨不在此列）。RangeModified 为可选的缓存刷新提示（调度所需的区间相交判定由宿主完成）。
    IReadOnlyMap<string, ISynthesisAutomation> Automations { get; }

    // 拉取跨线程冻结快照（仅 Process 同步前缀调用、可拉多份；[startTime, endTime] 为自动化开窗的全局秒
    // 区间，通常 = 输入段范围 ± 引擎自己的上下文窗）。worker 只读快照 + 自己 Read 出的音频缓冲——合成
    // 永不回碰宿主活数据（与 voice GetSnapshot 同判例）。前缀可枚举采样时刻的简单引擎直接对活视图
    // Evaluate 成数组即可，无须快照。
    EffectSynthesisSnapshot GetSnapshot(double startTime, double endTime);

    // 产出（与 voice 同一登记表语义、同一握柄 IAudioSegment）：产出分段自由——可建多段（如按静音切分的
    // splitter，为下游重建段粒度换取按段增量与并行），各段独立 Write/Commit/Dispose；宿主把每个已提交
    // 输出段接成下游 effect 的输入。唯一红线是**不得重新分布时间轴**（automation/回显与 part 显示共
    // 全局秒轴，变速/挪时间类引擎不属此管线）；几何微差（帧对齐补边、加尾）合法。
    // 改段几何 = Dispose 旧段建新段；就地重写重 Commit 不换段。仅数据线程调用。
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate);
}
