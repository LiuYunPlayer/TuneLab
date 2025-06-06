using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;
using TuneLab.Extensions.Synthesizer;
using TuneLab.Extensions.Voice;

namespace ExtensionCompatibilityLayer.Voice;

internal class VoiceSynthesisSegment : IVoiceSynthesisSegment
{
    public event Action? ProgressUpdated;
    public event Action<SynthesisError?>? Finished;

    public double Progress { get; private set; }
    public string Status => Progress == 1 ? "Done." : "Synthesizing...";

    public VoiceSynthesisSegment(TuneLab.Extensions.Voices.ISynthesisTask task, IVoiceSynthesisInput input, IVoiceSynthesisOutput output)
    {
        this.task = task;
        this.input = input;
        this.output = output;

        task.Progress += progress =>
        {
            Progress = progress;
            ProgressUpdated?.Invoke();
        };
        task.Complete += result =>
        {
            output.SynthesizedPitch = result.SynthesizedPitch.Convert(points => points.Convert(point => point.ToCoreFormat()));
            //FIXME: output.SynthesizedPhonemes = result.SynthesizedPhonemes.
            output.Audio = new MonoAudio() { StartTime = result.StartTime, SampleRate = result.SamplingRate, Samples = result.AudioData };
            Finished?.Invoke(null);
        };
        task.Error += error =>
        {
            Finished?.Invoke(new SynthesisError() { Message = error });
        };
    }

    public void OnDirtyEvent(VoiceDirtyEvent dirtyEvent)
    {
        
    }

    public void StartSynthesis()
    {
        task.Start();
    }

    public void StopSynthesis()
    {
        task.Stop();
    }

    TuneLab.Extensions.Voices.ISynthesisTask task;
    IVoiceSynthesisInput input;
    IVoiceSynthesisOutput output;
}
