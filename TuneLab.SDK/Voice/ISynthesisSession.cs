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
    // 接口面只保留函数式获取（静态声明是插件实现内部的事：返回缓存 map 即一行）。
    string DefaultLyric { get; }

    // 自动化轨配置（连续 / 分段）：与 GetPartPropertyConfig 同为当前 part 参数值的纯函数——宿主在 part 参数
    // commit 时按当前值重算轨集合并 diff 到 UI，故轨集合可随参数显隐（如某模式开关才暴露的轨）。
    // 须为纯函数（同输入同输出、无副作用、轻量）；静态轨集合的插件忽略 context 返回固定 map 即可。
    // 轨是 part 级（与 GetPartPropertyConfig 同用 part context，非 note 级）。
    // 孤儿数据：轨从声明消失后宿主保留其已画曲线（隐藏不删），参数回退使该轨复现即原样恢复。
    // 连续轨与分段轨同在此 map（由 AutomationConfig.DefaultValue 是否 NaN 区分形态），按声明序呈现。
    IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IPartPropertyContext context);

    // 合成参数回显轨声明（part 级，context 驱动、纯函数，与 GetAutomationConfigs 同语义）：
    // 引擎产出的只读回显曲线（如 energy）暴露为一等只读轨，自带 DisplayText/Min/Max/Color——宿主据此
    // 显隐、用各自色绘制，不可编辑。回显是分段形（DefaultValue 置 NaN，无基线、段间断开）。
    // context 驱动 ⇒ 合成前 key 即存在，轨可预声明、显隐不抖。曲线数据另经 SynthesizedParameters 按同一批 key 承载。
    IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IPartPropertyContext context);

    // 条件属性面板：宿主在属性 commit 时按当前值重算面板（面板 = f(当前值)，context 即当前值快照）。
    // 须为纯函数（同输入同输出、无副作用、轻量）：宿主在每次值 commit 时调用并 keyed-diff 到控件树。
    // 静态面板的插件忽略 context 返回固定 ObjectConfig 即可。
    // GetPartPropertyConfig = part（会话主体）级，只依赖 part 自身值；GetNotePropertyConfig = note 级，依赖 part + note。
    ObjectConfig GetPartPropertyConfig(IPartPropertyContext context);
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
    // 进度不在此传入——经 GetStatus 状态带（SynthesisStatusSegment.Progress）+ StatusChanged 上报；
    // 将来如需独立推送通道再加性补成员。
    Task SynthesizeNext(SynthesisSegment segment,
                        CancellationToken cancellation = default);

    // —— 音频产物（插件 native 采样率域）——
    // 音频本体经 IAudioSegment 握柄交付：插件向 ISynthesisContext.CreateAudioSegment 申请段时传入该段的
    // native 采样率（宿主据此解释、与工程率比对：相等直读、不等套一层流式重采样，集中宿主一处）。采样率随段走、
    // 不在会话级声明——插件完全可按用户选择逐段用不同率（如提供合成采样率下拉）。
    // 时间对齐协议：全局 0 时刻 = 采样点 0；覆盖区域的权威信息是各音频段（未交付区域即静音）。

    // —— 曲线类产物 ——
    // 线程契约（三者同）：由插件在数据线程发布（合成完在 marshal 回数据线程的续延里换引用）；发布的集合即不可变，
    // 宿主可跨线程只读；每次更新经 StatusChanged 单一信号通知，宿主收到即重读重绘。接口不强制不可变性，插件自保。
    IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch { get; }   // 分段
    // 回显曲线数据（按轨 id 键、与音频/音高同一秒时间系）：key 与 GetSynthesizedParameterConfigs 对齐，
    // 仅承载曲线数据本身（轨形态/色由 config 给）；每条为具名富类型 SynthesizedParameter（分段折线）。
    IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; }
    IReadOnlyList<SynthesizedPhoneme> Phonemes { get; }

    // —— 状态 / 按段报错（统一时间线）——
    IReadOnlyList<SynthesisStatusSegment> GetStatus();
    // 单一刷新信号：产物/状态有更新，宿主收到直接刷新（区域信息看 GetStatus）。
    // 出方向事件允许任意线程触发，宿主负责 marshal。
    event Action? StatusChanged;
}
