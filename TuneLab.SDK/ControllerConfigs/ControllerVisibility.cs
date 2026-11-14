namespace TuneLab.SDK;

// 独立声明通道（自动化轨 AutomationConfig、时间轴 lane 画布 TimelineLaneCanvasConfig）的编辑控件出不出现。只管控件，不管数据：
// 三档下数据都照常存储、照常传给合成（就是已声明的数据）。控件 = 这一条的全部编辑入口：参数面板标签栏、侧栏的默认值滑条等。
// 前两档是初始状态，用户可以在标签栏里改（按声源记、跨工程）；Never 由插件定死，用户改不了。
// 控件树里的成员（属性）没有这个枚举：不要控件时原位换成 ValueOnlyConfig；时间轴画布只需要初始状态，用 TimelineCanvasConfig.ShownByDefault。
// 宿主遇到不认识的值按 ShownByDefault 处理（宁可多显示，不让数据悄悄失去入口）。
public enum ControllerVisibility
{
    // 默认：显示，用户可以隐藏。
    ShownByDefault = 0,
    // 默认隐藏，用户可以在标签栏的显示列表里打开。用途：高级参数（默认收起、需要时用户自己打开）。
    HiddenByDefault,
    // 只传值：从不出现任何控件。用途：为兼容老数据保留声明（新版换了表达方式，旧数据在新数据没设过时仍要生效）、
    // 只由脚本 / 导入写入的数据。
    Never,
}
