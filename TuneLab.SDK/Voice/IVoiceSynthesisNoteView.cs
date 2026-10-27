using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// voice 声明面值视图族的一员（族的说明见 IVoiceSynthesisPartPropertyContext.cs）。
// voice part 数据 note 的只读值视图（声明面；EndTime 取原始满末，未钳）。
public interface IVoiceSynthesisNoteView
{
    // note 运行期身份（= IVoiceSynthesisNote.Id，同一个号）：插件据此查回会话为该 note 算的派生状态。
    string Id { get; }
    double StartTime { get; }              // 全局秒
    double EndTime { get; }                // 全局秒（原始满末，未钳）
    int Pitch { get; }
    string Lyric { get; }
    PropertyObject Properties { get; }     // per-note 属性值快照；多选 note 合并三态归插件
    double BodyOffset { get; }             // 主体起点（结合线）相对 note 头的有符号偏移（junction = noteStart + BodyOffset）
    // 钉死音素的结构化双列表（引导 / 主体），时间序。分类即列表成员。
    // 全序列 = LeadingPhonemes ++ BodyPhonemes（时间序）；刻意不设合并投影属性——派生量是消费侧私事，
    // 核相对 slot 坐标（音素属性声明 GetPhonemePropertyConfigs 的键）由 PhonemeSlots 从这两列表派生
    //（slot = 全序列下标 − LeadingPhonemes.Count），引擎与宿主共用同一口径。
    IReadOnlyList<IVoiceSynthesisPhonemeView> LeadingPhonemes { get; }
    IReadOnlyList<IVoiceSynthesisPhonemeView> BodyPhonemes { get; }
}
