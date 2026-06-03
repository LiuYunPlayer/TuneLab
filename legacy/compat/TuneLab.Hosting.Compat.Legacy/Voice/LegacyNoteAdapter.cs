using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LProp = TuneLab.Base.Properties;
using LVoice = TuneLab.Extensions.Voices;
using VVoice = TuneLab.SDK.Voice;
using TuneLab.Hosting.Compat.Legacy.Conversion;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

// 一宿主 note 一包装、身份保持 + 缓存：使插件用作字典键的 note 能双向映射回宿主 V1 note。
internal sealed class NoteWrapperCache
{
    readonly Dictionary<VVoice.ISynthesisNote, LegacyNoteAdapter> mMap = new(ReferenceEqualityComparer.Instance);

    [return: NotNullIfNotNull(nameof(note))]
    public LegacyNoteAdapter? Wrap(VVoice.ISynthesisNote? note)
    {
        if (note == null)
            return null;
        if (!mMap.TryGetValue(note, out var adapter))
        {
            adapter = new LegacyNoteAdapter(note, this);
            mMap[note] = adapter;
        }
        return adapter;
    }
}

// 把宿主 V1 ISynthesisNote 适配成老 ISynthesisNote 喂给老引擎。
//   · Properties/Phonemes 懒转换 + 缓存（边界 eager、不给逐访问实时转换）。
//   · Index 供 Segment 下标回查（零强制类型转换）。
//   · Origin 供合成结果 phonemes 键映射回宿主 note。
internal sealed class LegacyNoteAdapter(VVoice.ISynthesisNote origin, NoteWrapperCache cache) : LVoice.ISynthesisNote
{
    public VVoice.ISynthesisNote Origin => origin;
    public int Index { get; set; } = -1;

    public LVoice.ISynthesisNote? Next => cache.Wrap(origin.Next);
    public LVoice.ISynthesisNote? Last => cache.Wrap(origin.Last);
    public double StartTime => origin.StartTime;
    public double EndTime => origin.EndTime;
    public int Pitch => origin.Pitch;
    public string Lyric => origin.Lyric;
    public LProp.PropertyObject Properties => mProperties ??= origin.Properties.ToLegacy();
    public IReadOnlyList<LVoice.SynthesizedPhoneme> Phonemes => mPhonemes ??= origin.Phonemes.Select(p => p.ToLegacy()).ToList();

    LProp.PropertyObject? mProperties;
    IReadOnlyList<LVoice.SynthesizedPhoneme>? mPhonemes;
}
