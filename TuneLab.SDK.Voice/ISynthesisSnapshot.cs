using System.Diagnostics.CodeAnalysis;
using TuneLab.Primitives.Property;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.Timing;

namespace TuneLab.SDK.Voice;

// 宿主物化的不可变合成快照：SynthesizeNext 的同步前缀在数据线程按 segment 的捕获声明
// eager 物化，之后才 offload 到 worker——worker 永不碰活对象，只读它。
// 形状与 ISynthesisContext 活视图镜像对称，但纯值无事件（"把回调留到合成线程"在类型上写不出来）。
//
// 替换，而非同步：快照只写一次（构造，单线程），构造 happens-before worker 启动，此后只读；
// 数据变了走活视图通知 → 插件标脏 → 下次 GetNextSegment 出新段 → 捕获一份全新快照。
// 无共享可变状态，无需锁。物化/版本缓存/限速/并发记账全留宿主一处；
// 跨进程演进时它就是序列化消息体（一次过线，非 N 次 RPC）。
public interface ISynthesisSnapshot
{
    // 不可变值 record，段内 Next/Last 成链；与 segment.Notes 索引对齐。
    IReadOnlyList<SynthesisNoteSnapshot> Notes { get; }

    // tempo 快照：tick↔秒换算与活视图同一套共享算法，可安全跨线程。
    ITiming Timing { get; }

    // 冻结取值器：对按声明区间开窗捕获的原始锚点就地插值，窗口内取值与全曲线逐点全等。
    IAutomationValueGetter Pitch { get; }
    bool TryGetAutomation(string key, [MaybeNullWhen(false)] out IAutomationValueGetter automation);

    // 值拷（不可变 PropertyObject）。
    PropertyObject PartProperties { get; }
}
