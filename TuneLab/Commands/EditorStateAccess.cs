using System;
using TuneLab.Data;
using TuneLab.Scripting;

namespace TuneLab.Commands;

// 【有界面的宿主进程】里的编辑器态实现：把编辑器早就在用的四个访问器委托收成一个对象。
// 每次现调委托，故用户切 part / 改量化 / 改选区后，下一条命令看到的就是新值。
// headless 不用它（那里根本没有编辑器态，整个 EditorState 为 null）。
internal sealed class EditorStateAccess(
    Func<IMidiPart?>? currentPart,
    Func<IQuantization?>? quantization,
    Func<ScriptSelection?>? selection,
    Func<ScriptPianoSelection?>? pianoSelection,
    IScriptSelectionWriter? selectionWriter = null) : IEditorStateAccess
{
    public IMidiPart? CurrentPart => currentPart?.Invoke();
    public IQuantization? Quantization => quantization?.Invoke();
    public ScriptSelection? Selection => selection?.Invoke();
    public ScriptPianoSelection? PianoSelection => pianoSelection?.Invoke();
    public IScriptSelectionWriter? SelectionWriter => selectionWriter;
}
