using System;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 画布：一个**由插件自己画界面的值字段**。宿主给出一块区域（宽度跟随面板、高度由本 config 声明），插件经 ICanvasDrawing
// 作画、经鼠标事件交互、经 ICanvasHost 把编辑结果写回本字段——适合官方控件表达不了的编辑（如音素内部的拉伸曲线）。
// 它就是一个普通的值字段（实现 IValueConfig）：恢复默认、preset、撤销、多选都照常参与；值的形状由插件自定（宿主不解读），
// 插件读取时须自行校验（撤销 / preset / 粘贴都可能带来任意形状的值）。
// 构造函数全封，只走静态工厂 + With（与 SliderConfig 同款 ABI 理由：见 SliderConfig）。
//
// 只能作为 ObjectConfig 的成员（任意嵌套深度，含列表元素里的对象）。
//
// 契约：
// · createControl 是工厂，**不在声明时调用**：宿主把画布放进面板时才调用。控件实例的寿命 = 一次声明：面板每次按新声明
//   更新（值提交后、外部改值后、选中变化……）都用新 config 的工厂重建控件，旧实例随即作废（它手里的 ICanvasHost 失效）。
//   有草稿期间（见 ICanvasHost.SetValue）面板不刷新、实例不会被换掉；没有草稿时实例随时可能被换掉，临时状态（悬停等）
//   要能从值与事件位置重新算出。
// · 工厂必须廉价（不做 IO、不做重计算）：选中变化、提交都会重建。控件只承载事件与绘制逻辑，重资源由插件自己在插件级缓存。
// · 与按钮同样的线程约定：控件的全部回调都在主线程上；插件别在回调里阻塞主线程。
public sealed class CanvasConfig : IValueConfig
{
    public PropertyValue DefaultValue { get; private init; }

    // 画布区域的首选高度（与 DPI 无关的逻辑像素）。宽度跟随面板。v1 即实际高度；将来加最小 / 最大 / 自适应高度时，
    // 它仍是没有其他约束时采用的高度。
    public double Height { get; private init; }

    public Func<ICanvasHost, ICanvasControl> CreateControl { get; private init; } = null!;

    // 字段收起时标题行的一行摘要：宿主用当前值调用（插件自译）。值整体不一致（多选）时宿主直接显示多值标记、不调用。
    // null = 宿主按"默认 / 已修改"兜底。
    public Func<PropertyValue, string?>? Summary { get; private set; }

    private CanvasConfig() { }
    CanvasConfig Clone() => (CanvasConfig)MemberwiseClone();

    public static CanvasConfig Create(PropertyValue defaultValue, double height, Func<ICanvasHost, ICanvasControl> createControl)
    {
        ArgumentNullException.ThrowIfNull(createControl);
        if (!(height > 0))
            throw new ArgumentOutOfRangeException(nameof(height), "Canvas height must be positive.");
        return new() { DefaultValue = defaultValue, Height = height, CreateControl = createControl };
    }

    public CanvasConfig WithSummary(Func<PropertyValue, string?>? summary) { var c = Clone(); c.Summary = summary; return c; }
}
