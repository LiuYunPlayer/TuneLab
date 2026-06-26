using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// instrument 声明面（GetXxxConfig）的求值上下文族：调用级只读**值视图**（`*View` 后缀）。与 voice 的 IVoice*View
// 持平行副本（不抽公共类、各自独立演进；instrument note 无 Lyric——其无歌词系统）。为何值而非活对象：见 voice 侧说明（三态 Merge 需值）。

// 单条 part 的只读值视图（声明面）。
public interface IInstrumentPartView
{
    // 该 part 选定音源（= IInstrumentEngine.InstrumentSourceInfos 的 key）：引擎据此分流多音源。
    string InstrumentId { get; }
    // 当前 note 集合（只读、宿主当前序）：原始几何，instrument 不去重叠。
    IReadOnlyList<IInstrumentNoteView> Notes { get; }
    PropertyObject PartProperties { get; }
    bool TryGetAutomation(string key, [MaybeNullWhen(false)] out IAutomationEvaluator automation);
}

// instrument part 数据 note 的只读值视图（声明面；满末；【无 Lyric】）。
public interface IInstrumentNoteView
{
    double StartTime { get; }
    double EndTime { get; }
    int Pitch { get; }
    PropertyObject Properties { get; }
}

public interface IInstrumentPartPropertyContext
{
    IReadOnlyList<IInstrumentPartView> Parts { get; }
}

public interface IInstrumentNotePropertyContext
{
    IInstrumentPartView Part { get; }
    IReadOnlyList<IInstrumentNoteView> Notes { get; }
}
