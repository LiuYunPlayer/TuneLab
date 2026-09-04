using System;
using TuneLab.Data;
using TuneLab.Scripting;

namespace TuneLab.Commands;

// 「用户此刻在编辑器里看着什么」——当前 part、量化档、编排区选区、钢琴窗选区。
//
// 这是**坐在编辑器里**才有的隐式上下文：脚本的 getScriptInfo / getInputConfig 会读它（如按选中范围
// 给默认值），故求值这些脚本的命令要能拿到。
//
// 各入口的处置（docs/command-surface.md §5.2）：
//  · 侧栏 agent —— 实时访问器，用户切 part 即变；
//  · CLI attach —— 继承运行中实例的真实编辑器态（用户开着界面让外部 agent 干活时，"当前 part"的自然
//    含义就是他正在看的那个），允许命令参数显式覆盖；
//  · headless —— **没有编辑器态**，整个 ctx.EditorState 为 null。依赖它的命令要么如实按"没有"处理，
//    要么要求显式传参——不能默认成 track 0 part 0，那是在猜。
//
// 取属性而非快照：实现方每次现读，故命令拿到的恒是当刻值（与 CommandContext 里其余访问器同理）。
internal interface IEditorStateAccess
{
    IMidiPart? CurrentPart { get; }
    IQuantization? Quantization { get; }
    ScriptSelection? Selection { get; }
    ScriptPianoSelection? PianoSelection { get; }
}

internal static class EditorStateExtensions
{
    // 命令面的编辑器态 → 脚本层要的那几个委托。脚本层的签名是既有的（宿主菜单也在用同一套），
    // 这里只做形状适配，不复制任何判据。没有编辑器态（headless）时给 null，脚本读到的就是"没有"。
    public static Func<IMidiPart?>? CurrentPart(this CommandContext ctx)
        => ctx.EditorState is { } s ? () => s.CurrentPart : null;

    public static Func<IQuantization?>? Quantization(this CommandContext ctx)
        => ctx.EditorState is { } s ? () => s.Quantization : null;

    public static Func<ScriptSelection?>? Selection(this CommandContext ctx)
        => ctx.EditorState is { } s ? () => s.Selection : null;

    public static Func<ScriptPianoSelection?>? PianoSelection(this CommandContext ctx)
        => ctx.EditorState is { } s ? () => s.PianoSelection : null;
}
