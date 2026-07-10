using System.Collections.Generic;

namespace TuneLab.SDK;

// 一个归属 note 的合成音节：该 note 的音素序列 + 前置量 Preutterance（note 头之前音素的占位长度 = 拍前发声量，自然秒）。
// 一 note ≈ 一音节，本类型即该音节的音素布局。IVoiceSynthesisSession.SynthesizedPhonemes map 的值型——方向无关地对齐
// 钉死输入侧的「音素[] + Preutterance」形：音素只报标称几何（SynthesizedPhoneme：时长 / 权重），前后归属由 Preutterance 派生（见 PhonemeLayout）。
// 定位 / 跨 note 去重叠 / melisma 全由宿主按 Preutterance + 几何锚点解析（引擎不报绝对位置）。
public readonly struct SynthesizedSyllable
{
    public IReadOnlyList<SynthesizedPhoneme> Phonemes { get; }
    public double Preutterance { get; }

    public SynthesizedSyllable(IReadOnlyList<SynthesizedPhoneme> phonemes, double preutterance)
    {
        Phonemes = phonemes;
        Preutterance = preutterance;
    }
}
