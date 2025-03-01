namespace TuneLab.Audio;

internal interface IAudioEncoder
{
    void EncodeToWav(string path, float[] buffer, int sampleRate, int bitPerSample, int channelCount);
}
