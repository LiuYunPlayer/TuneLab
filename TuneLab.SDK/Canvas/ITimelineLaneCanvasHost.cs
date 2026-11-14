using System.Collections.Generic;
using TuneLab.Foundation;

using Range = TuneLab.Foundation.Range;

namespace TuneLab.SDK;

// 时间轴 lane 画布（TimelineLaneCanvasConfig）的宿主：与时间轴画布一样有时间轴（横）与音高轴（纵），另多一个带改动范围的提交。
// 局部 tick 的原点 = part 原点（TimeAxis.TickToX(0)）；其余成员与侧栏画布同一套契约。
public interface ITimelineLaneCanvasHost : ICanvasHost
{
    ICanvasTimeAxis TimeAxis { get; }
    ICanvasPitchAxis PitchAxis { get; }

    // 提交草稿，并说明这次改了哪几段（局部 tick，每段 [Start, End)）：值与范围一起交给宿主，是一次原子的提交。宿主把范围随这次提交
    // 记下（撤销 / 重做原样重发），换成秒交给合成侧（声源经 ISynthesisTimelineLaneCanvas.RangeModified 每段一次，效果器由宿主按区间标脏）。
    // · 范围原样记录、原样通知，宿主**不合并**：重叠、重复的段由插件自己去掉。按这次提交的最终结果报——拖动途中经过、最终没变的地方不算。
    // · 空列表 = 整条都算改了（同不带参数的 CommitValue）。Start > End 时宿主交换两端；含非有限值的段忽略（记一条错误）。
    // · 不带参数的 CommitValue()（继承自 ICanvasHost）= 整条都改了：整条替换（清空、导入、重置）时用它；只改了局部时用本重载。
    // 其余（没有草稿则什么都不做、实例作废后不生效）同 CommitValue()。
    void CommitValue(IReadOnlyList<Range> modifiedTickRanges);
}
