using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base;
using TuneLab.SDK.Base.ControllerConfigs;

namespace TuneLab.SDK.Voice;

// 一次 part 合成的有状态会话：声明 + 调度 + 产物 + 状态全在此。
// 厚插件原则：分片、调度状态、音频缓冲、合成进度、失效（dirty）判定全由插件托管——
// 失效依赖图（如 音素时长→音高→音频 的分级管线）只有引擎知道，宿主无从复制；
// 宿主只推变更流（经 ISynthesisContext）、驱动调度、读产物展示。
//
// 生命周期：绑定一个 part，活到 part 被删除（Dispose）；换声源时宿主丢弃旧会话、
// 重建新会话（context 随会话重建）。会话是轻量句柄，重模型加载是懒的。
public interface ISynthesisSession : IDisposable
{
    // —— 声明（该声源暴露什么；Name/Description 等元数据由 IVoiceEngine.VoiceInfos 提供，不重复）——
    string DefaultLyric { get; }
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }
    IReadOnlyOrderedMap<string, PiecewiseAutomationConfig> PiecewiseAutomationConfigs { get; }
    IReadOnlyOrderedMap<string, IControllerConfig> PartProperties { get; }
    IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties { get; }

    // 条件属性面板：宿主在属性 commit 时按当前值重算面板。默认回退到上面的静态声明（忽略 context）——
    // 想做"随其他字段值动态改变控件/字段"的插件覆写这两个方法，返回当前 context 下应呈现的 ObjectConfig。
    // 须为纯函数（同输入同输出、无副作用、轻量）：宿主在每次值 commit 时调用并 keyed-diff 到控件树。
    ObjectConfig GetPartConfig(IPropertyContext context) => new() { Properties = PartProperties };
    ObjectConfig GetNoteConfig(IPropertyContext context) => new() { Properties = NoteProperties };

    // —— 调度（宿主驱动逐步合成：插件只在被调用时干活，干完即停等下一次）——
    // 一个会话同时只合成一块；并行发生在不同 part 的不同会话之间，并发上限由宿主账本式管控。

    // peek：窗内"下一块待合成"，无副作用；只在会话空闲时被问，数据线程上廉价执行
    // （live 全量访问、基于完整 part 做分片决策，只记范围/引用不深拷）。null = 窗内无待合成。
    // 窗口与返回边界同为秒（与产物同一时间系）。
    ISynthesisSegment? GetNextSegment(double startTime, double endTime);

    // commit：合成宿主选定的这一段。宿主已按 segment 的捕获声明物化 snapshot，与 peek 在
    // 同一调度 tick 内同步完成；插件在 worker 上对快照合成（含沿快照链的邻居导航）。
    // await 返回 = 槽位释放、宿主重排。返回纯 Task、无 outcome——真完成/被取消/失败都一样返回
    // （取消不抛 OperationCanceledException：取消是正常调度结局），错误经 GetStatus 看、
    // 是否还有待合成经 GetNextSegment 看。取消是尽力请求：不可中止的插件把这块跑完才返回，
    // 槽位在 await 真正返回时才释放、不在请求取消时——资源始终封顶在并发上限内。
    // progress 用 IProgress（Progress<T> 自带 SynchronizationContext marshal）。
    Task SynthesizeNext(ISynthesisSegment segment, ISynthesisSnapshot snapshot,
                        IProgress<double>? progress = null,
                        CancellationToken cancellation = default);

    // —— 音频产物（插件 native 采样率域）——
    // 工程采样率是唯一真值（TuneLabContext.Global）；插件优先按此率产音频并在此暴露实际输出率，
    // 宿主比对：相等直读、不等时套一层流式重采样——重采样集中宿主一处，会话与工程率变化解耦。
    int SampleRate { get; }
    double StartTime { get; }
    int SampleCount { get; }
    void ReadAudio(int offset, int count, float[] dst);   // 宿主 pull-copy

    // —— 曲线类产物 ——
    IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch { get; }   // 分段
    IReadOnlyMap<string, IReadOnlyList<IReadOnlyList<Point>>> SynthesizedParameters { get; }
    IReadOnlyList<SynthesizedPhoneme> Phonemes { get; }

    // —— 状态 / 按段报错（统一时间线）——
    IReadOnlyList<SynthesisStatusSegment> GetStatus();
    // 单一刷新信号：产物/状态有更新，宿主收到直接刷新（区域信息看 GetStatus）。
    // 出方向事件允许任意线程触发，宿主负责 marshal。
    event Action? StatusChanged;
}
