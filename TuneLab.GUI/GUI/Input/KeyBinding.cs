using System.Runtime.InteropServices;
using Avalonia.Input;

namespace TuneLab.GUI.Input;

// 一个快捷键手势 = 单键 + 物理修饰位（Avalonia 原生 KeyModifiers：Control/Alt/Shift/Meta 各自独立、不折叠）。
// 按平台走真实键：⌘=Meta、⌃=Control 可分，Mac 原生组合（如 ⌃⌘F）可表达。v1 一命令至多一手势。
// 存储令牌与显示符号统一由 KeyCodec 负责（与 Avalonia 枚举名解耦）。见 docs/keybinding-system.md §1.2、§1.3。
internal readonly record struct KeyBinding(Key Key, KeyModifiers Modifiers = KeyModifiers.None)
{
    // 「本平台主命令键」便利别名：Mac=Meta(⌘)、其余=Control。绝大多数命令的默认手势用它一行搞定
    // （new(Key.C, PrimaryModifier) → ⌘C / Ctrl+C 自动按平台），只有要区分 ⌘/⌃ 的才显式写物理修饰。
    public static readonly KeyModifiers PrimaryModifier =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? KeyModifiers.Meta : KeyModifiers.Control;

    // 只保留真正的修饰位（Control/Alt/Shift/Meta），滤掉 Avalonia 可能夹带的其它状态位。
    public const KeyModifiers ModifierMask = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta;

    // 面向用户的显示串（平台字形）。
    public string ToDisplayString() => KeyCodec.ToDisplay(this);
}
