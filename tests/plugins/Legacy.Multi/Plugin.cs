using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TuneLab.Base.Structures;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voices;

namespace TuneLab.TestPlugins.LegacyMulti;

// 同一 dll 内多个老 attribute，验证 Legacy 一包多插件（Compat 扫描全部 attribute 注册）。

[ImportFormat("tlm1")]
public sealed class MultiImport1 : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream) => SampleProject("legacy-multi #1", 60);
    static ProjectInfo SampleProject(string name, int pitch)
    {
        var p = new ProjectInfo();
        p.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        var t = new TrackInfo { Name = name };
        var part = new MidiPartInfo { Name = name, Pos = 0, Dur = 480 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = pitch, Lyric = "m" });
        t.Parts.Add(part);
        p.Tracks.Add(t);
        return p;
    }

    internal static ProjectInfo Sample(string name, int pitch) => SampleProject(name, pitch);
}

[ImportFormat("tlm2")]
public sealed class MultiImport2 : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream) => MultiImport1.Sample("legacy-multi #2", 67);
}

[VoiceEngine("TLLegacyMultiVoice")]
public sealed class MultiVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => mVoiceInfos;

    public bool Init(string enginePath, out string? error)
    {
        error = null;
        mVoiceInfos.Add("legacy-multi-voice", new VoiceSourceInfo { Name = "Legacy Multi Voice", Description = "voice in legacy multi package" });
        return true;
    }

    public void Destroy() { }
    public IVoiceSource CreateVoiceSource(string id) => new MultiVoiceSource(id);

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}

public sealed class MultiVoiceSource(string id) : IVoiceSource
{
    public string Name => id;
    public string DefaultLyric => "a";
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, TuneLab.Base.Properties.IPropertyConfig> PartProperties => mPartProperties;
    public IReadOnlyOrderedMap<string, TuneLab.Base.Properties.IPropertyConfig> NoteProperties => mNoteProperties;

    public IReadOnlyList<SynthesisSegment<T>> Segment<T>(SynthesisSegment<T> segment) where T : ISynthesisNote
        => this.SimpleSegment(segment);

    public ISynthesisTask CreateSynthesisTask(ISynthesisData data) => new MultiSynthesisTask(data);

    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<string, TuneLab.Base.Properties.IPropertyConfig> mPartProperties = new();
    readonly OrderedMap<string, TuneLab.Base.Properties.IPropertyConfig> mNoteProperties = new();
}

public sealed class MultiSynthesisTask(ISynthesisData data) : ISynthesisTask
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
