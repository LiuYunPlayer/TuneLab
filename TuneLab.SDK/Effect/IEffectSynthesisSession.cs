using TuneLab.Foundation;

namespace TuneLab.SDK;

// 一条「effect 实例 × 一个上游音频段」的合成会话：与 voice/instrument 会话同族的**持久有状态实体**——
// 持有活视图上下文（IEffectSynthesisContext）、跨 Process 调用维护内部缓存、发布声称与回显。
// 作用域差异由 context 绑定表达（voice 会话绑 part、effect 会话绑段），不进类型名；
// 「一次跑完丢光状态」只是最简引擎的退化用法，不是本类型的身份。
// 段间彼此无共享上下文——各段会话分别处理后由宿主按时间混音。
//
// 失效判定权归宿主：宿主按作用域信号（本段输入重 Commit / 本 effect 参数变 / 本 effect 自动化变更区间
// 与本段相交）保守调度 Process，会话不承担任何失效上报义务（编辑批量/收口/防抖全在宿主，会话不可见）。
// context 的颗粒事件（Input.RangeModified / Properties.Modified / automation.RangeModified）是缓存型引擎
// 「该重算哪块」的可选信息源，简单引擎可以零订阅。**有意保留事件面而非打包进 Process 参数**：
// 数据线程上的事件流是全序的（区间变更与参数变更的交错顺序对纪元敏感的引擎是有效信息），打包交付会
// 丢时序；且「何时清账」只有引擎自己知道（合法早退不 Commit ≠ 未消费），记账义务归唯一知情者。
//
// 线程纪律：Process 的同步前缀（数据线程）读 context——经 Input.Read 把所需区间拷出到自有缓冲
// + 预采自动化/参数值；之后 offload 到 worker，worker 只读同步前缀物化的自有拷贝（合成永不回碰宿主活数据）。
//
// 加性约定（插件实现面）：将来在本面新增成员一律用默认接口方法（DIM）给兜底体，使增补不破已装插件。
public interface IEffectSynthesisSession : IDisposable
{
    // (重)处理本段。**电平语义，非边沿语义**：契约是「让输出与当前输入一致」，不是「应用某次变更」——
    // 同步前缀读到的就是此刻最新真相，期间发生过几次编辑、以何种顺序，与会话无关也不可见。
    // 宿主是保守调度（无法判定参数依赖/值级去重），引擎自比缓存后判定输出不会变时直接返回、不重 Commit
    // 即可（下游据此被跳过）；无内部增量可做的引擎"被调到 → 整段重处理"即可。
    // 返回语义同 voice SynthesizeNext：纯 Task（无 outcome）——取消是正常调度结局，经 cancellation 请求、
    // 不抛 OperationCanceledException、正常返回（不可中止的引擎把这段跑完才返回）；await 真正返回 = 槽位释放；
    // 错误抛异常，宿主在调用边界 catch → 该段 passthrough 降级。
    // 产物经 context.CreateAudioSegment 写出并 Commit。
    Task Process(CancellationToken cancellation = default);

    // 本段的参数回显曲线（按轨 id 键、与音频产物同一秒时间系）：key 与 IEffectSynthesisEngine.GetSynthesizedParameterConfigs
    // 对齐，仅承载曲线数据本身（轨形态/色由 config 给）；每条为分段折线富类型 SynthesizedParameter。
    // 会话是「effect × 单段」粒度，故本属性只覆盖本段；宿主把同一 effect 各段的回显按 key 拼接呈现。
    // 发布 = 换引用（发布的集合即不可变、宿主跨线程只读）。无回显的引擎返回空 map 即可。
    IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; }

    // 回显有更新（与 voice 会话 SynthesizedParametersChanged 对称；信号分离：进度声称走 StatusChanged、
    // 不惊动回显重读）：允许任意线程触发（长耗时引擎可在 Process 中途渐进发布回显、即时可见），
    // 宿主 marshal 回数据线程重读并刷新。仅在 Process 收尾发布的引擎可不触发——宿主在 Process
    // 归位后本就重聚合一次（兜底时机）；中途/收尾之外的更新则必须触发，否则宿主无从知晓。
    IActionEvent SynthesizedParametersChanged { get; }

    // 本会话的状态声称时间线（与 voice 会话 GetStatus 同一词汇；范围 = 全局秒、主语 = 本会话
    // 自己的产物——不受输入几何约束，加尾类引擎照实报自己输出的范围）。宿主把它作为状态带的
    // 声称层绘制：Synthesizing 段经 Progress 字段报进度；Synthesized 段是"声称完成"（呈现为
    // 非最终的软色，最终绿只来自链尾音频事实）。不报状态的引擎返回空列表——宿主按调度事实
    // 兜底呈现（处理中 = 输入范围整段合成中、无进度）。
    // 线程契约：返回已发布的不可变列表（换引用发布）；宿主在数据线程调用。
    IReadOnlyList<SynthesisStatusSegment> GetStatus();

    // 状态声称有更新：允许任意线程触发（worker 推理中就地报），宿主 marshal 回数据线程再拉 GetStatus。
    // 高频触发无害（重绘幂等自节流），但引擎宜自持粒度（如每整数百分比一报）。
    IActionEvent StatusChanged { get; }
}
