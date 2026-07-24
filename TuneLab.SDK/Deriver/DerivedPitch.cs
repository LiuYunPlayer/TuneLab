using TuneLab.Foundation;

namespace TuneLab.SDK;

// 派生音高曲线：nested-segments 的具名封装。与合成侧 SynthesizedPitch 同形不共类型（后者全局工程秒、反应式回显；
// 本类音频内容秒、一次性产物）。包成类而非裸 IReadOnlyList<IReadOnlyList<Point>>：留加性演化面（将来可长
// 清浊 / 颤音分解 / 置信度等专属维度，裸容器加不了）。
public sealed class DerivedPitch
{
    // 各连续段，按时间升序、互不重叠；段内 Point =（绝对音频内容秒, MIDI 半音 float）。空集 = 整条无值；段间间隙 = 自由区（绘制断开）。
    public IReadOnlyList<IReadOnlyList<Point>> Segments { get; init; } = [];
}
