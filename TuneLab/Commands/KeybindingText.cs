using System.Collections.Generic;
using System.Linq;
using System.Text;
using TuneLab.GUI.Input;
using TuneLab.Input;

namespace TuneLab.Commands;

internal static class KeybindingText
{
    // 喂模型的手势语法（存储令牌口径，与 KeyCodec 的表一致；出错时也回灌这段让模型自纠）。
    public const string GestureSyntax =
        "Gesture syntax: optional modifiers \"ctrl+\" \"alt+\" \"shift+\" \"cmd+\" (in that order; \"mod+\" = Ctrl on Windows/Linux, Cmd on macOS) followed by ONE key token — " +
        "a-z, 0-9, f1-f24, up/down/left/right, space, enter, tab, esc, backspace, delete, insert, home, end, pageup, pagedown, " +
        "minus, equal, comma, period, slash, backquote, bracketleft, bracketright, backslash, semicolon, quote, num0-num9, numadd, numsubtract, nummultiply, numdivide, numdecimal. " +
        "Example: \"ctrl+shift+p\".";

    // "<存储令牌> (<用户看到的字形>)"：前者供模型再喂回来，后者供调用方对用户复述。
    public static string Gesture(KeyBinding binding) => Gesture(KeyCodec.Serialize(binding), KeyCodec.ToDisplay(binding));

    // 同上，但从【已拆开】的两个字段拼——结构化结果里 token 与 display 是两个独立字段（那才是
    // --json 消费者要的），渲染时经这里拼回，故复合形只有这一处定义、不会与结构化结果漂移。
    public static string Gesture(string? token, string display) => token == null ? display : token + " (" + display + ")";

    public static string LabelOf(string id) => Keymap.TryGet(id, out var cmd) ? cmd.DisplayName() : id;

    // 同域同手势的其它命令（真冲突，只有一个生效：注册序最小者胜，内建恒胜）。
    public static void AppendConflicts(StringBuilder sb, string id, string prefix)
    {
        var peers = Keymap.SameScopeConflictPeers(id);
        if (peers.Count == 0)
            return;
        sb.Append(prefix).Append("CONFLICT: the same area also binds this gesture to ")
          .Append(string.Join(", ", peers.Select(p => "\"" + LabelOf(p) + "\" (" + p + ")")))
          .Append(" — only one of them fires (the built-in / earliest registered wins). Rebind one of them to fix it.");
    }

    // 同手势但不同作用域的其它命令：跨域共用、非冲突（内层遮蔽外层，按焦点解析）。
    public static IReadOnlyList<KeyCommand> OtherScopeUsers(string id, KeyBinding binding)
    {
        if (!Keymap.TryGet(id, out var self))
            return [];
        var list = new List<KeyCommand>();
        foreach (var cmd in Keymap.Commands)
        {
            if (cmd.Id == id || cmd.Scope == self.Scope)
                continue;
            if (Keymap.Effective(cmd.Id) is { } g && g.Equals(binding))
                list.Add(cmd);
        }
        return list;
    }
}
