using TuneLab.Foundation;

namespace TuneLab.SDK;

// 时间轴画布（TimelineCanvasConfig）的会话级活视图（宿主实现）：值 + 换算 + 带区间的变化通知。与 ISynthesisAutomation 同级，
// 经合成上下文的 TimelineCanvases 按 id 取（只有当前声明了的 id）。只在数据线程上用；worker 里用快照的 TimelineCanvases。
public interface ISynthesisTimelineCanvas
{
    // 当前值（插件自定的整值，宿主不解读）；没设过 = Null，插件按自己的默认值补。
    PropertyValue Value { get; }

    // 局部 tick ↔ 全局秒：值里的 tick 用它换成秒（原点与画布上 TimeAxis.TickToX(0) 是同一个，已含宿主维护的偏移，插件无感）。
    // 会话内不变（速度表改动、part 平移走会话整体重建）；不可变、任何线程可用。
    ITimeConverter TimeConverter { get; }

    // (startTime, endTime)，全局秒：值变了，变的是这一段（画布提交时报的范围换成秒，原样、不合并；报了几段就触发几次；声源 / 乐器上下文在一次写入的
    // 各段发完后补一次 Committed，重活宜留到那里）。不带范围的提交、宿主发起的写入（预设、脚本 / agent 写值……）按整条通知：
    // (-∞, +∞)。撤销 / 重做原样重发提交时的范围。
    IActionEvent<double, double> RangeModified { get; }
}
