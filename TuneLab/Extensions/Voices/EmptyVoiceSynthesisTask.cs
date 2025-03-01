using System;

namespace TuneLab.Extensions.Voices;

internal class EmptyVoiceSynthesisTask : ISynthesisTask
{
    public event Action<SynthesisResult>? Complete;
    public event Action<double>? Progress;
    public event Action<string>? Error;

    public EmptyVoiceSynthesisTask(ISynthesisData piece)
    {
        mStartTime = piece.StartTime();
    }

    public void Start()
    {
        Complete?.Invoke(new SynthesisResult(mStartTime, 44100, []));
    }

    public void Suspend()
    {

    }

    public void Resume()
    {

    }

    public void Stop()
    {

    }

    public void SetDirty(string dirtyType)
    {

    }

    double mStartTime;
}
