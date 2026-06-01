namespace TuneLab.Primitives.Audio;

// 跨边界单声道音频值类型（§三.10）。冻结 ABI 词汇：host ↔ effect/voice 插件之间传音频。
// 本话题（#7，仅契约层）暂无消费者——按 directive 显式纳入，作为 #11 effect 音频路径的奠基词汇。
public struct MonoAudio(double startTime, int sampleRate, float[] samples)
{
    public double StartTime = startTime;
    public int SampleRate = sampleRate;
    public float[] Samples = samples;
}
