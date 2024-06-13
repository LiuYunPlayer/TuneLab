using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Utils;

namespace TuneLab.Audio.NAudio;

internal class NAudioEngine : IAudioEngine
{
    public event Action? PlayStateChanged;
    public event Action? ProgressChanged;
    public bool IsPlaying => mIsPlaying;
    public int SamplingRate => 44100;
    public double CurrentTime => (double)(mPlayer.GetPosition() / sizeof(float) / mPlayer.OutputWaveFormat.Channels + mStartPosition) / SamplingRate;

    public void Init(IAudioProcessor processor)
    {
        var context = SynchronizationContext.Current ?? throw new Exception("Can't get SynchronizationContext");
        mAudioProcessor = processor;

        mPlayer.NumberOfBuffers = 20;
        mPlayer.DesiredLatency = 320;
        mPlayer.Init(new SampleProvider(this));

        System.Timers.Timer timer = new(16);
        timer.Elapsed += (s, e) => { context.Post(_ => { if (mIsPlaying) ProgressChanged?.Invoke(); }, null); };
        timer.Start();
    }

    public void Destroy()
    {
        Pause();
    }

    public void Play()
    {
        if (IsPlaying)
            return;

        mIsPlaying = true;
        PlayStateChanged?.Invoke();
        mPlayer.Play();
    }

    public void Pause()
    {
        if (!IsPlaying)
            return;

        mIsPlaying = false;
        PlayStateChanged?.Invoke();
        mPlayer.Pause();
    }

    public void Seek(double time)
    {
        mPlayer.Stop();
        mStartPosition = (int)(time * SamplingRate);
        mPosition = mStartPosition;

        if (IsPlaying)
        {
            mPlayer.Play();
        }

        ProgressChanged?.Invoke();
    }

    class SampleProvider(NAudioEngine engine) : ISampleProvider
    {
        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(engine.SamplingRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            int position = engine.mPosition;
            int length = count / 2;
            int endPosition = position + length;

            engine.mPosition += length;

            for (int i = offset; i < offset + count; i++)
            {
                buffer[i] = 0;
            }

            engine.mAudioProcessor?.ProcessBlock(buffer, offset, position, endPosition - position);

            return count;
        }
    }

    bool mIsPlaying = false;
    int mPosition = 0;
    int mStartPosition = 0;
    readonly WaveOutEvent mPlayer = new();
    IAudioProcessor? mAudioProcessor;
}
