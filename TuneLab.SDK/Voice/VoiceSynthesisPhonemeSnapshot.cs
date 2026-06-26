using TuneLab.Foundation;

namespace TuneLab.SDK;

// 合成快照里一个钉死音素的冻结表项（VoiceSynthesisNoteSnapshot.Phonemes 的元素）：音素几何（与 SynthesizedPhoneme
// 同形：Symbol/Duration/StretchWeight/IsLead，平铺）+ 用户在该音素上设的引擎自定义属性值快照。与 VoiceSynthesisNoteSnapshot
// 同为数据线程物化、worker 只读的不可变值体（自包含、无 live 引用）。
//
// 几何字段平铺（引擎直读 ph.Symbol 等；要喂 PhonemeLayout.Resolve 时按字段重建 SynthesizedPhoneme 即可）。
// Properties 是该音素经 IVoiceSynthesisEngine.GetPhonemePropertyConfigs 声明的属性的冻结值（未声明 / 未设 =
// PropertyObject.Empty）。属性只存在于钉死音素上（用户数据）——引擎 G2P 的自动音素无此项。
public readonly struct VoiceSynthesisPhonemeSnapshot
{
    public string Symbol { get; }
    public double Duration { get; }
    public double StretchWeight { get; }
    public bool IsLead { get; }
    public PropertyObject Properties { get; }

    public VoiceSynthesisPhonemeSnapshot(string symbol, double duration, double stretchWeight, bool isLead, PropertyObject properties)
    {
        Symbol = symbol;
        Duration = duration;
        StretchWeight = stretchWeight;
        IsLead = isLead;
        Properties = properties;
    }
}
