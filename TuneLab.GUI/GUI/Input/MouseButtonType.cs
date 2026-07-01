using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
