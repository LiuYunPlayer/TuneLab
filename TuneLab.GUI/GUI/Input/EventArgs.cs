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
    // 置 true = 这次事件已被本组件消费，不再冒泡给外层（按下 / 移动 / 抬起 / 滚轮）。不置照旧冒泡。
    // 按下（与按着键时的移动）上它就是"认领这次拖动"：认领者收后续的移动与抬起（见 Container 的转交规则）。
    public bool Handled { get; set; }
}

internal class MouseButtonEventArgs : MouseEventArgs
{
    public MouseButtonType MouseButtonType { get; set; }
}

internal class MouseDownEventArgs : MouseButtonEventArgs
{
    public bool IsDoubleClick { get; set; }
}
internal class MouseMoveEventArgs : MouseEventArgs { }
internal class MouseUpEventArgs : MouseButtonEventArgs { public bool IsClick { get; set; } }
internal class MouseEnterEventArgs : MouseEventArgs { }
internal class MouseLeaveEventArgs : MouseEventArgs { }

internal class WheelEventArgs : MouseEventArgs
{
    public Point Delta { get; set; }
}