namespace TuneLab.Audio;

internal interface IAudioEncoder
{
    void Encode(string path, float[] buffer, int sampleRate, int channelCount, AudioEncodeSettings settings);
}
