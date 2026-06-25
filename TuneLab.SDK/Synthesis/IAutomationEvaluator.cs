using System.Collections.Generic;

namespace TuneLab.SDK;

// 自动化轨按时间点批量求值的服务接口（voice 与 effect 共用）：调用方递一列时间点，
// 拿回该轨（含 vibrato/默认值等最终合成结果）在各点的值。
// 查询轴 = 全局秒：插件统一面对秒轴（合成域即秒/采样点），tick 仅是宿主乐谱内部表示、不外露；
// 宿主在实现侧完成秒↔tick 换算与插值，调用方不需要懂 tick 也不需要懂插值。
public interface IAutomationEvaluator
{
    double[] Evaluate(IReadOnlyList<double> times);   // times = 全局秒
}
