namespace TuneLab.Audio;

internal interface IAudioResampler
{
    IAudioStream Resample(IAudioProvider input, int outputSampleRate);
}
