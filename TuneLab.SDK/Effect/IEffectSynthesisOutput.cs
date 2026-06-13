using TuneLab.Foundation;

namespace TuneLab.SDK;

// 一次效果器合成的输出汇：引擎把处理后的整段音频写入 Audio。
// V1 仅音频；pitch / automation 回写为未来需求，暂不纳入。
public interface IEffectSynthesisOutput
{
    MonoAudio Audio { get; set; }
}
