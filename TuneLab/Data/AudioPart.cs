using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Base.Utils;
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

    public AudioPart(ITrack track, AudioPartInfo info) : base(track)
    {
        Name = new(this, string.Empty);
        Pos = new(this);
        Dur = new(this);
        Path = new(this, string.Empty);
        Dur.Modified.Subscribe(mDurationChanged);
        Path.Modified.Subscribe(Reload);
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

    public async void Reload()
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
                int sampleRate = AudioEngine.SampleRate;
                var data = AudioUtils.Decode(Path, ref sampleRate);
                switch (data.Length)
                {
                    case 1:
                        audioData = new MonoAudioData(data[0]);
                        waveforms = [new(data[0])];
                        break;
                    case 2:
                        audioData = new StereoAudioData(data[0], data[1]);
                        waveforms = [new(data[0]), new(data[1])];
                        break;
                }
            }
            catch (Exception ex)
            {
                audioData = null;
                waveforms = null;
                Log.Error($"Failed to load audio data from {Path}: {ex.Message}");
            }
        });

        if (audioData == null || waveforms == null)
            return;

        mAudioData = audioData;
        mWaveforms = waveforms;
        mAudioChanged.Invoke();
    }

    protected override int SampleCount()
    {
        return mAudioData == null ? 0 : Math.Min(base.SampleCount(), mAudioData.Count);
    }

    protected override int SampleRate => AudioEngine.SampleRate;
    public int ChannelCount => mWaveforms.Length;

    Waveform[] mWaveforms = [];
    IAudioData? mAudioData;

    readonly ActionEvent mAudioChanged = new();
}
