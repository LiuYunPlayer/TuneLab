using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

// 声明面（GetXxxConfig）的宿主实现：调用级只读**值视图**（实现 SDK 的 *View 接口）。每次声明求值时由宿主就地包一层、调完即弃；
// 直接读数据层当前值（不依赖会话 / 管线，故拆除后 / 构造期也可用）。
// SDK 层 voice / instrument 声明 *View 是**平行副本**（各自独立演进）；但底层 part 数据相同，故宿主**一套实现同时满足两域**
// （covariance + 显式接口实现）——这是宿主内部复用，不是 SDK 公共契约。

// 单条 part 的只读值视图：同时实现 voice / instrument 两域的 part 视图。
internal sealed class PartContext(IMidiPart part) : IVoicePartView, IInstrumentPartView
{
    public string VoiceId => part.SoundSource.ID;          // IVoicePartView
    public string InstrumentId => part.SoundSource.ID;     // IInstrumentPartView（同一底层音源 id）
    public PropertyObject PartProperties => part.Properties.GetInfo();

    IReadOnlyList<IVoiceNoteView> IVoicePartView.Notes => Notes;
    IReadOnlyList<IInstrumentNoteView> IInstrumentPartView.Notes => Notes;
    IReadOnlyList<PartNote> Notes => mNotes ??= part.Notes.Select(n => new PartNote(n)).ToList();

    public bool TryGetAutomation(string key, [MaybeNullWhen(false)] out IAutomationEvaluator automation)
    {
        if (string.IsNullOrEmpty(key) || !part.SoundSource.AutomationConfigs.ContainsKey(key))
        {
            automation = null;
            return false;
        }
        automation = new Evaluator(part, key);
        return true;
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
    internal sealed class PartNote(INote note) : IVoiceNoteView, IInstrumentNoteView
    {
        public double StartTime => note.StartTime;
        public double EndTime => note.EndTime;     // = TempoManager.GetTime(GlobalEndPos)，原始满末
        public int Pitch => note.Pitch.Value;
        public string Lyric => note.Lyric.Value;   // 仅 IVoiceNoteView 暴露
        public PropertyObject Properties => note.Properties.GetInfo();
    }
}

// part 级声明壳（多选 part；单选 = 1 个、无选中 = 空）：同时满足两域（covariance over IReadOnlyList<PartContext>）。
internal sealed class PartPropertyContext(IReadOnlyList<PartContext> parts) : IVoicePartPropertyContext, IInstrumentPartPropertyContext
{
    public static readonly PartPropertyContext Empty = new([]);
    IReadOnlyList<IVoicePartView> IVoicePartPropertyContext.Parts => parts;
    IReadOnlyList<IInstrumentPartView> IInstrumentPartPropertyContext.Parts => parts;

    public static PartPropertyContext Single(IMidiPart part) => new([new PartContext(part)]);
}

// note 级声明壳（单 part、多选其下 note）：同时满足两域。
internal sealed class NotePropertyContext(PartContext part, IReadOnlyList<PartContext.PartNote> notes)
    : IVoiceNotePropertyContext, IInstrumentNotePropertyContext
{
    IVoicePartView IVoiceNotePropertyContext.Part => part;
    IReadOnlyList<IVoiceNoteView> IVoiceNotePropertyContext.Notes => notes;
    IInstrumentPartView IInstrumentNotePropertyContext.Part => part;
    IReadOnlyList<IInstrumentNoteView> IInstrumentNotePropertyContext.Notes => notes;
}
