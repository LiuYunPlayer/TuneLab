using System;
using System.Collections.Generic;

namespace TuneLab.SDK;

// 自动化轨按时间点批量求值的服务接口（voice 与 effect 共用）：调用方递一列时间点 + 输出缓冲，
// 拿回该轨（含 vibrato/默认值等最终合成结果）在各点的值。
// 查询轴 = 全局秒：插件统一面对秒轴（合成域即秒/采样点），tick 仅是宿主乐谱内部表示、不外露；
// 宿主在实现侧完成秒↔tick 换算与插值，调用方不需要懂 tick 也不需要懂插值。
public interface IAutomationEvaluator
{
    // times = 全局秒，**必须按非降序（升序，允许相等）排列**——实现走只进不退的游标沿曲线扫描（O(n) 而非
    // 每点二分），是硬前提而非优化建议。乱序传入不报错、但写入该点的未定义值（游标已越过）；调用方须先排序。
    // 输出缓冲 results 由调用方提供、**须与 times 等长**；实现按点写入 results[i]，与 times 一一对应、同序。
    // 由调用方掌控输出内存，便于在热路径（逐轨 / 逐曲线 / 逐段反复求值）复用同一 scratch、免每次分配。
    // 想要旧的「返回一个新数组」写法：用扩展 IAutomationEvaluatorExtension.Evaluate(times)。
    void Evaluate(IReadOnlyList<double> times, Span<double> results);
}

// IAutomationEvaluator 的便利扩展：分配一个与 times 等长的新数组、求值后返回——与移出输出前的旧签名同构，
// 供不在意分配的调用方一行取用（热路径想省内存则直接用接口的 buffer 版、复用自持 scratch）。
public static class IAutomationEvaluatorExtension
{
    public static double[] Evaluate(this IAutomationEvaluator evaluator, IReadOnlyList<double> times)
    {
        var results = new double[times.Count];
        evaluator.Evaluate(times, results);
        return results;
    }
}
