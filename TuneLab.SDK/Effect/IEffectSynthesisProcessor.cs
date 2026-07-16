using TuneLab.Foundation;

namespace TuneLab.SDK;

// 一条「effect 实例 × 一个上游音频段」的持久厚处理器：持有自己那一段的上下文（IEffectContext），
// 自管该段生命周期的失效与重处理。段间彼此无共享上下文——各段分别处理后由宿主按时间混音。
//
// 失效自管：处理器订阅 context（Input.Committed / Properties.Modified / 各 automation.RangeModified）
// 自算 dirty，于 context.Committed（逻辑编辑收口）一次性触发 ProcessingRequested；宿主据此调度 Process。
// 引擎私有失效图（哪条参数/哪段自动化标脏的内部内容各异）就此落在处理器内部，宿主无从复制。
// 无内部增量可做的引擎"任何信号 → 整段重处理"即可。
//
// 线程纪律：Process 的同步前缀（数据线程）读 context——抓 Input.Samples 引用 + 预采自动化/参数值；
// 之后 offload 到 worker，worker 只读同步前缀物化的不可变值（合成永不回碰宿主活数据）。
public interface IEffectProcessor : IDisposable
{
    // (重)处理本段：同步前缀读 context 后 offload；产物经 context.CreateAudioSegment 写出并 Commit。
    // 语义同 voice SynthesizeNext：返回纯 Task（无 outcome）——取消是正常调度结局，经 cancellation 请求、
    // 不抛 OperationCanceledException、正常返回（不可中止的引擎把这段跑完才返回）；await 真正返回 = 槽位释放；
    // 错误抛异常，宿主在调用边界 catch → 该段 passthrough 降级。
    Task Process(CancellationToken cancellation = default);

    // 本段的参数回显曲线（按轨 id 键、与音频产物同一秒时间系）：key 与 IEffectEngine.GetSynthesizedParameterConfigs
    // 对齐，仅承载曲线数据本身（轨形态/色由 config 给）；每条为分段折线富类型 SynthesizedParameter。
    // 处理器是「effect × 单段」粒度，故本属性只覆盖本段；宿主把同一 effect 各段的回显按 key 拼接呈现。
    // 线程契约同音频产物：由处理器在数据线程发布（Process 续延里换引用），发布的集合即不可变、宿主跨线程只读；
    // 宿主在 Process 收尾随产物一并重读。无回显的引擎返回空 map 即可。
    IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; }

    // 处理器自标脏后触发（恒在数据线程）；宿主据此调度 Process。
    IActionEvent ProcessingRequested { get; }
}
