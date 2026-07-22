using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// note 的不可变快照值（worker 线程、将来 worker 进程只读）：边界为全局秒、Properties 值拷。
// 触底到值类型：不含任何 live 数据对象引用（Phonemes 也须是物化时新建的副本），自包含可
// 序列化值树，为跨进程演进留路。
// 邻居导航不进本面（接口最小化）：VoiceSynthesisSnapshot.Notes 即有序列表（与 GetSnapshot
// 递入的 notes 索引对齐），协同发音按索引取邻居。
// 形态 = 无参构造 + required init 属性：初始化后不可变（只写一次纪律），将来加字段
// 纯加性（新字段带默认值、不标 required，不破构造签名）。
public sealed class VoiceSynthesisNoteSnapshot
{
    // 归属身份（宿主发号的会话内运行期不透明 token）：与活视图 IVoiceSynthesisNote.Id 同值，worker 只从快照读它
    // 当 SynthesizedPhonemes 键（零活引用）。加性字段（非 required、宿主恒设、默认空串占位）。
    public string Id { get; init; } = string.Empty;

    public required double StartTime { get; init; }
    // EndTime = 有效末（宿主去重叠后盖前钳到下一 note 起点，单声部音频口径，可重叠的尾巴已截）。
    // 宿主独占音素布局（定位 / 跨 note 压缩 / melisma），插件只该见有效末——不再暴露 note 满末。
    public required double EndTime { get; init; }
    public required int Pitch { get; init; }
    public required string Lyric { get; init; }
    // 延音身份不在快照面：判定权完整归插件（IVoiceSynthesisSession.IsContinuation，判定域是 live 数据）。
    // 快照窗口可能裁掉链头，快照域自判会与 live 判定分叉——需要把身份带进 worker 的实现，应在
    // SynthesizeNext 的同步前缀对 live note 调用自己的判定、随自有快照结构一并冻结。
    // 钉死音素的冻结表项（几何描述符 + per-phoneme 属性值快照），结构化双列表：引导（核前前置辅音）/ 主体（核 + 尾辅音）。
    // 非钉死（引擎 G2P）note 两列表皆空。分类即列表成员（抗抖、跨拍可显式归属）。
    public required IReadOnlyList<VoiceSynthesisPhonemeSnapshot> LeadingPhonemes { get; init; }
    public required IReadOnlyList<VoiceSynthesisPhonemeSnapshot> BodyPhonemes { get; init; }
    // 主体起点（= 两列表结合线）相对 note 头的有符号偏移：junction = noteStart + BodyOffset（左负右正）。
    // 加性字段（非 required、默认 0）：无钉死音素或元音起手时 = 0。
    public double BodyOffset { get; init; }
    public required PropertyObject Properties { get; init; }
}
