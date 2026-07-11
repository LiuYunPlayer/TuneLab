using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 宿主物化的不可变合成快照（context.GetSnapshot 的返回体）：插件在 SynthesizeNext 的
// 同步前缀（数据线程）主动拉取，之后才 offload——worker 永不碰活对象，只读它。
// 形状与 IVoiceSynthesisContext 活视图镜像对称，但为纯数据体：无事件（"把回调留到合成线程"在
// 类型上写不出来）、无活引用；构造形态 = 无参 + required init（初始化后不可变，加字段纯加性）。
//
// 替换，而非同步：快照只写一次（构造，数据线程），构造 happens-before worker 启动，此后只读；
// 数据变了走活视图通知 → 插件标脏 → 下次合成拉一份全新快照。无共享可变状态，无需锁。
// 跨进程演进时它就是 GetSnapshot 的一次批量返回体。
//
// 全秒轴：所有时间量（note 边界、求值查询点）均为全局秒，快照不携带 tick↔秒换算服务
// （插件不碰 tick）。automation 冻结形态 = 原始锚点 + 冻结求值器（按需插值，秒轴）：查询点
// 常是合成的中间产物（音素定时后才知道在哪采），快照时刻预知不了；想"冻结时算好"的插件在
// 同步前缀调求值器把值采成 double[] 自存即可。原始锚点不暴露、插值算法恒在宿主侧（杜绝两套
// 插值漂移；v2 跨进程在快照序列化时物化为离散点）。
public sealed class VoiceSynthesisSnapshot
{
    // 不可变值快照，有序列表；与 GetSnapshot 递入的 notes 索引对齐（产物归属契约），邻居按索引导航。
    public required IReadOnlyList<VoiceSynthesisNoteSnapshot> Notes { get; init; }

    // automation 冻结快照（可扩展容器，见 SynthesisAutomationSnapshot）：当前裹一个全局秒轴求值器，
    // 开窗 = 拉取区间内原始锚点就地插值。Pitch/PitchDeviation 双通道语义与活视图镜像
    // （绝对约束 NaN=自由 / 加性偏差永不 NaN）：finalPitch(t) = resolve(Pitch(t)) + PitchDeviation(t)。
    public required SynthesisAutomationSnapshot Pitch { get; init; }
    public required SynthesisAutomationSnapshot PitchDeviation { get; init; }

    // 值拷（不可变 PropertyObject）。
    public required PropertyObject PartProperties { get; init; }

    // 已声明 automation 轨按开窗区间物化（无数据对象的轨冻结为默认值常量），只读 map：可枚举可点取。
    // 与活视图 IVoiceSynthesisContext.Automations 同构（轨集 = 引擎声明集）；Pitch/PitchDeviation 两条
    // 固定通道独立、不在此 map。物化已在宿主侧一次完成，直接暴露 map 零额外负担、跨进程也免反复回调。
    public required IReadOnlyMap<string, SynthesisAutomationSnapshot> Automations { get; init; }

    // 音素布局在 SDK：把每 note 的标称音素（SynthesizedPhoneme：时长 / 权重）+ 双列表分类 + BodyOffset + 几何锚点（note 头 / 核填充终点）
    // 解析为跨 note 去重叠后的真实时序，是 PhonemeLayout.Resolve 这一纯函数。引擎调它即与宿主显示完全一致
    // （核起点=音符头、可伸音素按缩放比 r^w 等比伸缩、刚性辅音不动、有空隙则互不影响）；冻结的只是 I/O 形状、分配体仍可宿主侧演进，
    // 引擎运行时绑定宿主的这一份故永不漂移。想自管音频摆放的引擎可不调、自由放置——错位非致命。
}
