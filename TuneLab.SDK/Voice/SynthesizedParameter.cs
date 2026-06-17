using TuneLab.Foundation;

namespace TuneLab.SDK;

// 参数回显曲线产物（engine→host）：一条只读回显轨的曲线数据，具名冻结值类型。
// 命名与其他产物一致用 Synthesized* 前缀（避开宿主数据层占用的 PiecewiseCurve）。
// 形态用 nested-segments：产物本由合成器预分段、渲染端要段折线、不想逐点扫 NaN。
// 与 SynthesizedPitch 有意解耦不共类型：pitch 是固定专属通道（宿主全知其色/量程），
// parameter 是动态 keyed 集合（引擎声明、自带 Min/Max/Color 元数据）——给参数富类型恰配其更富生态。
public sealed class SynthesizedParameter
{
    // 各连续段，按时间升序、互不重叠；段内 Points 为 (全局秒, 值) 折线。
    // 空集合 = 整条无值；段间间隙 = NaN 区（绘制断开）。
    public required IReadOnlyList<IReadOnlyList<Point>> Segments { get; init; }
}
