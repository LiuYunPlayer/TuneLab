using TuneLab.Foundation;

namespace TuneLab.SDK;

// 会话活视图中的 note（可订阅，仅数据线程）。固定字段保持最小（通用乐理属性）；
// voice 专属 per-note 参数一律走 Properties（keyed）——加新参数 = 加会话声明 NoteProperties
// 的 key，不动本接口固定面。
//
// 时间量为全局秒（插件侧统一秒轴，与音频产物同系）：note 边界是 tempo 表派生的秒值。
// 时基变更（tempo 表 / part 平移）**不走增量通知**——宿主整体重建会话（含 context），新会话
// 读到新秒值；边界 Modified 只在 note 自身编辑时触发。tick 是宿主乐谱内部表示、不外露。
public interface IVoiceSynthesisNote
{
    // 会话内运行期稳定身份（宿主发号的不透明 token、不持久）：合成产物 SynthesizedPhonemes 按此键回指归属 note，
    // 免把活 note 引用引入值产物 / worker。不可变（一个会话内恒定，故非 notifiable）；镜像到
    // VoiceSynthesisNoteSnapshot.Id 供 worker 读。用 string 而非专用类型：将来若需给 note 持久 uuid，宿主把此值
    // 从会话计数器换成持久 uuid（Guid 本即 string）即可，SDK 面不变。当前语义 = 仅一次会话内稳定、唯一、可作 map 键。
    string Id { get; }

    IReadOnlyNotifiableProperty<double> StartTime { get; }
    IReadOnlyNotifiableProperty<double> EndTime { get; }
    IReadOnlyNotifiableProperty<int> Pitch { get; }
    IReadOnlyNotifiableProperty<string> Lyric { get; }
    // 钉死音素的结构化双列表（引导 = 核前前置辅音；主体 = 核 + 尾辅音），时间序。两者皆空 = 非钉死（引擎 G2P）。
    // 分类即列表成员（不从几何派生、抗抖）；订阅变化须同订两列表（无合并信号，见会话注释）。
    IReadOnlyNotifiableProperty<IReadOnlyList<SynthesizedPhoneme>> LeadingPhonemes { get; }
    IReadOnlyNotifiableProperty<IReadOnlyList<SynthesizedPhoneme>> BodyPhonemes { get; }
    // 主体起点（= 两列表结合线）相对 note 头的有符号偏移：junction = noteStart + BodyOffset（左负右正）。
    // 仅在钉死（有音素）时有意义；元音起手 / 无钉死时 = 0。定位由此 + 几何锚点派生（见 PhonemeLayout）。
    IReadOnlyNotifiableProperty<double> BodyOffset { get; }
    IReadOnlyNotifiablePropertyObject Properties { get; }

    // 延音身份不在本面：判定权完整归插件（IVoiceSynthesisSession.IsContinuation，宿主照单消费），
    // 回喂只会是实现自己的输出、纯冗余，故无此字段。实现以本面数据（Lyric / 位置 / Phonemes / 邻居导航）
    // 自行判定——SDK 刻意不提供判定实现（判定绑定合成行为，语义须实现完全自有，见会话方法注释）。

    // 邻居链（协同发音用）。活视图上仅供数据线程的分片决策 live 导航——事件 handler 内
    // 只有 note 自身引用、无列表索引上下文，O(1) 邻居导航是真实便利；
    // 合成须在快照（snapshot.Notes 有序列表按索引）上导航，不回活对象。
    IVoiceSynthesisNote? Next { get; }
    IVoiceSynthesisNote? Previous { get; }
}
