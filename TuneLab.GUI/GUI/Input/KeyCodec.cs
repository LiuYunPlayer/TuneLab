using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Input;

namespace TuneLab.GUI.Input;

// 快捷键的"名字标准"，与 Avalonia 枚举名解耦：
//   · 存储令牌——自有的一套稳定、简洁字符串（对齐 W3C UI Events code 的精简版：a / 1 / f1 / up / space / comma…），
//     整条手势序列化成单字符串 "ctrl+shift+a"（修饰规范序 ctrl+alt+shift+cmd，cmd=Meta）。
//   · 显示符号——面向用户的字形（Apple 约定：↑↓←→ / ⌘⌥⌃⇧ / ⏎⇥⎋⌫…），与存储令牌是两张独立的表。
// 键名/显示/(存储↔Avalonia Key)的转换全部归此处，别处不得再依赖 Key.ToString()。见 docs/keybinding-system.md §1.3。
internal static class KeyCodec
{
    static readonly Dictionary<Key, string> sKeyToToken = new();
    static readonly Dictionary<string, Key> sTokenToKey = new();
    static readonly Dictionary<Key, string> sKeyDisplay = new();   // 平台无关的键显示（修饰另算，Mac 具名键在 KeyDisplay 覆盖）

    static KeyCodec()
    {
        for (Key k = Key.A; k <= Key.Z; k++)                       // 字母：token=小写、显示=大写
        {
            var token = ((char)('a' + (k - Key.A))).ToString();
            Register(k, token, token.ToUpperInvariant());
        }
        for (Key k = Key.D0; k <= Key.D9; k++)                     // 主键盘数字
        {
            var d = (k - Key.D0).ToString();
            Register(k, d, d);
        }
        for (Key k = Key.F1; k <= Key.F24; k++)                    // 功能键
        {
            var n = (k - Key.F1 + 1).ToString();
            Register(k, "f" + n, "F" + n);
        }
        for (Key k = Key.NumPad0; k <= Key.NumPad9; k++)           // 小键盘数字
        {
            var n = (k - Key.NumPad0).ToString();
            Register(k, "num" + n, "Num" + n);
        }

        Register(Key.Up, "up", "↑");
        Register(Key.Down, "down", "↓");
        Register(Key.Left, "left", "←");
        Register(Key.Right, "right", "→");

        Register(Key.Space, "space", "Space");
        Register(Key.Enter, "enter", "Enter");
        Register(Key.Tab, "tab", "Tab");
        Register(Key.Escape, "esc", "Esc");
        Register(Key.Back, "backspace", "Backspace");
        Register(Key.Delete, "delete", "Delete");
        Register(Key.Insert, "insert", "Insert");
        Register(Key.Home, "home", "Home");
        Register(Key.End, "end", "End");
        Register(Key.PageUp, "pageup", "PageUp");
        Register(Key.PageDown, "pagedown", "PageDown");

        Register(Key.OemMinus, "minus", "-");
        Register(Key.OemPlus, "equal", "=");
        Register(Key.OemComma, "comma", ",");
        Register(Key.OemPeriod, "period", ".");
        Register(Key.OemQuestion, "slash", "/");
        Register(Key.OemTilde, "backquote", "`");
        Register(Key.OemOpenBrackets, "bracketleft", "[");
        Register(Key.OemCloseBrackets, "bracketright", "]");
        Register(Key.OemPipe, "backslash", "\\");
        Register(Key.OemSemicolon, "semicolon", ";");
        Register(Key.OemQuotes, "quote", "'");

        Register(Key.Add, "numadd", "Num+");
        Register(Key.Subtract, "numsubtract", "Num-");
        Register(Key.Multiply, "nummultiply", "Num*");
        Register(Key.Divide, "numdivide", "Num/");
        Register(Key.Decimal, "numdecimal", "Num.");
    }

    static void Register(Key key, string token, string display)
    {
        sKeyToToken[key] = token;
        sTokenToKey[token] = key;
        sKeyDisplay[key] = display;
    }

    // 该键是否在收录范围内（录制侧据此拦截：不在表内的冷僻键不可绑，绝不回退到 Avalonia 名）。
    public static bool IsSupported(Key key) => sKeyToToken.ContainsKey(key);

    // 序列化为存储字符串 "ctrl+shift+a"；键未收录返回 null（不写盘）。
    public static string? Serialize(KeyBinding b)
    {
        if (!sKeyToToken.TryGetValue(b.Key, out var keyToken))
            return null;
        var sb = new StringBuilder();
        if (b.Modifiers.HasFlag(KeyModifiers.Control)) sb.Append("ctrl+");
        if (b.Modifiers.HasFlag(KeyModifiers.Alt)) sb.Append("alt+");
        if (b.Modifiers.HasFlag(KeyModifiers.Shift)) sb.Append("shift+");
        if (b.Modifiers.HasFlag(KeyModifiers.Meta)) sb.Append("cmd+");
        sb.Append(keyToken);
        return sb.ToString();
    }

    public static bool TryParse(string? s, out KeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var parts = s.Trim().ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        var mods = KeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i])
            {
                case "ctrl": mods |= KeyModifiers.Control; break;
                case "alt": mods |= KeyModifiers.Alt; break;
                case "shift": mods |= KeyModifiers.Shift; break;
                case "cmd": mods |= KeyModifiers.Meta; break;
                default: return false;   // 未知修饰令牌
            }
        }
        if (!sTokenToKey.TryGetValue(parts[^1], out var key))
            return false;   // 未知键令牌
        binding = new KeyBinding(key, mods);
        return true;
    }

    // 面向用户的显示串。修饰按平台出字形（Mac ⌃⌥⇧⌘、Win Ctrl+/Alt+/Shift+/Win+），键走显示表。
    public static string ToDisplay(KeyBinding b)
    {
        bool mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        var sb = new StringBuilder();
        if (mac)
        {
            if (b.Modifiers.HasFlag(KeyModifiers.Control)) sb.Append('⌃');
            if (b.Modifiers.HasFlag(KeyModifiers.Alt)) sb.Append('⌥');
            if (b.Modifiers.HasFlag(KeyModifiers.Shift)) sb.Append('⇧');
            if (b.Modifiers.HasFlag(KeyModifiers.Meta)) sb.Append('⌘');
        }
        else
        {
            if (b.Modifiers.HasFlag(KeyModifiers.Control)) sb.Append("Ctrl+");
            if (b.Modifiers.HasFlag(KeyModifiers.Alt)) sb.Append("Alt+");
            if (b.Modifiers.HasFlag(KeyModifiers.Shift)) sb.Append("Shift+");
            if (b.Modifiers.HasFlag(KeyModifiers.Meta)) sb.Append("Win+");
        }
        sb.Append(KeyDisplay(b.Key, mac));
        return sb.ToString();
    }

    static string KeyDisplay(Key key, bool mac)
    {
        if (mac)
        {
            switch (key)   // Mac 具名键用字形
            {
                case Key.Enter: return "⏎";
                case Key.Tab: return "⇥";
                case Key.Escape: return "⎋";
                case Key.Back: return "⌫";
                case Key.Delete: return "⌦";
                case Key.PageUp: return "⇞";
                case Key.PageDown: return "⇟";
                case Key.Home: return "↖";
                case Key.End: return "↘";
                case Key.Space: return "␣";
            }
        }
        return sKeyDisplay.TryGetValue(key, out var d) ? d : key.ToString();
    }
}
