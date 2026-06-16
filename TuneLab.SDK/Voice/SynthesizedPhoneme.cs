namespace TuneLab.SDK;

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
    public ILiveNote? Note;

    // 伸缩权重：宿主拖伸 note 时用共享公式 new_dᵢ = dᵢ + Δ×(wᵢ/Σwⱼ)（再非负 clamp）
    // 分配长度变化——辅音 w=0、元音 w=1 则长度变化全进元音；w = dᵢ 退化为均匀缩放。
    // 用户锁定音素时，权重随时长一并从产物固定为用户数据（进工程），此后宿主缩放
    // pinned 音素始终有正确分布可用。
    // 消费端防御语义：Σw ≤ 0（含插件未设、struct 默认全零）时宿主退化为均匀缩放，无除零。
    public double StretchWeight;

    public override string ToString()
    {
        return $"{{{Symbol}: [{StartTime}, {EndTime}]}}";
    }
}
