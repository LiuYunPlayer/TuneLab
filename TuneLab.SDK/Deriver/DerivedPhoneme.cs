namespace TuneLab.SDK;

// 派生音素：取 DataInfo PhonemeInfo 之形的独立类型（同构、不共类型——不引用 DataInfo，purpose-built 产物族）。
// 只留派生得出的自然属性（符号 / 时长 / 弹性权重）；不带 per-phoneme 自定义 Properties——那是可编辑创作面，
// deriver 不产（同 DerivedNote/DerivedPart 均不带创作字段）。落成真实 MidiPart 时宿主转 PhonemeInfo、创作字段默认填。
// 语义同 PhonemeInfo：辅音 StretchWeight=0（固定时长）、元音 >0（可伸）；位置由布局派生、不存。
public sealed class DerivedPhoneme
{
    public required string Symbol { get; init; }
    // 标称时长（秒）。
    public required double Duration { get; init; }
    // 弹性伸缩权重。0 = 刚性（辅音）；>0 = 可伸（元音）。
    public double StretchWeight { get; init; }
}
