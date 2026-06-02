using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base;
using TuneLab.SDK.Voice;
using TuneLab.TestPlugins.Suite.Common;

namespace TuneLab.TestPlugins.Suite.Voice;

// 一包多插件之 voice：1 个声库（名取自共享 Common）。合成产出静音 + phoneme（合成保真已由 V1.Voice 覆盖，此处从简）。
[VoiceEngine("TLSuiteVoice")]
public sealed class SuiteVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => mVoiceInfos;

    public bool Init(string enginePath, out string? error)
    {
        error = null;
        mVoiceInfos.Add("suite-voice", new VoiceSourceInfo { Name = SuiteCommon.Label("Voice"), Description = "Suite shared-infra voice" });
        return true;
    }

    public void Destroy() { }
    public IVoiceSource CreateVoiceSource(string id) => new SuiteVoiceSource(id);

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}

public sealed class SuiteVoiceSource(string id) : IVoiceSource
{
    public string Name => id;
    public string DefaultLyric => "la";
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, IControllerConfig> PartProperties => mPartProperties;
    public IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties => mNoteProperties;

    public IReadOnlyList<SynthesisSegment<T>> Segment<T>(SynthesisSegment<T> segment) where T : ISynthesisNote
        => this.SimpleSegment(segment);

    public ISynthesisTask CreateSynthesisTask(ISynthesisData data) => new SuiteSynthesisTask(data);

    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<string, IControllerConfig> mPartProperties = new();
    readonly OrderedMap<string, IControllerConfig> mNoteProperties = new();
}

public sealed class SuiteSynthesisTask(ISynthesisData data) : ISynthesisTask
{
    public event Action<SynthesisResult>? Complete;
    public event Action<double>? Progress;
    public event Action<string>? Error;

    public void Start()
    {
        var notes = data.Notes.ToList();
        double startTime = notes.Count > 0 ? notes[0].StartTime : 0;
        double endTime = notes.Count > 0 ? notes[^1].EndTime : 0;
        int sampleCount = Math.Max(1, (int)((endTime - startTime) * SampleRate));
        var phonemes = new Dictionary<ISynthesisNote, SynthesizedPhoneme[]>();
        foreach (var note in notes)
            phonemes[note] = [new SynthesizedPhoneme { Symbol = note.Lyric, StartTime = note.StartTime, EndTime = note.EndTime }];

        Progress?.Invoke(1);
        Complete?.Invoke(new SynthesisResult(startTime, SampleRate, new float[sampleCount], null, phonemes));
    }

    public void Suspend() { }
    public void Resume() { }
    public void Stop() { }
    public void SetDirty(string dirtyType) { }

    const int SampleRate = 44100;
}
