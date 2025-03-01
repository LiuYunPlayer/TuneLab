using Avalonia.Input;
using System;
using System.Runtime.InteropServices;

namespace TuneLab.GUI.Input;

[Flags]
internal enum ModifierKeys : uint
{
    None = 0x0u,
    Ctrl = 0x1u,
    Alt = 0x2u,
    Shift = 0x4u,
}

internal static class KeyModifierExtension
{
    public static ModifierKeys ToModifierKeys(this KeyModifiers keyModifiers)
    {
        ModifierKeys modifierKeys = ModifierKeys.None;

        if ((uint)(keyModifiers & KeyModifiers.Alt) != 0)
            modifierKeys |= ModifierKeys.Alt;
        if ((uint)(keyModifiers & KeyModifiers.Control) != 0)
            modifierKeys |= ModifierKeys.Ctrl;
        if ((uint)(keyModifiers & KeyModifiers.Shift) != 0)
            modifierKeys |= ModifierKeys.Shift;
        if ((uint)(keyModifiers & KeyModifiers.Meta) != 0)
            modifierKeys |= ModifierKeys.Ctrl;

        return modifierKeys;
    }

    public static KeyModifiers ToAvalonia(this ModifierKeys modifierKeys)
    {
        KeyModifiers keyModifiers = KeyModifiers.None;

        if ((modifierKeys & ModifierKeys.Alt) != 0)
            keyModifiers |= KeyModifiers.Alt;
        if ((modifierKeys & ModifierKeys.Shift) != 0)
            keyModifiers |= KeyModifiers.Shift;
        if ((modifierKeys & ModifierKeys.Ctrl) != 0)
            keyModifiers |= RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? KeyModifiers.Control
                : KeyModifiers.Meta;

        return keyModifiers;
    }
}
