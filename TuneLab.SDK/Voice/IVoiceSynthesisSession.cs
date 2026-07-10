using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.SDK;

// 一次 part 合成的有状态会话：声明 + 调度 + 产物 + 状态全在此。
// 厚插件原则：分片、调度状态、音频缓冲、合成进度、失效（dirty）判定全由插件托管——
// 失效依赖图（如 音素时长→音高→音频 的分级管线）只有引擎知道，宿主无从复制；
// 宿主只推变更流（经 IVoiceContext）、驱动调度、读产物展示。
//
// 生命周期：绑定一个 part，活到 part 被删除（Dispose）；换声源时宿主丢弃旧会话、
// 重建新会话（context 随会话重建）。会话是轻量句柄，重模型加载是懒的。
public interface IVoiceSynthesisSession : IDisposable
{
    // —— 运行时取值（会话创建后才被消费；声明类 config 已上移到 IVoiceEngine，不在此重复）——
    // 默认歌词：创建 note 时取用，是会话级运行时取值（按需读，非构造前声明），故留在实例上。
    string DefaultLyric { get; }

    // 延音判定（完整判定，实现所有）：note 是否起延音作用——与前方 note 共享布局空间的乘客。
    // 记号学与链结构（相接 / 孤儿 / 断链、乃至跨空隙语义）全归实现，宿主不叠加自己的判据、
    // 照单消费（显示布局 / 编辑手势同源）。上下文经 note.Next / Last 自行导航
    // （歌词 / 位置 / 钉死音素均可读）。挂会话而非引擎声明层：判定可用会话已加载资源（如声库词典）。
    //
    // 契约约束：
    //   · 数据线程同步调用、不留存 note 引用；
    //   · 观测确定性：相同当前数据 → 相同答案（内部 memoize 自便）；
    //   · 禁依赖合成进度 / 产物——判定不得随"合成过没有"改变（显示骨架在合成前后恒等的根基）。
    //
    // 判定即唯一通道，宿主照单全收：返回 true 的 note 在显示上透明（前 note 元音铺过）——**即使它
    // 带钉死音素**。钉死与歌词/位置同级、都是喂给本判定的用户数据输入，其在延续 note 上的语义归你
    // 解释（宿主不猜用户意图、不叠加任何消解——用户使用你的引擎即遵守你的规矩）。
    //
    // 绑定性：本判定对实现自己的合成有约束力——判定为延续的 note 不得在合成产物中携带归属自己的
    // 音素（区段发音全部挂链头 note），违反视为协议 bug。宿主的兜底是**忽略**：判定优先级最高——
    // 音素布局的第一步就是延音判定，判定为延续的 note 其音素数据根本不被读取，你违约回传的音素
    // 落账但不显示；你的音频若含实际唱出的违约内容，与显示的分叉是你自身矛盾的如实代价。
    //
    // **刻意无默认实现**：判定与你的合成行为是一对绑定承诺，任何默认体都会替你的合成许诺它未必
    // 实现的语义（"-" 铺末默认对不做 melisma 的引擎撒谎、恒 false 默认掩盖做了 melisma 却忘实现
    // 本方法的引擎）——沉默继承即静默分叉，故本方法强制你显式表态。不做延音语义的引擎如实
    // `=> false`（每个 note 都是内容）即可。
    //
    // 参考语义（编辑器 "-" 录入约定的对应判定，宿主对无声源 part 也按此显示；样例插件 V1.Voice
    // 有完整实现）：歌词 "-" ∧ 经不断裂相接链回溯到内容 note（相接 = 前末 ≥ 后起，严格比较无容差
    // ——边界同源 tick 换算，相接即精确相等；空隙断链 / 链头缺失 → 孤儿 false）∧ 本 note 无钉死
    // 音素（钉死即内容、退出乘客并成为合法链头）。链 / 相接 / 记号全是你的语义空间（如"小间距视为
    // 相接"的引擎策略），SDK 不提供判定积木——判定绑定合成行为，你必须完全自有其语义。
    bool IsContinuation(IVoiceSynthesisNote note);

    // —— 调度（宿主驱动逐步合成：插件只在被调用时干活，干完即停等下一次）——
    // 一个会话同时只合成一块；并行发生在不同 part 的不同会话之间，并发上限由宿主账本式管控。

    // peek：窗内"下一块待合成"的纯值边界，无副作用；只在会话空闲时被问，数据线程上廉价执行
    // （live 全量访问、基于完整 part 做分片决策）。null = 窗内无待合成。
    // 窗口与返回边界同为秒（与产物同一时间系）。返回纯调度提示 SynthesisRange。
    SynthesisRange? GetNextSegment(double startTime, double endTime);

    // commit：合成宿主选中的这一块。入参是与选中它的那次 peek【完全相同】的窗口（秒）——而非把
    // GetNextSegment 自报的 SynthesisRange 原样回灌：插件按同一窗口确定性重导出 notelist
    // （确定性分片 + 数据未变 ⇒ 与 peek 同结果；或用 peek 时自缓存的分块）。与 peek 在同一调度 tick
    // 内同步衔接（期间无编辑可插入），插件在同步前缀经 IVoiceSynthesisContext.GetSnapshot 拉取本次合成所需
    // 快照，之后才 offload——worker 只读快照。
    // await 返回 = 槽位释放、宿主重排。返回纯 Task、无 outcome——真完成/被取消/失败都一样返回
    // （取消不抛 OperationCanceledException：取消是正常调度结局），错误经 GetStatus 看、
    // 是否还有待合成经 GetNextSegment 看。取消是尽力请求：不可中止的插件把这块跑完才返回，
    // 槽位在 await 真正返回时才释放、不在请求取消时——资源始终封顶在并发上限内。
    // 进度不在此传入——经 GetStatus 状态带（SynthesisStatusSegment.Progress）+ StatusChanged 上报；
    // 将来如需独立推送通道再加性补成员。
    Task SynthesizeNext(double startTime, double endTime,
                        CancellationToken cancellation = default);

    // —— 音频产物（插件 native 采样率域）——
    // 音频本体经 IAudioSegment 握柄交付：插件向 IVoiceSynthesisContext.CreateAudioSegment 申请段时传入该段的
    // native 采样率（宿主据此解释、与工程率比对：相等直读、不等套一层流式重采样，集中宿主一处）。采样率随段走、
    // 不在会话级声明——插件完全可按用户选择逐段用不同率（如提供合成采样率下拉）。
    // 时间对齐协议：全局 0 时刻 = 采样点 0；覆盖区域的权威信息是各音频段（未交付区域即静音）。

    // —— 曲线类 / 离散产物 ——
    // 线程契约（全部产物同）：由插件在数据线程发布（合成完在 marshal 回数据线程的续延里换引用）；发布的集合即不可变，
    // 宿主可跨线程只读；每种产物各有专属更新信号（见下），宿主收到即重读对应产物。接口不强制不可变性，插件自保。
    // 合成音高（具名富类型，分段折线）：固定专属通道，与通用 keyed 参数解耦各自演进。
    SynthesizedPitch SynthesizedPitch { get; }
    // 回显曲线数据（按轨 id 键、与音频/音高同一秒时间系）：key 与 GetSynthesizedParameterConfigs 对齐，
    // 仅承载曲线数据本身（轨形态/色由 config 给）；每条为具名富类型 SynthesizedParameter（分段折线）。
    IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; }
    // 合成音素（按归属 note 键，每 note 一个 SynthesizedSyllable = 音素序列 + 前置量 Preutterance，只报标称几何——
    // 定位 / 去重叠归宿主）。引擎自行托管失效——脏 / 合成中的块不应在此报告其 note 的音素（宿主据此留白）。时长模型下
    // 无主音素无锚不可定位、故无「无主音素」契约（breath 等将来用「归属 note 的前置 / 后置音素」或专属事件通道承载）。
    IReadOnlyMap<IVoiceSynthesisNote, SynthesizedSyllable> SynthesizedPhonemes { get; }

    // —— 状态 / 按段报错（统一时间线）——
    IReadOnlyList<SynthesisStatusSegment> GetStatus();

    // —— 更新信号（按产物分离）——
    // 一种产物一个信号：宿主对不同产物要做的事不同（音素回填到 note / 参数轨重绘 / 音高曲线重绘 / 状态带重绘），
    // 分离后宿主只刷新变化的那部分；高频的状态/进度（StatusChanged，逐 tick）不再带动产物重读。
    // 只在对应产物**真正变化**时 fire（产物没变就别 fire）。音频产物不在此列——经 IAudioSegment.Commit 自有通道驱动。
    // 出方向事件允许任意线程触发，宿主负责 marshal。
    IActionEvent SynthesizedPhonemesChanged { get; }
    IActionEvent SynthesizedParametersChanged { get; }
    IActionEvent SynthesizedPitchChanged { get; }
    // 状态 / 进度（GetStatus）变化——通常最高频（进度条逐 tick），不触及产物。
    IActionEvent StatusChanged { get; }
}
