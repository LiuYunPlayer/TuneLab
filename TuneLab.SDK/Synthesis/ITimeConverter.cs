using System.Collections.Generic;

namespace TuneLab.SDK;

// 时间换算器（宿主实现）：局部 tick ↔ 全局秒。不可变、任何线程都能用。
//
// tick 相对这份换算器的原点（谁给出谁定原点：画布 lane 的是所属音符的起点，合成上下文的是 part 的原点），局部 tick 可以为负；
// 秒是全局秒，与插件拿到的其余时间（音符 StartTime、自动化轨求值、音频段）同一口径。速度表改动后宿主给出的是另一份，旧的这份不变。
// 只有批量版：输入须**升序**（宿主单次扫描实现，乱序结果未定义）；单点换算用扩展方法 ToSecond / ToTick（TimeConverterExtensions）。
// 命名：换算按单位（Second / Tick），单复数表元数——一个点用单数、一组用复数。
public interface ITimeConverter
{
    double[] ToSeconds(IReadOnlyList<double> ticks);
    double[] ToTicks(IReadOnlyList<double> seconds);
}
