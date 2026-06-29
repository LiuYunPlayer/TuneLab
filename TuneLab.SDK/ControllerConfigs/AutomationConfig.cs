using TuneLab.Foundation;

namespace TuneLab.SDK;

// 自动化轨配置（voice 与 effect 共用）。一个类同时表达两种形态，由 DefaultValue 区分：
//   · 连续型：DefaultValue 为实数（经 WithDefault 设定）——处处有值、以此为默认基线（如音量）。
//   · 分段型：DefaultValue 为 NaN（默认、不调 WithDefault）——无默认基线、段间取值 NaN（如 pitch 类），按分段渲染/编辑。
// NaN 表"无基线"与本 SDK 既有"NaN 表空"求值约定同源；作者在同一张有序 map 里自由穿插两种轨、声明序即呈现序。
// 刻意不继承 SliderConfig：冻结面上解耦优先于 DRY。构造函数全封，只走静态工厂 + With。
public sealed class AutomationConfig : IValueConfig<double>
{
    // 默认 NaN = 分段轨；经 WithDefault 给实数 → 连续轨。
    public double DefaultValue { get; private set; } = double.NaN;

    // 值轴标度——自动化上下界即 slider 两端，与滑条共用 INormalizedScale。当前只经线性工厂构造（渲染器按线性算）。
    public INormalizedScale Scale { get; private set; } = null!;

    // 轨曲线渲染色（hex）。不设给中性默认——但同一面板多轨应各设其色以免撞色。
    public string Color { get; private set; } = "#888888";

    // 数值显示/回读格式；null = 宿主默认。
    public INumberFormat? Format { get; private set; }

    // 量程两端的描述文本（如 min="Female"、max="Male"），显示在参数面板上下界处。可选、由插件按当前语言自译、可只设一端。
    public string? MinLabel { get; private set; }
    public string? MaxLabel { get; private set; }

    // 可随机：UI 默认值面板（滑条）右侧给随机入口，在量程内重新取值。
    public bool Randomizable { get; private set; }

    // 量程由标度定义，沿用 slider 同一换算（约定标度单调递增）。
    public double MinValue => Scale.ToValue(0);
    public double MaxValue => Scale.ToValue(1);

    // 分段（间断、无默认基线）轨 = DefaultValue 为 NaN；连续轨为实数。
    public bool IsPiecewise => double.IsNaN(DefaultValue);

    private AutomationConfig() { }
    AutomationConfig Clone() => (AutomationConfig)MemberwiseClone();

    // 量程即必需项；默认分段（NaN 基线），按需 WithDefault 转连续。
    public static AutomationConfig Create(double minValue, double maxValue)
        => new() { Scale = NormalizedScale.Linear(minValue, maxValue) };

    // 设默认基线 → 连续轨（传 NaN 仍为分段）。
    public AutomationConfig WithDefault(double value) { var c = Clone(); c.DefaultValue = value; return c; }
    public AutomationConfig WithColor(string color) { var c = Clone(); c.Color = color; return c; }
    public AutomationConfig WithFormat(INumberFormat format) { var c = Clone(); c.Format = format; return c; }
    // 量程两端描述文本（插件自译，可只设一端）。
    public AutomationConfig WithMinLabel(string label) { var c = Clone(); c.MinLabel = label; return c; }
    public AutomationConfig WithMaxLabel(string label) { var c = Clone(); c.MaxLabel = label; return c; }
    public AutomationConfig WithRandomizable(bool value = true) { var c = Clone(); c.Randomizable = value; return c; }

    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
