using System.Collections.Generic;

namespace TuneLab.SDK;

// 自动化轨按时间点批量求值的服务接口（voice 与 effect 共用）：调用方递一列时间点，
// 拿回该轨（含 vibrato/默认值等最终合成结果）在各点的值。
// 本接口不定义时间轴：查询轴由暴露它的成员契约规定（如 voice 面 = 全局 tick，
// effect 面 = 全局秒），形参名取中性的 points。插值算法恒在实现侧（宿主），调用方不需要懂插值。
public interface IAutomationEvaluator
{
    double[] Evaluate(IReadOnlyList<double> points);
}
