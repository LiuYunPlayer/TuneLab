using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

// 声明面（GetXxxConfig）的宿主实现：调用级只读**值视图**（实现 SDK 的 *View 接口）。每次声明求值时由宿主就地包一层、调完即弃；
// 直接读数据层当前值（不依赖会话 / 管线，故拆除后 / 构造期也可用）。
// SDK 层 voice / instrument 声明 *View 是**平行副本**（各自独立演进）；但底层 part 数据相同，故宿主**一套实现同时满足两域**
// （covariance + 显式接口实现）——这是宿主内部复用，不是 SDK 公共契约。

// 单条 part 的只读值视图：同时实现 voice / instrument 两域的 part 视图。
internal sealed class PartContext(IMidiPart part) : IVoiceSynthesisPartView, IInstrumentSynthesisPartView
{
    public string VoiceId => part.SoundSource.ID;          // IVoiceSynthesisPartView
    public string InstrumentId => part.SoundSource.ID;     // IInstrumentPartView（同一底层音源 id）
    public PropertyObject PartProperties => part.Properties.GetInfo();

    IReadOnlyList<IVoiceSynthesisNoteView> IVoiceSynthesisPartView.Notes => Notes;
    IReadOnlyList<IInstrumentSynthesisNoteView> IInstrumentSynthesisPartView.Notes => Notes;
    IReadOnlyList<PartNote> Notes => mNotes ??= part.Notes.Select(n => new PartNote(n)).ToList();

    // 已声明 automation 轨当前曲线求值器只读 map：轨集 = 当前音源引擎声明的 AutomationConfigs；
    // 求值器无状态、每次读现建（声明面是调用级一次性求值，不留存）。
    public IReadOnlyMap<string, IAutomationEvaluator> Automations
    {
        get
        {
            var map = new Map<string, IAutomationEvaluator>();
            foreach (var kvp in part.SoundSource.AutomationConfigs)
                map.Add(kvp.Key.Id, new Evaluator(part, kvp.Key.Id));
            return map;
        }
    }

    List<PartNote>? mNotes;

    // 读某条已声明自动化轨当前曲线：查询轴全局秒 → 逐点换算全局 tick → 读 part 终值（含偏差源）。
    sealed class Evaluator(IMidiPart part, string key) : IAutomationEvaluator
    {
        public double[] Evaluate(IReadOnlyList<double> times)
        {
            var ticks = new double[times.Count];
            for (int i = 0; i < times.Count; i++)
                ticks[i] = part.TempoManager.GetTick(times[i]);
            return part.GetFinalAutomationValues(ticks, key);
        }
    }

    // part 数据 note：同时满足 voice（带 Lyric）/ instrument（无 Lyric，多出的 Lyric 成员对其接口不可见）两域。EndTime 取原始满末。
    internal sealed class PartNote(INote note) : IVoiceSynthesisNoteView, IInstrumentSynthesisNoteView
    {
        public double StartTime => note.StartTime;
        public double EndTime => note.EndTime;     // = TempoManager.GetTime(GlobalEndPos)，原始满末
        public int Pitch => note.Pitch.Value;
        public string Lyric => note.Lyric.Value;   // 仅 IVoiceSynthesisNoteView 暴露
        public PropertyObject Properties => note.Properties.GetInfo();
        // 该 note 的**显示音素**（仅 IVoiceSynthesisNoteView 暴露——instrument 无音素）：钉死则取 IPhoneme（带属性）、
        // 否则取合成音素（属性空，编辑时由宿主钉死后写入）。引擎据此对所见音素声明属性 schema，无论是否已钉死。
        public IReadOnlyList<IVoiceSynthesisPhonemeView> Phonemes
        {
            get
            {
                if (note.Phonemes.Count > 0)
                    return note.Phonemes.Select(p => new PartPhoneme(p)).ToList();
                if (note.SynthesizedPhonemes is { Length: > 0 } synth)
                    return synth.Select(p => new PartPhoneme(p)).ToList();
                return [];
            }
        }
    }

    // part 数据音素的只读值视图（声明面，voice 专属）：几何当前值 + per-phoneme 属性值快照。
    // 两种来源：钉死音素 IPhoneme（属性走 HasProperties 闸门、不物化）、合成音素 SynthesizedPhoneme（属性恒空）。
    internal sealed class PartPhoneme : IVoiceSynthesisPhonemeView
    {
        public string Symbol { get; }
        public double Duration { get; }
        public double StretchWeight { get; }
        public bool IsLead { get; }
        public PropertyObject Properties { get; }

        public PartPhoneme(IPhoneme phoneme)
        {
            Symbol = phoneme.Symbol.Value;
            Duration = phoneme.Duration.Value;
            StretchWeight = phoneme.StretchWeight.Value;
            IsLead = phoneme.IsLead.Value;
            Properties = phoneme.HasProperties ? phoneme.Properties.GetInfo() : PropertyObject.Empty;
        }

        public PartPhoneme(SynthesizedPhoneme phoneme)
        {
            Symbol = phoneme.Symbol;
            Duration = phoneme.Duration;
            StretchWeight = phoneme.StretchWeight;
            IsLead = phoneme.IsLead;
            Properties = PropertyObject.Empty;
        }
    }
}

// part 级声明壳（多选 part；单选 = 1 个、无选中 = 空）：同时满足两域（covariance over IReadOnlyList<PartContext>）。
internal sealed class PartPropertyContext(IReadOnlyList<PartContext> parts) : IVoiceSynthesisPartPropertyContext, IInstrumentSynthesisPartPropertyContext
{
    public static readonly PartPropertyContext Empty = new([]);
    IReadOnlyList<IVoiceSynthesisPartView> IVoiceSynthesisPartPropertyContext.Parts => parts;
    IReadOnlyList<IInstrumentSynthesisPartView> IInstrumentSynthesisPartPropertyContext.Parts => parts;

    public static PartPropertyContext Single(IMidiPart part) => new([new PartContext(part)]);
}

// note 级声明壳（单 part、多选其下 note）：同时满足两域。
internal sealed class NotePropertyContext(PartContext part, IReadOnlyList<PartContext.PartNote> notes)
    : IVoiceSynthesisNotePropertyContext, IInstrumentSynthesisNotePropertyContext
{
    IVoiceSynthesisPartView IVoiceSynthesisNotePropertyContext.Part => part;
    IReadOnlyList<IVoiceSynthesisNoteView> IVoiceSynthesisNotePropertyContext.Notes => notes;
    IInstrumentSynthesisPartView IInstrumentSynthesisNotePropertyContext.Part => part;
    IReadOnlyList<IInstrumentSynthesisNoteView> IInstrumentSynthesisNotePropertyContext.Notes => notes;
}
