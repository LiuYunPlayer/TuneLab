namespace TuneLab.Extensions.Synthesizer;

internal struct MonoAudio
{
    public double StartTime;
    public int SampleRate;
    public float[] Samples;

    public readonly MonoAudio Clone()
    {
        MonoAudio clone = this;
        clone.Samples = (float[])Samples.Clone();
        return clone;
    }
}
