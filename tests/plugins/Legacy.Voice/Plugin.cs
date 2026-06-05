using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using TuneLab.Extensions.Voices;

namespace TuneLab.TestPlugins.LegacyVoice;

// Legacy voice 测试引擎：用【老】接口（TuneLab.Extensions.Voices + TuneLab.Base.*）。
// 经 Compat.Legacy 适配成 V1。除合成 + phoneme 外，刻意带 NumberConfig / AutomationConfig，
// 以验证 Config 家族跨代转换（NumberConfig→SliderConfig、AutomationConfig→AutomationConfig）。

[VoiceEngine("TLTestVoiceLegacy")]
public sealed class LegacyTestVoiceEngine : IVoiceEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos => mVoiceInfos;

    public bool Init(string enginePath, out string? error)
    {
        error = null;
        mVoiceInfos.Add("legacy-carol", new VoiceSourceInfo { Name = "Carol (Legacy Test)", Description = "Legacy test voice" });
        return true;
    }

    public void Destroy() { }

    public IVoiceSource CreateVoiceSource(string id) => new LegacyTestVoiceSource(id);

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}

public sealed class LegacyTestVoiceSource : IVoiceSource
{
    public LegacyTestVoiceSource(string id)
    {
        mId = id;
        mNoteProperties.Add("tension", new NumberConfig(0, -1, 1, false));
        // 自定义自动化参数名避开宿主保留名（Volume/VibratoEnvelope 等内置项）——否则会被内置项占用、显示不出。
        mAutomationConfigs.Add("Breathiness", new AutomationConfig("Breathiness", 0, -60, 12, "#88AAFF"));
    }

    public string Name => mId;
    public string DefaultLyric => "a";
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, IPropertyConfig> PartProperties => mPartProperties;
    public IReadOnlyOrderedMap<string, IPropertyConfig> NoteProperties => mNoteProperties;

    public IReadOnlyList<SynthesisSegment<T>> Segment<T>(SynthesisSegment<T> segment) where T : ISynthesisNote
        => this.SimpleSegment(segment);

    public ISynthesisTask CreateSynthesisTask(ISynthesisData data) => new LegacyTestSynthesisTask(data);

    readonly string mId;
    readonly OrderedMap<string, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<string, IPropertyConfig> mPartProperties = new();
    readonly OrderedMap<string, IPropertyConfig> mNoteProperties = new();
}

public sealed class LegacyTestSynthesisTask : ISynthesisTask
{
    public event Action<SynthesisResult>? Complete;
    public event Action<double>? Progress;
    public event Action<string>? Error;

    public LegacyTestSynthesisTask(ISynthesisData data)
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
                    // attack/release 线性包络：note 边界处波形从 0 渐入/渐出，消除截断造成的爆音（"啪"声）。
                    // 渐入/渐出各取设定时长与半个 note 长度的较小者，短音符也不会重叠（attack==0 时不进渐变分支，无除零）。
                    int length = to - from;
                    int attack = Math.Min(AttackSamples, length / 2);
                    int release = Math.Min(ReleaseSamples, length / 2);
                    for (int i = from; i < to; i++)
                    {
                        int pos = i - from;
                        double envelope = pos < attack ? (double)pos / attack
                            : pos >= length - release ? (double)(length - pos) / release
                            : 1.0;
                        audio[i] = (float)(0.2 * envelope * Math.Sin(2 * Math.PI * freq * pos / SampleRate));
                    }

                    phonemes[note] = new[]
                    {
                        new SynthesizedPhoneme
                        {
                            Symbol = string.IsNullOrEmpty(note.Lyric) ? "a" : note.Lyric,
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
    const int AttackSamples = (int)(0.008 * SampleRate);   // 8ms 渐入
    const int ReleaseSamples = (int)(0.012 * SampleRate);  // 12ms 渐出
    readonly ISynthesisData mData;
    volatile bool mCancelled;
}
