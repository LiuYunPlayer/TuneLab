using System.Collections.Generic;

namespace TuneLab.SDK;

// automation 轨的不可变冻结快照（worker 线程、将来 worker 进程只读）：作为**可扩展具名容器**，
// 而非直接暴露裸 IAutomationEvaluator——当前只承载一个求值器（Evaluator），但将来 automation
// 冻结面需带元数据（DefaultValue / 值域 / 单位等）时，加 required init 属性即可，对插件**纯加性、
// 不破 ABI**；裸接口则只能换类型（source/ABI-breaking）。这是冻结面用容器占位的前瞻投资。
//
// 用组合（持有 Evaluator）而非实现 IAutomationEvaluator：它是"含求值器的快照容器"（has-a），
// 不是"一个求值器"（is-a），语义诚实、不混淆。
public sealed class SynthesisAutomationSnapshot
{
    // 冻结求值器（查询轴 = 全局秒）：对开窗区间捕获的原始锚点就地插值，窗口内取值与全曲线逐点全等。
    public required IAutomationEvaluator Evaluator { get; init; }
}
