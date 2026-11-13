using System;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 画布 lane：一个**只在参数面板里、沿着时间线排布**的插件自绘值字段。每个声明了它的对象（note 级字段 = 每个 note，
// 音素级字段 = 每个音素）在参数面板里各占自己时间段上的一块：宽 = 这个对象的时间段，高 = 面板高度，都由宿主定。
// 侧栏里不在那儿编辑（只有一行摘要）；参数面板的标签栏里常驻（哪个 note 的声明里有它，标签栏就有它），不用用户去钉。
// 标签栏上一开始显不显示见 ShownByDefault：默认隐藏的、用户藏起来的，侧栏那一行仍在，点编辑按钮即把它显示出来。
// 不要控件（只传值）时原位换成 ValueOnlyConfig。
//
// 与 CanvasConfig（侧栏画布）怎么选：界面属于侧栏、与时间无关（或只按对象内的相对位置画）→ CanvasConfig；
// 必须与时间轴对齐、逐个对象编辑（如音素混合：曲线 + 符号 + 语言）→ 本类。同一个字段只能选一种。
// 侧栏可以多选，多个对象没有一个共同的时间段，时间换算在那里给不出来；本类永远只对应一个对象，所以 ICanvasLaneHost.TimeAxis 一定有。
//
// 值语义与 CanvasConfig 相同：没有默认值（没设过 = 不存值，插件读值时自己补）、恢复默认 = 删掉存值，preset、撤销照常参与；
// 值的形状由插件自定（宿主不解读），读取时须自行校验；
// 值在声明时由插件取出、被工厂捕获，宿主不把值读回给控件；实例的寿命 = 一次声明（契约见 CanvasConfig）。
// 可以在 note / 音素属性声明（GetNotePropertyConfig / GetPhonemePropertyConfigs）的任何位置：对象成员、定长数组 / 列表的元素、
// 键控容器的条目里，任意嵌套——参数面板里按路径各成一条 lane。侧栏在它的位置显示一行：字段名 + 摘要 + 编辑按钮（跳到参数面板）。
// 只有这两处有时间轴：放进 part / 效果器属性、扩展设置、输入表单（PromptInputs）里，宿主显示「此处不支持」、改不了它，并记一条警告。
// 构造函数全封，只走静态工厂 + With（与 SliderConfig 同款 ABI 理由：见 SliderConfig）。
public sealed class CanvasLaneConfig : IControllerConfig
{
    public Func<ICanvasLaneHost, ICanvasControl> CreateControl { get; private init; } = null!;

    // 标签栏按钮与焦点圆点的颜色（hex，"#RRGGBB" 或 "#AARRGGBB"）。与 AutomationConfig.Color 同一约定：不设 = 全透明 "#00000000" =
    // 不指定，宿主从调色板分配（确定性：按声明序、避开同一面板上已占用的颜色）；alpha 为 0 的颜色一律按"不指定"处理。
    public string Color { get; private set; } = "#00000000";

    // 侧栏那一行的摘要（插件自译），在声明时按当时的值算好——值一变就会重新声明，摘要随之更新；多选时显示什么也由插件定。
    // null = 宿主按"默认 / 已修改 / 多值"兜底（看有没有存值）。与 CanvasConfig.Summary 同义。
    public string? Summary { get; private set; }

    // 用户没设过时，这条 lane 在参数面板标签栏上是否显示（默认 true）。只是初始状态：用户在标签栏里显式显示 / 隐藏过就按用户的
    //（按声源记）。与 ControllerVisibility 的 ShownByDefault / HiddenByDefault 同义；lane 没有 Never 这一档——不要控件就换成 ValueOnlyConfig。
    public bool ShownByDefault { get; private set; } = true;

    private CanvasLaneConfig() { }
    CanvasLaneConfig Clone() => (CanvasLaneConfig)MemberwiseClone();

    public static CanvasLaneConfig Create(Func<ICanvasLaneHost, ICanvasControl> createControl)
    {
        ArgumentNullException.ThrowIfNull(createControl);
        return new() { CreateControl = createControl };
    }

    public CanvasLaneConfig WithColor(string color) { var c = Clone(); c.Color = color; return c; }
    public CanvasLaneConfig WithSummary(string summary) { var c = Clone(); c.Summary = summary; return c; }
    public CanvasLaneConfig WithShownByDefault(bool shown) { var c = Clone(); c.ShownByDefault = shown; return c; }
}
