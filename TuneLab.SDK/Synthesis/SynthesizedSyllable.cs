using System.Collections.Generic;

namespace TuneLab.SDK;

// 一个归属 note 的合成音节：分类做成引擎声明的结构化双列表（引导音素 + 主体音素），几何收进一个有符号 BodyOffset。
// 一 note ≈ 一音节，本类型即该音节的音素布局。IVoiceSynthesisSession.SynthesizedPhonemes map 的值型——方向无关地对齐
// 钉死输入侧的「LeadingPhonemes / BodyPhonemes / BodyOffset」形。音素只报标称几何（SynthesizedPhoneme：时长 / 权重）。
//
// · LeadingPhonemes：引导音素（核前的前置辅音），时间序。
// · BodyPhonemes：主体音素（核 + 尾辅音），时间序。
// · BodyOffset（有符号自然秒）：主体起点（= 两列表结合线 junction）相对 note 头的偏移，junction = noteStart + BodyOffset
//   （左负右正）。0 ⇒ 主体起点精确落 note 头；<0 ⇒ 主体首元素跨头起声于拍前；>0 ⇒ 引导末元素跨头伸过拍点。
//
// 分类为结构化双列表（而非从几何派生）：抗帧抖动、跨拍音素可显式归属（跨拍辅音归 leading、跨拍元音归 body，
// 几何相同分类相反）、结构上拼不出交替洞。定位 / 跨 note 去重叠 / melisma 全由宿主按 BodyOffset + 几何锚点解析
// （引擎不报绝对位置，见 PhonemeLayout）。空列表合法（纯 body 的元音起手 / 纯 leading 的边角）。
public readonly struct SynthesizedSyllable
{
    public IReadOnlyList<SynthesizedPhoneme> LeadingPhonemes { get; }
    public IReadOnlyList<SynthesizedPhoneme> BodyPhonemes { get; }
    public double BodyOffset { get; }

    // 扁平合并序（Leading ++ Body）不入 SDK 面：那是宿主扁平索引寻址的私事，需要时宿主侧自行拼（见宿主
    // SynthesizedSyllableExtensions）。本类型只承载结构化真相（双列表 + BodyOffset）。

    public SynthesizedSyllable(IReadOnlyList<SynthesizedPhoneme> leadingPhonemes, IReadOnlyList<SynthesizedPhoneme> bodyPhonemes, double bodyOffset)
    {
        LeadingPhonemes = leadingPhonemes;
        BodyPhonemes = bodyPhonemes;
        BodyOffset = bodyOffset;
    }
}
