using System;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 画布：一个**由插件自己画界面的值字段**。宿主给出一块区域（大小由宿主定，参考本 config 的首选尺寸），插件经 ICanvasDrawing
// 作画、经鼠标事件交互、经 ICanvasHost 把编辑结果写回本字段——适合官方控件表达不了的编辑（如音素内部的拉伸曲线）。
// 它就是一个普通的值字段（实现 IValueConfig）：恢复默认、preset、撤销、多选都照常参与；值的形状由插件自定（宿主不解读），
// 插件读取时须自行校验（撤销 / preset / 粘贴都可能带来任意形状的值）。
// 构造函数全封，只走静态工厂 + With（与 SliderConfig 同款 ABI 理由：见 SliderConfig）。
//
// 只能作为 ObjectConfig 的成员（任意嵌套深度，含列表元素里的对象）。
//
// 契约：
// · 值由插件在声明时取：声明 context 带着每个选中对象各自的值（未设过的读不到 = 默认值），多选怎么合并、怎么呈现由插件定；
//   取出的值让工厂捕获、交给控件。宿主不把值读回给控件（ICanvasHost 没有读值的接口）。
// · createControl 是工厂，**不在声明时调用**：宿主把画布放进面板时才调用。控件实例的寿命 = 一次声明：工程里的值一变
//  （提交、撤销、别处改了同一份数据），当前实例就作废——不再收到事件、写回不生效，只留它最后画出的一帧撑到新声明到达；
//   面板按新声明更新（值变了之后、选中变化……）时用新 config 的工厂建新实例。所以一个实例的生命周期内工程里的值不变。
//   插件宣告编辑期间（ICanvasHost.BeginEdit / EndEdit）或有草稿、弹出期间面板不刷新、实例不会被换掉；否则实例随时可能
//   被换掉，临时状态（悬停等）要能从值与事件位置重新算出。
// · 工厂必须廉价（不做 IO、不做重计算）：选中变化、提交都会重建。控件只承载事件与绘制逻辑，重资源由插件自己在插件级缓存。
// · 与按钮同样的线程约定：控件的全部回调都在主线程上；插件别在回调里阻塞主线程。
public sealed class CanvasConfig : IValueConfig
{
    public PropertyValue DefaultValue { get; private init; }

    // 插件心目中的最佳尺寸（与 DPI 无关的逻辑像素）。只是期望：各承载位置按自己的约束取用，实际尺寸以 ICanvasHost.Width / Height
    // 为准、随时可能变，控件须按实际尺寸适配。侧栏宽度跟随面板、只取首选高度；没有外部约束的位置宽高都取。
    // 眼下没有位置取宽度，但它同样是契约的一部分——以后出现这样的位置时，宿主就用这里给的值。
    public double PreferredWidth { get; private init; }
    public double PreferredHeight { get; private init; }

    public Func<ICanvasHost, ICanvasControl> CreateControl { get; private init; } = null!;

    // 字段收起时标题行的一行摘要（插件自译），在声明时按当时的值算好——值一变就会重新声明，摘要随之更新。
    // 多选不一致时显示什么也由插件定。null = 宿主按"默认 / 已修改 / 多值"兜底。
    public string? Summary { get; private set; }

    private CanvasConfig() { }
    CanvasConfig Clone() => (CanvasConfig)MemberwiseClone();

    public static CanvasConfig Create(PropertyValue defaultValue, double preferredWidth, double preferredHeight, Func<ICanvasHost, ICanvasControl> createControl)
    {
        ArgumentNullException.ThrowIfNull(createControl);
        if (!(preferredWidth > 0 && double.IsFinite(preferredWidth)))
            throw new ArgumentOutOfRangeException(nameof(preferredWidth), "Canvas preferred width must be a positive finite number.");
        if (!(preferredHeight > 0 && double.IsFinite(preferredHeight)))
            throw new ArgumentOutOfRangeException(nameof(preferredHeight), "Canvas preferred height must be a positive finite number.");
        // 默认值就是"未设"状态，也定了字段的类型（存值类型不同按默认处理）：Null 作默认 = 字段永远存不进值；
        // Multiple 只是多选读出时"各对象不一致"的标记、绝不入存储。两者都是写错了，当场报出来。
        if (defaultValue.IsNull() || defaultValue.IsMultiple())
            throw new ArgumentException("A canvas default value must be a real value: it is the unset state and fixes the field's type (an empty object {} is a good \"unset\"). Null means no value, and Multiple only marks differing values when reading a multi-selection.", nameof(defaultValue));
        return new() { DefaultValue = defaultValue, PreferredWidth = preferredWidth, PreferredHeight = preferredHeight, CreateControl = createControl };
    }

    public CanvasConfig WithSummary(string summary) { var c = Clone(); c.Summary = summary; return c; }
}
