using System;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 画布全部事件参数的基类（宿主构造）。以后只加属性、不改既有属性。
public class CanvasEvent
{
    // 插件处理了这次事件就置 true：宿主不再做该事件的默认动作。默认 false = 交还宿主。
    // 宿主有默认动作的事件才读它（滚轮 = 滚动所在面板，键盘 = 冒泡给快捷键），其余事件置不置都一样；各事件的默认动作见其回调注释。
    public bool Handled { get; set; }

    internal CanvasEvent() { }
}

// 画布的鼠标事件参数。语义与宿主自己的组件一致：点击 / 双击由宿主判定，修饰键跨平台归一（macOS 的 Cmd 记作 Ctrl）。
public class CanvasMouseEvent : CanvasEvent
{
    // 画布内坐标（逻辑像素，左上角为原点）。按下后拖出画布时可以为负或超出尺寸。
    public Point Position { get; }
    public CanvasModifierKeys Modifiers { get; }

    internal CanvasMouseEvent(Point position, CanvasModifierKeys modifiers)
    {
        Position = position;
        Modifiers = modifiers;
    }
}

public sealed class CanvasMouseButtonEvent : CanvasMouseEvent
{
    public CanvasMouseButton Button { get; }
    // 按下事件：是否为双击的第二下。
    public bool IsDoubleClick { get; }
    // 抬起事件：抬起位置与按下位置相同（中间没有拖动）——即一次"点击"。
    public bool IsClick { get; }

    internal CanvasMouseButtonEvent(Point position, CanvasModifierKeys modifiers, CanvasMouseButton button, bool isDoubleClick, bool isClick)
        : base(position, modifiers)
    {
        Button = button;
        IsDoubleClick = isDoubleClick;
        IsClick = isClick;
    }
}

public sealed class CanvasWheelEvent : CanvasMouseEvent
{
    // 滚动量（向上 / 向左为正，一格约为 1）。
    public Point Delta { get; }

    internal CanvasWheelEvent(Point position, CanvasModifierKeys modifiers, Point delta)
        : base(position, modifiers)
    {
        Delta = delta;
    }
}

// 画布的键盘事件参数。画布有焦点时才收到（点一下画布即获得焦点；点别处、切走窗口即失去）。
// 只转发 CanvasKey 收录的键，其余键不经画布、照常冒泡（交给快捷键等）。不带文字输入（输入法 / 字符）。
public sealed class CanvasKeyEvent : CanvasEvent
{
    public CanvasKey Key { get; }
    public CanvasModifierKeys Modifiers { get; }
    // 按下事件：按住不放时系统自动重复发来的按下（重复的节奏与延迟由系统设置决定）。抬起事件恒为 false。
    public bool IsRepeat { get; }

    internal CanvasKeyEvent(CanvasKey key, CanvasModifierKeys modifiers, bool isRepeat)
    {
        Key = key;
        Modifiers = modifiers;
        IsRepeat = isRepeat;
    }
}
