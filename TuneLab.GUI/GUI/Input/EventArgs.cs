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
    public bool Handled { get; set; }
}

internal class MouseButtonEventArgs : MouseEventArgs
{
    public MouseButtonType MouseButtonType { get; set; }
}

internal class MouseDownEventArgs : MouseButtonEventArgs
{
    public bool IsDoubleClick { get; set; }
    // 置 true = 这次按下本组件不要：不留鼠标捕获（交给父元素），按下照常冒泡——这次拖动的移动与抬起都归外层。
    // 本组件随后收到一次补发的抬起（捕获丢失，见 Component.OnPointerCaptureLost）。
    public bool Declined { get; set; }
}
internal class MouseMoveEventArgs : MouseEventArgs { }
internal class MouseUpEventArgs : MouseButtonEventArgs { public bool IsClick { get; set; } }
internal class MouseEnterEventArgs : MouseEventArgs { }
internal class MouseLeaveEventArgs : MouseEventArgs { }

internal class WheelEventArgs : MouseEventArgs
{
    public Point Delta { get; set; }
}