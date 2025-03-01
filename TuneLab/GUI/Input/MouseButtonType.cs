﻿using Avalonia.Input;

namespace TuneLab.GUI.Input;

internal enum MouseButtonType
{
    Other,
    PrimaryButton,
    MiddleButton,
    SecondaryButton,
}

internal static class MouseButtonTypeExtension
{
    public static MouseButtonType ToMouseButtonType(this PointerUpdateKind kind)
    {
        return kind switch
        {
            PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => MouseButtonType.PrimaryButton,
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => MouseButtonType.SecondaryButton,
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => MouseButtonType.MiddleButton,
            _ => MouseButtonType.Other,
        };
    }
}
