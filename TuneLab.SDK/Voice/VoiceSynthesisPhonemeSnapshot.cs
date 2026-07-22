using TuneLab.Foundation;

namespace TuneLab.SDK;

// 合成快照里一个钉死音素的冻结表项（VoiceSynthesisNoteSnapshot 两列表的元素）：音素几何（与 SynthesizedPhoneme
// 同形：Symbol/Duration/StretchWeight，平铺；引导 / 主体归属由所属列表给、不落每音素）+ 用户在该音素上设的引擎自定义属性值快照。与 VoiceSynthesisNoteSnapshot
// 同为数据线程物化、worker 只读的不可变值体（自包含、无 live 引用）。
//
// 几何字段平铺（引擎直读 ph.Symbol 等；要喂 PhonemeLayout.Resolve 时按字段重建 SynthesizedPhoneme 即可）。
// Properties 是该音素经 IVoiceSynthesisEngine.GetPhonemePropertyConfigs 声明的属性的冻结值（未声明 / 未设 =
// PropertyObject.Empty）。属性只存在于钉死音素上（用户数据）——引擎 G2P 的自动音素无此项。
//
// 注意：`default(VoiceSynthesisPhonemeSnapshot)` / `new […][n]` 不经构造器，Symbol / Properties 会是 null
// （违背非空声明）——**default 非有效实例**。本类型仅由宿主快照工厂构造、逐个填满后交引擎只读，无外部可达的
// default 路径，故不加防御 getter；若将来变得可达，按同款 backing 演进路加 `?? ""` / `?? PropertyObject.Empty`。
public readonly struct VoiceSynthesisPhonemeSnapshot
{
    public string Symbol { get; }
    public double Duration { get; }
    public double StretchWeight { get; }
    public PropertyObject Properties { get; }

    public VoiceSynthesisPhonemeSnapshot(string symbol, double duration, double stretchWeight, PropertyObject properties)
    {
        Symbol = symbol;
        Duration = duration;
        StretchWeight = stretchWeight;
        Properties = properties;
    }
}
