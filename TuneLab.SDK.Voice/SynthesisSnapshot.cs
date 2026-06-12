using TuneLab.Primitives.DataStructures;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.Timing;

namespace TuneLab.SDK.Voice;

// 宿主物化的不可变合成快照（context.GetSnapshot 的返回体）：插件在 SynthesizeNext 的
// 同步前缀（数据线程）主动拉取，之后才 offload——worker 永不碰活对象，只读它。
// 形状与 ISynthesisContext 活视图镜像对称，但为纯数据体（数据=具体类型）：无事件
// （"把回调留到合成线程"在类型上写不出来）、无活引用；构造形态 = 无参 + required init
// （初始化后不可变，加字段纯加性）。
//
// 替换，而非同步：快照只写一次（构造，数据线程），构造 happens-before worker 启动，此后只读；
// 数据变了走活视图通知 → 插件标脏 → 下次合成拉一份全新快照。无共享可变状态，无需锁。
// 跨进程演进时它就是 GetSnapshot 的一次批量返回体。
//
// automation 冻结形态 = 原始锚点 + 冻结 getter（按需插值）：查询点常是合成的中间产物
// （音素定时后才知道在哪采），快照时刻预知不了；想"冻结时算好"的插件在同步前缀调 getter
// 把值采成 double[] 自存即可（推荐模式：前缀预采，worker 内调用仅留给依赖中间产物的动态点）。
// 原始锚点不暴露、插值算法恒在宿主侧（杜绝两套插值漂移；v2 时 getter 即批量 RPC 接缝）。
public sealed class SynthesisSnapshot
{
    // 不可变值快照，有序列表；与 GetSnapshot 递入的 notes 索引对齐（产物归属契约），邻居按索引导航。
    public required IReadOnlyList<SynthesisNoteSnapshot> Notes { get; init; }

    // tick↔秒换算服务（接口接缝，实现在宿主侧、与活视图同一套共享算法）：冻结实现可安全跨线程。
    // v2 跨进程时随快照序列化物化（细节缓后）。tempo 标记明牌数据有需求时纯加性补回（如 Tempos 字段）。
    public required ITiming Timing { get; init; }

    // 冻结取值器：对按开窗区间捕获的原始锚点就地插值，窗口内取值与全曲线逐点全等。
    // Pitch/PitchDeviation 双通道语义与活视图镜像（绝对约束 NaN=自由 / 加性偏差永不 NaN）：
    // finalPitch(t) = resolve(Pitch(t)) + PitchDeviation(t)。
    public required IAutomationValueGetter Pitch { get; init; }
    public required IAutomationValueGetter PitchDeviation { get; init; }

    // 全部已声明轨按开窗区间物化（纯数据体：可枚举 Map，而非查询方法）。
    public required IReadOnlyMap<string, IAutomationValueGetter> Automations { get; init; }

    // 值拷（不可变 PropertyObject）。
    public required PropertyObject PartProperties { get; init; }
}