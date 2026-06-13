using System.Collections.Generic;

namespace TuneLab.SDK;

// automation 轨的不可变冻结快照（worker 线程、将来 worker 进程只读）：对按开窗区间捕获的
// 原始锚点就地插值，窗口内取值与全曲线逐点全等。查询轴 = 全局秒。
//
// 与活视图 ISynthesisAutomation 镜像对称但去事件（"把回调留到合成线程"在类型上写不出来）。
// 当前只承载求值能力，故 = 一个 IAutomationEvaluator 的具名封装；具名（而非裸接口）是为了
// 与 SynthesisNoteSnapshot 对称、且给冻结面将来加字段（值域/单位等）留无破坏通道——
// 加字段纯加性，不动 IReadOnlyList<...> 等承载它的集合签名。
public sealed class SynthesisAutomationSnapshot : IAutomationEvaluator
{
    public SynthesisAutomationSnapshot(IAutomationEvaluator evaluator)
    {
        mEvaluator = evaluator;
    }

    public double[] Evaluate(IReadOnlyList<double> times) => mEvaluator.Evaluate(times);

    readonly IAutomationEvaluator mEvaluator;
}
