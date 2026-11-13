using System;

namespace TuneLab.SDK;

// 时间轴画布：一条**贯穿整个 part 的插件自绘轨**（区段、帧、歌词块、插件自己的曲线……宿主不解读），与自动化轨平级。
// 由引擎的 GetTimelineCanvasConfigs 声明（声源 / 乐器 / 效果器都有），id 平铺；参数面板的标签栏里跟在所属对象的自动化轨后面，
// 激活时整个参数面板都是这块画布。
//
// 与 CanvasLaneConfig 怎么选：这是某个音符 / 音素的属性 → CanvasLaneConfig（每个对象各占自己那一段、跟着对象走）；
// 这是一条贯穿整个 part 的轨 → 本类。
//
// 时间：定义域是整条时间轴——局部 tick（相对 part）可以为负、可以超出 part，part 裁剪外照样能画能编辑（同自动化轨）。
// 画布铺满整个参数面板（Width / Height = 面板尺寸，画布坐标 = 面板坐标），内容按 TimeAxis / PitchAxis 的换算摆。值里存 tick 就存局部 tick
//（TimeAxis.TickToX(0) 是原点），宿主在拆分、合并时自己维护原点，插件无感。
//
// 值：一个 PropertyValue 整值，形状由插件自定（宿主不解读，读取时须自行校验）；**没有默认值**，没设过 = 不存值（读到 Null）。
// 值从声明上下文取（各声明视图的 TimelineCanvases，按 id），由工厂捕获交给控件；实例寿命 = 一次声明，与 CanvasConfig 同一套契约。
// 提交时说明改了哪几段：ITimelineCanvasHost.CommitValue(modifiedTickRanges)；整条替换时用不带参数的 CommitValue()。
// 合成侧经上下文的 TimelineCanvases 拿到同一份值（ISynthesisTimelineCanvas）。
//
// 孤儿数据：从声明里消失的 id，值照样存着、写进工程，只是不显示、不给合成；重新声明（如换回原来的声源）即原样恢复。
// 与自动化轨不强制不重名（两族各存各的）。
// part 上的操作（第一版）：移动 part 什么都不用改；拆分 = 左右各拿一份完整的值（各显示自己那段）；合并 = 保留按时间顺序第一个有该 id 的 part 的值；
// 选区复制粘贴不带画布值；part 预设整值带上它（偏移 + 值，用户自己管理预设里存的是什么）。
// 构造函数全封，只走静态工厂 + With（与 SliderConfig 同款 ABI 理由：见 SliderConfig）。
public sealed class TimelineCanvasConfig
{
    // 控件工厂。空壳（无参 Create）没有工厂，为 null——此时 Visibility 恒为 Never，宿主不会调它。
    public Func<ITimelineCanvasHost, ICanvasControl>? CreateControl { get; private init; }

    // 标签栏按钮与焦点圆点的颜色（hex，"#RRGGBB" 或 "#AARRGGBB"）。与 AutomationConfig.Color 同一约定：不设 = 全透明 "#00000000" =
    // 不指定，宿主从调色板分配；alpha 为 0 的颜色一律按"不指定"处理。
    public string Color { get; private set; } = "#00000000";

    // 编辑控件出不出现，与 AutomationConfig.Visibility 同义（见 ControllerVisibility）。三档下值都照常存储、照常传给合成。
    public ControllerVisibility Visibility { get; private set; } = ControllerVisibility.ShownByDefault;

    private TimelineCanvasConfig() { }
    TimelineCanvasConfig Clone() => (TimelineCanvasConfig)MemberwiseClone();

    public static TimelineCanvasConfig Create(Func<ITimelineCanvasHost, ICanvasControl> createControl)
    {
        ArgumentNullException.ThrowIfNull(createControl);
        return new() { CreateControl = createControl };
    }

    // 只传值的空壳：没有控件工厂，可见性固定为 Never——只声明"这个 id 上有一份整值，存着并传给合成"。
    // 用途：新版不再编辑、但合成还要读的旧画布值。
    public static TimelineCanvasConfig Create()
        => new() { Visibility = ControllerVisibility.Never };

    public TimelineCanvasConfig WithColor(string color) { var c = Clone(); c.Color = color; return c; }

    // 空壳（无参 Create）只能是 Never：设成要显示时抛参数异常——没有工厂就没有界面。
    public TimelineCanvasConfig WithVisibility(ControllerVisibility visibility)
    {
        if (CreateControl == null && visibility != ControllerVisibility.Never)
            throw new ArgumentException("A TimelineCanvasConfig created by Create() has no control factory; its visibility can only be Never. Use Create(createControl) for a canvas the user can see.", nameof(visibility));
        var c = Clone(); c.Visibility = visibility; return c;
    }
}
