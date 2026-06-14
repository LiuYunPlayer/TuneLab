using TuneLab.Audio;
using TuneLab.Foundation;

namespace TuneLab.Data.Synthesis;

// 一个已完成的合成音频段（工程率）：链尾音频 + 预算波形峰值。
// 播放（MidiPart.GetAudioData）与波形绘制（PianoScrollView）都按段消费——各段按自身时间位置混音/绘制，
// 不再拼成整 part 单条 buffer：稀疏 part 不摊零、段间空洞留白、编辑只重算变化段（其余段波形按引用复用）。
internal readonly record struct SynthesizedSegment(MonoAudio Audio, Waveform Waveform);
