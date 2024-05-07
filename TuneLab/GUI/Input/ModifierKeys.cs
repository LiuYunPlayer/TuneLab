using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.GUI.Input;

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
}
