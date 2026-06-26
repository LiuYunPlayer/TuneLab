namespace TuneLab.SDK;

// 音素描述符（方向无关，输入 / 输出共用一个类型）：只报「标称时长 + 弹性权重 + 前置标记」，**不报绝对位置**。
// 进（用户钉死约束，挂 IVoiceSynthesisNote.Phonemes）与出（引擎合成产物，IVoiceSynthesisSession.SynthesizedPhonemes）同形——
// 描述符方向无关且稳定，故合并；持久层 PhonemeInfo 与本类型解耦（独立演进，见 Format/DataInfo/PhonemeInfo）。
//
// 时长模型：引擎只报标称时长，定位 / 跨 note 去重叠压缩 / melisma 铺设全由宿主按同一时长模型派生
// （核起点=音符头、前置往左累积、核填到满末）。引擎报已压缩的绝对位置会让宿主布局误判（相接判据失真），
// 故只报自然时长、宿主独占布局。引擎自己的音频内部如何摆放是引擎的事，与此显示契约解耦。
//
// 把标称时长解析为真实时序的布局算法在 SDK（PhonemeLayout.Resolve，纯函数）：想与宿主显示完全一致
// （WYSIWYG）的引擎调它即可——冻结的只是 I/O 形状，压缩内部逻辑仍可宿主侧演进，引擎运行时绑定宿主的这一份故永不漂移。
// 想自管音频摆放（交叉淡入等）的引擎可不调，仍按本描述符自由放置、错位非致命。
//
// 粒度为整 note：IVoiceSynthesisNote.Phonemes 列表非空 = 全部音素用户钉死（引擎遵守约束）；为空 = 引擎从 Lyric 做 G2P + 全自由定时。
public struct SynthesizedPhoneme
{
    public string Symbol;

    // 标称时长（秒）：辅音(StretchWeight=0)为其固定长；核 / 元音(StretchWeight>0)的此值被布局忽略（恒按填充派生）。
    public double Duration;

    // 伸缩权重：0 = 刚性辅音（长度固定）；>0 = 可伸核 / 元音（吸收 note 伸缩 / 压缩，按权重分摊、先让）。
    // 用户锁定音素时随时长一并固定为用户数据。Σw ≤ 0（含未设、默认零）时宿主退化为均匀缩放，无除零。
    public double StretchWeight;

    // 是否前置音素（音节核之前的引导辅音）：决定摆放（前置从核起点往左累积、核填充）。引擎按音韵学标注；缺省 false。
    public bool IsLead;

    public override readonly string ToString()
    {
        return $"{{{Symbol}: {Duration}s{(IsLead ? " lead" : "")} w={StretchWeight}}}";
    }
}
