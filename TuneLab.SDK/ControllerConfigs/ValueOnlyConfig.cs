namespace TuneLab.SDK;

// 只传值：控件树里一个**没有编辑控件**的值字段。数据照常存储、照常传给合成（属性本来就整个传给合成），preset、撤销、脚本照常参与；
// 界面上不出现：侧栏不占行，不能钉进参数面板。任何控件（滑条、下拉框……、侧栏画布 CanvasConfig、时间轴画布 TimelineCanvasConfig）
// 不要控件时，都原位换成它。
// 用途：为兼容老数据保留声明（新版换了表达方式，旧数据在新数据没设过时仍要生效，又想让它进 preset、出现在给 agent 的参数说明里）、
// 只由脚本 / 导入写入的数据。
//
// 值语义与画布相同：**没有默认值**——声明里的默认值只是给宿主控件显示用的兜底，没有控件就用不着。没设过 = 不存值（读到 Null），
// 插件读值时自己补默认值；恢复默认 = 删掉存值；值的形状由插件定（宿主不解读，读取时须自行校验）。不实现 IValueConfig。
// 数组 / 列表里它所在的位置上是 Null（位置必须有值）。
// 独立声明通道（自动化轨、时间轴 lane 画布）不用它：那两种用自己 config 上的 ControllerVisibility.Never。
// 构造函数全封，只走静态工厂（与 SliderConfig 同款 ABI 理由：见 SliderConfig）。
public sealed class ValueOnlyConfig : IControllerConfig
{
    private ValueOnlyConfig() { }

    public static ValueOnlyConfig Create() => new();
}
