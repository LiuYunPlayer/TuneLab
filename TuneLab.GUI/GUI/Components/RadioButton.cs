using Avalonia.Media;
using TuneLab.GUI.Controllers;

namespace TuneLab.GUI.Components;

// 单选样式的开关：与 CheckBox 同结构（继承 Toggle，三层视觉：框 / 高亮底 / 记号），只把方框换成圆、把勾换成圆点。
// 用途 = 让"只能选一个"在视觉上一眼可辨（圆点 vs CheckBox 的方框），这是通用惯例。
//
// 【互斥不在这里】本控件只承担视觉与自身布尔态，分组逻辑归调用方——与仓库既有范式一致：
// FunctionBar 的钢琴工具、ParameterTabBar 的参数 tab 都是「点击只写共享数据源 → 全体按数据源重刷」，
// 控件自己从不知道同伴是谁。塞一个 group 进控件会另立一套机制，反而与那两处分家。
//
// 【是否允许点掉由调用方决定】需要"总得选中一个"（如工具栏）就自己接 AllowSwitch += () => !IsChecked（见 FunctionBar）；
// 需要"可以一个都不选"就什么都不接。本控件不预设，因为两种需求都真实存在。
internal class RadioButton : Toggle, IDataValueController<bool>
{
    public RadioButton()
    {
        Width = 16;
        Height = 16;
        AddContent(new() { Item = new IconItem() { Icon = Assets.RadioFrame }, UncheckedColorSet = new() { Color = Colors.White } });
        // 16×16 上 CornerRadius=8 即整圆，故高亮底不需要额外图标资源。
        AddContent(new() { Item = new BorderItem() { CornerRadius = 8 }, CheckedColorSet = new() { Color = Style.HIGH_LIGHT } });
        AddContent(new() { Item = new IconItem() { Icon = Assets.RadioDot }, CheckedColorSet = new() { Color = Colors.White } });
    }
}
