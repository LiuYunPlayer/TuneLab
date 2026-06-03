using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base;
using TuneLab.SDK.Voice;

namespace TuneLab.TestPlugins.V1Voice;

// V1 voice 测试引擎：2 个声库；合成时按每个 note 的音高填一段正弦，并产出按 note 键的 phoneme。
// 用于验证：引擎注册、声库列表、CreateVoiceSource、Segment、合成任务事件、SynthesizedPhonemes 按 note 映射。

[VoiceEngine("TLTestVoiceV1")]
public sealed class TestVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => mVoiceInfos;

    public bool Init(string enginePath, out string? error)
    {
        error = null;
        mVoiceInfos.Add("v1-alice", new VoiceSourceInfo { Name = "Alice (V1 Test)", Description = "Test voice Alice" });
        mVoiceInfos.Add("v1-bob", new VoiceSourceInfo { Name = "Bob (V1 Test)", Description = "Test voice Bob" });
        return true;
    }

    public void Destroy() { }

    public IVoiceSource CreateVoiceSource(string id) => new TestVoiceSource(id);

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}

public sealed class TestVoiceSource : IVoiceSource
{
    public TestVoiceSource(string id)
    {
        mId = id;
        mNoteProperties.Add("tension", new SliderConfig(0, -1, 1, false));
        // 自定义自动化参数名避开宿主保留名（Volume/VibratoEnvelope 等内置项）。
        mAutomationConfigs.Add("Growl", new AutomationConfig("Growl", 0, 0, 100, "#E5A573"));
    }

    public string Name => mId;
    public string DefaultLyric => "la";
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, IControllerConfig> PartProperties => mPartProperties;
    public IReadOnlyOrderedMap<string, IControllerConfig> NoteProperties => mNoteProperties;

    public IReadOnlyList<SynthesisSegment<T>> Segment<T>(SynthesisSegment<T> segment) where T : ISynthesisNote
        => this.SimpleSegment(segment);

    public ISynthesisTask CreateSynthesisTask(ISynthesisData data) => new TestSynthesisTask(data);

    readonly string mId;
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<string, IControllerConfig> mPartProperties = new();
    readonly OrderedMap<string, IControllerConfig> mNoteProperties = new();
}

public sealed class TestSynthesisTask : ISynthesisTask
{
    public event Action<SynthesisResult>? Complete;
    public event Action<double>? Progress;
    public event Action<string>? Error;

    public TestSynthesisTask(ISynthesisData data)
    {
        mData = data;
    }

    public void Start()
    {
        mCancelled = false;
        Task.Run(() =>
        {
            try
            {
                var notes = mData.Notes.ToList();
                if (notes.Count == 0)
                {
                    Progress?.Invoke(1);
                    Complete?.Invoke(new SynthesisResult(0, SampleRate, Array.Empty<float>()));
                    return;
                }

                double startTime = notes[0].StartTime;
                double endTime = notes[^1].EndTime;
                int sampleCount = Math.Max(1, (int)((endTime - startTime) * SampleRate));
                var audio = new float[sampleCount];
                var phonemes = new Dictionary<ISynthesisNote, SynthesizedPhoneme[]>();

                for (int n = 0; n < notes.Count; n++)
                {
                    if (mCancelled)
                        return;

                    var note = notes[n];
                    double freq = 440.0 * Math.Pow(2, (note.Pitch - 69) / 12.0);
                    int from = Math.Clamp((int)((note.StartTime - startTime) * SampleRate), 0, sampleCount);
                    int to = Math.Clamp((int)((note.EndTime - startTime) * SampleRate), 0, sampleCount);
                    for (int i = from; i < to; i++)
                        audio[i] = (float)(0.2 * Math.Sin(2 * Math.PI * freq * (i - from) / SampleRate));

                    phonemes[note] = new[]
                    {
                        new SynthesizedPhoneme
                        {
                            Symbol = string.IsNullOrEmpty(note.Lyric) ? "la" : note.Lyric,
                            StartTime = note.StartTime,
                            EndTime = note.EndTime,
                        },
                    };
                    Progress?.Invoke((double)(n + 1) / notes.Count);
                }

                if (mCancelled)
                    return;

                Complete?.Invoke(new SynthesisResult(startTime, SampleRate, audio, null, phonemes));
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex.Message);
            }
        });
    }

    public void Suspend() { }
    public void Resume() { }
    public void Stop() => mCancelled = true;
    public void SetDirty(string dirtyType) { }

    const int SampleRate = 44100;
    readonly ISynthesisData mData;
    volatile bool mCancelled;
}
