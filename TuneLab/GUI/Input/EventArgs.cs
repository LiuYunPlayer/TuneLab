using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.GUI.Input;

internal class EventArgs
{

}

internal class MouseEventArgs : EventArgs
{
    public ModifierKeys KeyModifiers { get; set; }
    public Point Position { get; set; }
}

internal class MouseButtonEventArgs : MouseEventArgs
{
    public MouseButtonType MouseButtonType { get; set; }
}

internal class MouseDownEventArgs : MouseButtonEventArgs { public bool IsDoubleClick { get; set; } }
internal class MouseMoveEventArgs : MouseEventArgs { }
internal class MouseUpEventArgs : MouseButtonEventArgs { public bool IsClick { get; set; } }
internal class MouseEnterEventArgs : MouseEventArgs { }

internal class WheelEventArgs : MouseEventArgs
{
    public Point Delta { get; set; }
}