using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using TuneLab.Audio;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Data;

internal class AudioPart : Part, IAudioPart
{
    public IActionEvent AudioChanged => mAudioChanged;
    public override DataString Name { get; }
    public override DataStruct<double> Pos { get; }
    public override DataStruct<double> Dur { get; }
    public DataString Path { get; }
    IDataProperty<string> IAudioPart.Path => Path;

    public AudioPart(ITempoManager tempoManager, ITimeSignatureManager timeSignatureManager, AudioPartInfo info) : base(tempoManager, timeSignatureManager)
    {
        Name = new(this, string.Empty);
        Pos = new(this);
        Dur = new(this);
        Path = new(this, string.Empty);
        Dur.Modified.Subscribe(mDurationChanged);
        Path.Modified.Subscribe(async () =>
        {
            mAudioData = null;
            mWaveforms = [];
            mAudioChanged.Invoke();
            IAudioData? audioData = null;
            Waveform[]? waveforms = null;
            await Task.Run(() =>
            {
                try
                {
                    ISampleProvider sampleProvider;
                    var reader = new AudioFileReader(Path);
                    sampleProvider = reader;
                    if (reader.WaveFormat.SampleRate != AudioEngine.SamplingRate)
                    {
                        var resampler = new MediaFoundationResampler(reader, new WaveFormat(AudioEngine.SamplingRate, reader.WaveFormat.Channels));
                        resampler.ResamplerQuality = 60;
                        sampleProvider = resampler.ToSampleProvider();
                    }

                    float[] buffer = new float[reader.Length * AudioEngine.SamplingRate / reader.WaveFormat.SampleRate];
                    var count = sampleProvider.Read(buffer, 0, buffer.Length);

                    int channelCount = sampleProvider.WaveFormat.Channels;
                    if (channelCount == 1)
                    {
                        audioData = new MonoAudioData(buffer);
                        waveforms = [new(buffer)];
                    }
                    else if (channelCount == 2)
                    {
                        float[] left = new float[buffer.Length / 2];
                        float[] right = new float[buffer.Length / 2];
                        for (int i = 0; i < buffer.Length / 2; i++)
                        {
                            left[i] = buffer[i * 2];
                            right[i] = buffer[i * 2 + 1];
                        }
                        audioData = new StereoAudioData(left, right);
                        waveforms = [new(left), new(right)];
                    }
                    reader.Dispose();
                }
                catch (Exception e)
                {
                    audioData = null;
                    waveforms = null;
                }
            });

            if (audioData == null || waveforms == null)
                return;

            mAudioData = audioData;
            mWaveforms = waveforms;
            mAudioChanged.Invoke();
        });
        IDataObject<AudioPartInfo>.SetInfo(this, info);
    }

    public override AudioPartInfo GetInfo()
    {
        return new()
        {
            Name = Name,
            Pos = Pos,
            Dur = Dur,
            Path = Path
        };
    }

    void IDataObject<AudioPartInfo>.SetInfo(AudioPartInfo info)
    {
        IDataObject<AudioPartInfo>.SetInfo(Name, info.Name);
        IDataObject<AudioPartInfo>.SetInfo(Pos, info.Pos);
        IDataObject<AudioPartInfo>.SetInfo(Dur, info.Dur);
        IDataObject<AudioPartInfo>.SetInfo(Path, info.Path);
    }

    protected override IAudioData GetAudioData(int offset, int count)
    {
        if (mAudioData == null)
            return new EmptyAudioData();

        return mAudioData.GetAudioData(offset, count);
    }

    public Waveform GetWaveform(int channelIndex)
    {
        return mWaveforms[channelIndex];
    }

    protected override int SampleCount()
    {
        return mAudioData == null ? 0 : Math.Min(base.SampleCount(), mAudioData.Count);
    }

    protected override int SamplingRate => AudioEngine.SamplingRate;
    public int ChannelCount => mWaveforms.Length;

    Waveform[] mWaveforms = [];
    IAudioData? mAudioData;

    readonly ActionEvent mAudioChanged = new();
}
