using TuneLab.Foundation;

namespace TuneLab.SDK;

// automation 轨的会话级活视图：求值 + 区间变更订阅。查询轴与区间事件均为全局秒。
// 插件由此做最细粒度失效："某轨 [startTime, endTime) 变了 → 只标脏覆盖该区间的段"。
//
// 继承 IAutomationEvaluator 是 is-a：活/冻两面共用同一份求值签名（同一采样语义），
// 活视图只额外多一个区间订阅事件；冻结面（SynthesisAutomationSnapshot）则只取求值能力。
public interface ISynthesisAutomation : IAutomationEvaluator
{
    // (startTime, endTime)，全局秒。只通知曲线本身的编辑；tempo 变化导致的秒轴位移**不**经此通知
    // ——时基变更走会话整体重建（见 IVoiceSynthesisContext 头注释），重建后的新视图即新秒轴。
    IActionEvent<double, double> RangeModified { get; }
}
