namespace TuneLab.Audio;

internal interface IAudioSampleProvider
{
    void Read(float[] buffer, int offset, int count);
}
