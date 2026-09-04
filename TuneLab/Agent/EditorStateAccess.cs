using System;
using TuneLab.Commands;
using TuneLab.Data;
using TuneLab.Scripting;

namespace TuneLab.Agent;

// 侧栏 agent 的编辑器态实现：把宿主早就在用的四个访问器委托收成一个对象交给命令面。
// 每次现调委托，故用户切 part / 改量化 / 改选区后，下一条命令看到的就是新值。
internal sealed class EditorStateAccess(
    Func<IMidiPart?>? currentPart,
    Func<IQuantization?>? quantization,
    Func<ScriptSelection?>? selection,
    Func<ScriptPianoSelection?>? pianoSelection) : IEditorStateAccess
{
    public IMidiPart? CurrentPart => currentPart?.Invoke();
    public IQuantization? Quantization => quantization?.Invoke();
    public ScriptSelection? Selection => selection?.Invoke();
    public ScriptPianoSelection? PianoSelection => pianoSelection?.Invoke();
}
