namespace TuneLab.Audio;

// 宿主内部单声道音频值类型（播放/波形/链尾段消费面）。
// 曾位于 Foundation 作为 host↔插件的音频契约词汇——effect SDK 改走 IAudioSegment/Read 后
// 插件面不再整段传音频，遂退役出插件可见面、迁回宿主。
internal struct MonoAudio(double startTime, int sampleRate, float[] samples)
{
    public double StartTime = startTime;
    public int SampleRate = sampleRate;
    public float[] Samples = samples;
}
