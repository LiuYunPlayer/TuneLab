using TuneLab.Foundation;

namespace TuneLab.SDK;

// 会话级输入活视图：宿主实现，每次 CreateSession 新建、随会话死。
// 插件订阅它而非宿主长寿数据层——中间层短命使泄漏结构性不可能（无需弱事件/退订契约），
// 且宿主始终握着线程/时机/故障隔离/批量四个旋钮（在 command 提交后、数据线程、try-catch 包裹下 emit）。
//
// 线程纪律：事件恒在数据线程触发与处理，handler 只做廉价记录/标脏；合成永不碰活视图，
// 只读派发时物化的 VoiceSnapshot。
//
// 通知语义：只转发已提交的真实变更（拖拽等中间态被业务层 merge 折叠掉，订阅者眼中状态
// 从编辑前直达收口后）。变更定位的三种最小事实——字段变了（note 可订阅属性，配合
// WillModify/Modified 拿旧/新值）、区间变了（ISynthesisAutomation.RangeModified 带秒范围）、
// 集合变了（Notes 增删）。这些事实映射到哪些段、重合成到管线哪一级（失效依赖图）归插件；
// 机制粒度支撑最精细策略，也允许"任何通知 → 全部标脏"的懒实现。
//
// 坐标系约定（SDK 面）：插件侧时间量一律为全局秒（与音频产物、状态段同一时间系）；tick 仅
// 是宿主乐谱内部表示、不外露。时基变更（tempo 表 / part 平移）无独立信号也无增量分解通知——
// 宿主直接**整体重建会话**（旧会话 Dispose、新 context 新会话），新会话读到的即新秒值。
// 插件无需处理"时基变了"：正确实现 Dispose（退订 + 释放音频段）即天然正确。
public interface IVoiceSynthesisContext
{
    // 选定声库（= IVoiceSynthesisEngine.VoiceSourceInfos 的 key）：context 生命内**不可变**（换声库 = 宿主重建 context + 会话），
    // 故烘入 context、CreateSession 不再单列 voiceId。会话面本就是会话级生命周期，带身份不引入额外耦合。
    string VoiceId { get; }
    // 所属 part 的运行期身份（宿主发号的不透明 token、不持久）：part 对象存活期间恒定、宿主进程内唯一——跨会话重建
    //（换声库 / 时基变更）不变。与声明面 IVoiceSynthesisPartView.PartId 同一个号：插件据此把会话里算出的 part 级
    // 派生状态（如后台分析结果）与声明求值时拿到的 part 对上（见 InvalidatePart）。
    string PartId { get; }
    // 链表形态（无索引承诺，宿主数据层即双向链表）：顺序消费用枚举、头尾 O(1) 走
    // First/Last、邻居导航走 note.Next/Previous；支持 WhenAny（成员增删自动接线）。
    //
    // 排序契约（全序、确定性）：StartTime 升序 → 同起点 EndTime 降序（长 note 在前）→
    // 再同则保持宿主插入序。note 可重叠（和弦）——序列直传原始可重叠 note，"后盖前"等
    // 去重叠是插件自己的责任（单声部插件按需截断，和弦插件原味消费重叠）。
    IReadOnlyNotifiableLinkedList<IVoiceSynthesisNote> Notes { get; }
    IReadOnlyNotifiablePropertyObject PartProperties { get; }

    // 已声明 automation 轨（引擎经 GetAutomationConfigs 声明的，按 key）：只读 map，可枚举可点取
    // （插件不必重跑声明逻辑去探有哪些轨；跨进程也由宿主一次枚举物化送达，免反复回调）。与 Pitch/
    // PitchDeviation 两条固定通道并列、互不相属。轨集随声明动态变（config 是当前值的纯函数）——取用前
    // 重读本属性即当前集；订阅各轨 ISynthesisAutomation.RangeModified 收区间失效。
    IReadOnlyMap<string, ISynthesisAutomation> Automations { get; }

    // 音高的两个平行通道（绝对约束 + 相对偏差）：
    // Pitch = 用户钉死的绝对音高曲线（分段型：有值=钉死、NaN=插件自由发挥）；
    // PitchDeviation = 加性偏差（连续型：处处有值、默认 0、永不 NaN；宿主侧 vibrato 等偏差源都汇于此）。
    // 合成契约：finalPitch(t) = resolve(Pitch(t)) + PitchDeviation(t)——插件先解析绝对面
    // （钉死区用用户值、自由区自己生成），再叠加偏差；偏差因此也作用于未绘制区域。
    ISynthesisAutomation Pitch { get; }
    ISynthesisAutomation PitchDeviation { get; }

    // 物化合成快照（插件主动拉取）：notes = 本次合成需要的 note（段内 + 协同发音邻居，插件自由圈定，
    // 返回的 snapshot.Notes 与之索引对齐）。仅数据线程、仅 SynthesizeNext 的同步前缀（offload 之前）调用。
    // automation / pitch 一律**全量冻结、不开窗**：真实采样范围依赖音素时长，而时长在 offload 后的合成阶段
    // 才知，同步前缀无从正确圈窗（圈窄了则 padding / 前置辅音的采样点落在窗外取到错值）；全量冻的是原始
    // 控制点（锚点，廉价——4 分钟稠密 pitch 约几十万字节、亚毫秒，远小于一次推理），worker 按任意查询点
    // 插值、越界端与活曲线一致地钳夹。物化 / 版本缓存 / 记账留在宿主实现内。
    VoiceSynthesisSnapshot GetSnapshot(IReadOnlyList<IVoiceSynthesisNote> notes);

    // 音频产物的宿主分配工厂：插件合成产出音频时申请一个段握柄，写入、Commit() 标完成，
    // 重分片（或改长度/位置）时 Dispose() 释放重建。宿主据此持有段登记表、驱动下游 effect 链按段
    // 重渲染（段是音频承载 + effect 失效单元，与 IVoiceSynthesisSession.GetStatus 的 UI 状态段解耦，两套分区可不同）。
    // 仅数据线程调用；sampleOffset = 全局起始采样位置（native 率，全局 0 时刻 = 采样点 0），sampleCount = 段长（采样数），
    // sampleRate = 该段 native 采样率（插件传入，宿主据此解释/重采样；可逐段不同）。
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate);

    // 逻辑编辑收口信号：每个逻辑编辑（一个 command，含单条编辑）的全部变更通知发完后触发一次
    //（单条编辑也补发——故插件无需区分"在不在批量中"）。它不是宿主缓冲，而是让插件延迟昂贵状态修正的
    // 收口点——每条变更通知里廉价记录/标脏，Committed 一次性做重活（如重分片）；批量编辑（移调几百个 note）
    // 因此只重分片一次。出方向事件，宿主在数据线程触发。
    IActionEvent Committed { get; }

    // —— 声明失效（插件 → 宿主）——
    // 插件宣告它此前对本 part / 这些 note 给出的声明（IVoiceSynthesisEngine 的 Get*Config 族）已过期，请宿主重新询问。
    // 用于声明依赖插件自有派生状态的场景（如后台歌词解析、声调分析：解析完成后面板才出现某些选项）——派生状态
    // 变化后必须调用，否则宿主不知道该重新询问，界面停在旧答案上。
    //
    // · 只作废插件自己的回答：不改动、也不宣告任何用户数据变化；不使任何区段待重合成（合成失效归插件自管）。
    // · InvalidatePart：part 作用域全部声明（part 属性面板、自动化轨集合、回显轨集合），并向下级联到 note / 音素声明
    //  （note 声明本就以 part 为上下文）——调它就够，不必再逐个报 note。
    // · InvalidateNotes：这些 note 的 note / 音素声明；不向上级联——若 note 派生状态也影响 part 声明，另调 InvalidatePart。
    //   noteId = IVoiceSynthesisNote.Id（= 声明面 IVoiceSynthesisNoteView.Id）；未知 / 已删除的 id 静默忽略。
    // · 不含 IsContinuation：延音判定必须由当前数据同步决定（见其契约），本信号不使宿主的延音缓存失效。
    // · 派生状态只能是用户数据（歌词 / 音高 / 时间 / 属性值）的函数，不得依赖声明的输出，否则成环。
    // · 任意线程可调、可高频调用：宿主 marshal 到数据线程并合拍处理，调用方无需自行节流；会话销毁后的调用被忽略。
    void InvalidatePart();
    void InvalidateNotes(IEnumerable<string> noteIds);
}
