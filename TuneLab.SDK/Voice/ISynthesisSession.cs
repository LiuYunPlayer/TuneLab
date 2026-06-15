using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.SDK;

// 一次 part 合成的有状态会话：声明 + 调度 + 产物 + 状态全在此。
// 厚插件原则：分片、调度状态、音频缓冲、合成进度、失效（dirty）判定全由插件托管——
// 失效依赖图（如 音素时长→音高→音频 的分级管线）只有引擎知道，宿主无从复制；
// 宿主只推变更流（经 ISynthesisContext）、驱动调度、读产物展示。
//
// 生命周期：绑定一个 part，活到 part 被删除（Dispose）；换声源时宿主丢弃旧会话、
// 重建新会话（context 随会话重建）。会话是轻量句柄，重模型加载是懒的。
public interface ISynthesisSession : IDisposable
{
    // —— 声明（该声源暴露什么；Name/Description 等元数据由 IVoiceEngine.VoiceSourceInfos 提供，不重复）——
    // 接口面只保留函数式获取（静态声明是插件实现内部的事：返回缓存 map 即一行）；
    // 宿主在会话创建/重建后调用并缓存。运行中动态变化（轨集合变更通知 + 既有轨数据归宿）挂账缓后。
    string DefaultLyric { get; }
    IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs();
    IReadOnlyOrderedMap<string, PiecewiseAutomationConfig> GetPiecewiseAutomationConfigs();

    // 条件属性面板：宿主在属性 commit 时按当前值重算面板（面板 = f(当前值)，context 即当前值快照）。
    // 须为纯函数（同输入同输出、无副作用、轻量）：宿主在每次值 commit 时调用并 keyed-diff 到控件树。
    // 静态面板的插件忽略 context 返回固定 ObjectConfig 即可。
    // GetPropertyConfig = part（会话主体）级，只依赖 part 自身值；GetNotePropertyConfig = note 级，依赖 part + note。
    ObjectConfig GetPropertyConfig(IPartPropertyContext context);
    ObjectConfig GetNotePropertyConfig(INotePropertyContext context);

    // —— 调度（宿主驱动逐步合成：插件只在被调用时干活，干完即停等下一次）——
    // 一个会话同时只合成一块；并行发生在不同 part 的不同会话之间，并发上限由宿主账本式管控。

    // peek：窗内"下一块待合成"的纯值边界，无副作用；只在会话空闲时被问，数据线程上廉价执行
    // （live 全量访问、基于完整 part 做分片决策）。null = 窗内无待合成。
    // 窗口与返回边界同为秒（与产物同一时间系）。
    SynthesisSegment? GetNextSegment(double startTime, double endTime);

    // commit：合成 peek 报出的这一块。与 peek 在同一调度 tick 内同步衔接（期间无编辑可插入），
    // 插件在同步前缀重算分块（确定性分片 + 数据未变 ⇒ 与 peek 同结果）、经
    // ISynthesisContext.GetSnapshot 拉取本次合成所需快照，之后才 offload——worker 只读快照。
    // await 返回 = 槽位释放、宿主重排。返回纯 Task、无 outcome——真完成/被取消/失败都一样返回
    // （取消不抛 OperationCanceledException：取消是正常调度结局），错误经 GetStatus 看、
    // 是否还有待合成经 GetNextSegment 看。取消是尽力请求：不可中止的插件把这块跑完才返回，
    // 槽位在 await 真正返回时才释放、不在请求取消时——资源始终封顶在并发上限内。
    // progress 用 IProgress（Progress<T> 自带 SynchronizationContext marshal）。
    Task SynthesizeNext(SynthesisSegment segment,
                        IProgress<double>? progress = null,
                        CancellationToken cancellation = default);

    // —— 音频产物（插件 native 采样率域）——
    // 音频本体经 IAudioSegment 握柄交付：插件向 ISynthesisContext.CreateAudioSegment 申请段时传入该段的
    // native 采样率（宿主据此解释、与工程率比对：相等直读、不等套一层流式重采样，集中宿主一处）。采样率随段走、
    // 不在会话级声明——插件完全可按用户选择逐段用不同率（如提供合成采样率下拉）。
    // 时间对齐协议：全局 0 时刻 = 采样点 0；覆盖区域的权威信息是各音频段（未交付区域即静音）。

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
