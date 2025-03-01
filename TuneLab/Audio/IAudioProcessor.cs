namespace TuneLab.Audio;

internal interface IAudioProcessor
{
    void ProcessBlock(float[] buffer, int offset, int position, int count);
}
