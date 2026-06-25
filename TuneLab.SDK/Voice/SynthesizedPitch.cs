using TuneLab.Foundation;

namespace TuneLab.SDK;

// 合成音高产物（engine→host）：一条只读回显轨的音高曲线数据，具名冻结值类型。
// 命名与其他产物一致用 Synthesized* 前缀。形态用 nested-segments：产物本由合成器预分段、
// 渲染端要段折线、不想逐点扫 NaN——各连续段按时间升序、互不重叠；段内 Points 为 (全局秒, 半音值) 折线。
//
// 与 SynthesizedParameter 有意解耦不共类型：pitch 是固定专属通道（宿主全知其色 / 量程），
// 将来要加清浊 / 颤音分解等专属维度，与通用 keyed 参数（引擎声明、自带 Min/Max/Color）不同线、各自演进。
public sealed class SynthesizedPitch
{
    // 各连续段，按时间升序、互不重叠；段内 Points 为 (全局秒, 半音值) 折线。
    // 空集合 = 整条无值；段间间隙 = 自由区（绘制断开）。
    public required IReadOnlyList<IReadOnlyList<Point>> Segments { get; init; }
}
