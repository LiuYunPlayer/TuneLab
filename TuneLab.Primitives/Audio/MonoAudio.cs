namespace TuneLab.Primitives.Audio;

// 跨边界单声道音频值类型。冻结 ABI 词汇：host ↔ effect/voice 插件之间传音频。
// 当前仅契约层、暂无消费者——作为未来 effect/voice 音频路径的奠基词汇显式纳入。
public struct MonoAudio(double startTime, int sampleRate, float[] samples)
{
    public double StartTime = startTime;
    public int SampleRate = sampleRate;
    public float[] Samples = samples;
}
