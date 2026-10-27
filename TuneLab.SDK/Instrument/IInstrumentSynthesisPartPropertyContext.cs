using System.Collections.Generic;

namespace TuneLab.SDK;

// instrument 声明面（GetXxxConfig）的求值上下文族：调用级只读**值视图**（`*View` 后缀）。与 voice 的 IVoice*View
// 持平行副本（不抽公共类、各自独立演进；instrument note 无 Lyric——其无歌词系统）。为何值而非活对象：见 voice 侧说明（三态 Merge 需值）。

public interface IInstrumentSynthesisPartPropertyContext
{
    IReadOnlyList<IInstrumentSynthesisPartView> Parts { get; }
}

public interface IInstrumentSynthesisNotePropertyContext
{
    IInstrumentSynthesisPartView Part { get; }
    IReadOnlyList<IInstrumentSynthesisNoteView> Notes { get; }
}
