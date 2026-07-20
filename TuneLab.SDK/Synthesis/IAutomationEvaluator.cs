using System.Collections.Generic;

namespace TuneLab.SDK;

// 自动化轨按时间点批量求值的服务接口（voice 与 effect 共用）：调用方递一列时间点，
// 拿回该轨（含 vibrato/默认值等最终合成结果）在各点的值。
// 查询轴 = 全局秒：插件统一面对秒轴（合成域即秒/采样点），tick 仅是宿主乐谱内部表示、不外露；
// 宿主在实现侧完成秒↔tick 换算与插值，调用方不需要懂 tick 也不需要懂插值。
public interface IAutomationEvaluator
{
    // times = 全局秒，**必须按非降序（升序，允许相等）排列**——实现走只进不退的游标沿曲线扫描（O(n) 而非
    // 每点二分），是硬前提而非优化建议。乱序传入不报错、但返回该点的未定义值（游标已越过）；调用方须先排序。
    // 返回数组与 times 一一对应、同序、同长。
    double[] Evaluate(IReadOnlyList<double> times);
}
