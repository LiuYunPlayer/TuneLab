using System;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 时间轴画布：一个**只在参数面板里、沿着时间线排布**的插件自绘值字段。每个声明了它的对象（note 级字段 = 每个 note，
// 音素级字段 = 每个音素）在参数面板里各占自己时间段上的一块：宽 = 这个对象的时间段，高 = 面板高度，都由宿主定。
// 侧栏里不在那儿编辑（只有一行摘要）；参数面板的标签栏里常驻（哪个 note 的声明里有它，标签栏就有它），不用用户去钉。
//
// 与 CanvasConfig（侧栏画布）怎么选：界面属于侧栏、与时间无关（或只按对象内的相对位置画）→ CanvasConfig；
// 必须与时间轴对齐、逐个对象编辑（如音素混合：曲线 + 符号 + 语言）→ 本类。同一个字段只能选一种。
// 侧栏可以多选，多个对象没有一个共同的时间段，时间换算在那里给不出来；本类永远只对应一个对象，所以 ITimelineCanvasHost.TimeAxis 一定有。
//
// 值语义与 CanvasConfig 相同：恢复默认、preset、撤销照常参与；值的形状由插件自定（宿主不解读），读取时须自行校验；
// 值在声明时由插件取出、被工厂捕获，宿主不把值读回给控件；实例的寿命 = 一次声明（契约见 CanvasConfig）。
// 可以在 note / 音素属性声明（GetNotePropertyConfig / GetPhonemePropertyConfigs）的任何位置：对象成员、定长数组 / 列表的元素、
// 键控容器的条目里，任意嵌套——参数面板里按路径各成一条 lane。侧栏在它的位置显示一行：字段名 + 摘要 + 编辑按钮（跳到参数面板）。
// 只有这两处有时间轴：放进 part / 效果器属性、扩展设置、输入表单（PromptInputs）里，宿主显示「此处不支持」、改不了它，并记一条警告。
// 构造函数全封，只走静态工厂 + With（与 SliderConfig 同款 ABI 理由：见 SliderConfig）。
public sealed class TimelineCanvasConfig : IValueConfig
{
    public PropertyValue DefaultValue { get; private init; }

    public Func<ITimelineCanvasHost, ICanvasControl> CreateControl { get; private init; } = null!;

    // 标签栏按钮与焦点圆点的颜色（hex，"#RRGGBB" 或 "#AARRGGBB"）。与 AutomationConfig.Color 同一约定：不设 = 全透明 "#00000000" =
    // 不指定，宿主从调色板分配（确定性：按声明序、避开同一面板上已占用的颜色）；alpha 为 0 的颜色一律按"不指定"处理。
    public string Color { get; private set; } = "#00000000";

    // 侧栏那一行的摘要（插件自译），在声明时按当时的值算好——值一变就会重新声明，摘要随之更新；多选时显示什么也由插件定。
    // null = 宿主按"默认 / 已修改 / 多值"兜底。与 CanvasConfig.Summary 同义。
    public string? Summary { get; private set; }

    private TimelineCanvasConfig() { }
    TimelineCanvasConfig Clone() => (TimelineCanvasConfig)MemberwiseClone();

    public static TimelineCanvasConfig Create(PropertyValue defaultValue, Func<ITimelineCanvasHost, ICanvasControl> createControl)
    {
        ArgumentNullException.ThrowIfNull(createControl);
        // 与 CanvasConfig 同一条规矩：默认值就是"未设"状态，也定了字段的类型。
        if (defaultValue.IsNull() || defaultValue.IsMultiple())
            throw new ArgumentException("A timeline canvas default value must be a real value: it is the unset state and fixes the field's type (an empty object {} is a good \"unset\"). Null means no value, and Multiple only marks differing values when reading a multi-selection.", nameof(defaultValue));
        return new() { DefaultValue = defaultValue, CreateControl = createControl };
    }

    public TimelineCanvasConfig WithColor(string color) { var c = Clone(); c.Color = color; return c; }
    public TimelineCanvasConfig WithSummary(string summary) { var c = Clone(); c.Summary = summary; return c; }
}
