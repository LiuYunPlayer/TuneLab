namespace TuneLab.SDK;

// automation 轨的会话级活视图：求值 + 区间变更订阅。查询轴与区间事件均为全局秒。
// 插件由此做最细粒度失效："某轨 [startTime, endTime) 变了 → 只标脏覆盖该区间的段"。
//
// 继承 IAutomationEvaluator 是 is-a：活/冻两面共用同一份求值签名（同一采样语义），
// 活视图只额外多一个区间订阅事件；冻结面（SynthesisAutomationSnapshot）则只取求值能力。
public interface ISynthesisAutomation : IAutomationEvaluator
{
    // (startTime, endTime)，全局秒。除曲线本身编辑外，tempo 变化导致的秒轴位移也经此通知
    // （同一 tick 锚点经新 tempo 映射到不同秒位置 → 宿主在批量括号内对受影响轨触发全区间）。
    event Action<double, double>? RangeModified;
}
