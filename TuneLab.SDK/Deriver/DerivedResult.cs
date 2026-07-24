namespace TuneLab.SDK;

// deriver 产物的根：一套 purpose-built 的「派生产物」表示（Derived*），形状由「分析能产出什么」决定，
// 刻意不与导入导出交换格式 DataInfo（*Info）同构——那套表达用户意图 / 可编辑工程状态（gain/pan/color/
// soundsource/effects/offsets/vibrato/properties…），本套是对音频自然属性的描述（测出的音符/音高/音素/速度）。
// 二者用途不同，强行同构既不成立（本套本就砍掉大半字段）也是过早耦合。将来若需导入外部秒基工程，另设计独立一套。
//
// 全程物理秒（deriver 输入是固定音频、无时间线）；宿主是 tick 网格的唯一主人，秒→tick 换算全在宿主侧。
// 空集合 = 「这项我不产」——deriver 只填自己专精的槽。用非空空默认（非可空）：null 与空在消费端等价、都是 no-op，
// 空默认免 NRE、更安全（与 tick DataInfo 家族一致）。
public sealed class DerivedResult
{
    // 多轨（如声部分离产多个 stem 轨）。每轨可含多个 part（如按静音段切分产轨内多个不重叠 part）。
    public IReadOnlyList<DerivedTrack> Tracks { get; init; } = [];
    // 工程级时间线（速度检测型才填）。
    public IReadOnlyList<DerivedTempo> Tempos { get; init; } = [];
    // 工程级时间线（拍号检测型才填）。
    public IReadOnlyList<DerivedTimeSignature> TimeSignatures { get; init; } = [];
}
