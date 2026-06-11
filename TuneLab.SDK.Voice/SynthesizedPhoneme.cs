namespace TuneLab.SDK.Voice;

// 音素时序输出（engine→host）：扁平时间线 + 出身 note 引用，而非按 note 装字典——
// 音素时序会外溢（辅音常入侵上一个音符的尾巴），扁平 + 出身才能同时表达越界与换气。
public struct SynthesizedPhoneme
{
    public string Symbol;
    public double StartTime;     // 绝对秒（与音频产物同一时间系），可越界/重叠
    public double EndTime;

    // 出身 note（歌词归属），不是"压着谁"：后一个 note 的辅音入侵前一个的尾巴时，
    // Note = 后者、StartTime 落在前者范围内。换气等无主音素为 null。
    // 仅作身份 token 用（归属/定位），合成中不得读其属性。
    public ISynthesisNote? Note;

    // 伸缩权重：宿主拖伸 note 时用共享公式 new_dᵢ = dᵢ + Δ×(wᵢ/Σwⱼ)（再非负 clamp）
    // 就地算 preview、零引擎调用——辅音 w=0、元音 w=1 则长度变化全进元音；w = dᵢ 退化为均匀缩放。
    // preview 纯显示、绝不反馈给引擎当约束；权威时长由下次全量合成重新定时并覆盖。
    public double StretchWeight;

    public override string ToString()
    {
        return $"{{{Symbol}: [{StartTime}, {EndTime}]}}";
    }
}
