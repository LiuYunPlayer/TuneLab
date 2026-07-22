using System;
using System.Collections.Generic;

namespace TuneLab.SDK;

// 曲线按查询点批量求值的服务接口（voice 与 effect 共用；机制轴无关，可复用于将来的局部曲线求值）：
// 调用方递一列查询点 + 输出缓冲，拿回该曲线（含 vibrato/默认值等最终合成结果）在各点的值。
// **查询点所在的坐标轴由「提供该求值器的一方」规定、不由本接口固定**——当前宿主交付的所有求值器
// （IVoiceSynthesisContext.Pitch/PitchDeviation/Automations、各 *Snapshot 的 Evaluator 等）均以全局秒为轴；
// tick 等宿主内部表示不外露、插值算法恒在宿主侧完成，调用方不需要懂坐标换算与插值。
public interface IAutomationEvaluator
{
    // positions = 查询点（轴由求值器提供方规定，当前均为全局秒），**必须按非降序（升序，允许相等）排列**——实现
    // 走只进不退的游标沿曲线扫描（O(n) 而非每点二分），是硬前提而非优化建议。乱序传入不报错、但写入该点的未定义
    // 值（游标已越过）；调用方须先排序。
    // 输出缓冲 results 由调用方提供、**须与 positions 等长**；实现按点写入 results[i]，与 positions 一一对应、同序。
    // 由调用方掌控输出内存，便于在热路径（逐轨 / 逐曲线 / 逐段反复求值）复用同一 scratch、免每次分配。
    // 想要旧的「返回一个新数组」写法：用扩展 IAutomationEvaluatorExtensions.Evaluate(positions)。
    void Evaluate(IReadOnlyList<double> positions, Span<double> results);
}

// IAutomationEvaluator 的便利扩展：分配一个与 positions 等长的新数组、求值后返回——与移出输出前的旧签名同构，
// 供不在意分配的调用方一行取用（热路径想省内存则直接用接口的 buffer 版、复用自持 scratch）。
public static class IAutomationEvaluatorExtensions
{
    public static double[] Evaluate(this IAutomationEvaluator evaluator, IReadOnlyList<double> positions)
    {
        var results = new double[positions.Count];
        evaluator.Evaluate(positions, results);
        return results;
    }
}
