using System;
using System.Collections.Generic;
using TuneLab.SDK;

namespace TuneLab.Data;

// 一条 lane 画的是什么：数值（滑条 / 有界数字框，宿主画值段、拖写）还是插件画布（每个对象一块插件自绘的界面）。
internal enum LaneKind
{
    Number,
    // 用户从侧栏钉进参数面板的侧栏画布（CanvasConfig）。
    Canvas,
    // 插件声明的时间轴画布（TimelineCanvasConfig）：常驻，不经钉选。
    TimelineCanvas,
}

// 参数面板 lane 的一条呈现快照（宿主消费形态）：值轴标度（量程 + 位置↔值映射，吸附标度即绘制吸附）
// /默认值/显示格式/量程端点描述文本 + 宿主分配的轨色。lane 是「参数面板钉选」当前各种类的呈现形式
//（钉选存储见 TuneLab.Configs.ParameterPinning，未来非 lane 形式的新种类另立自己的呈现快照）。
// 由 MidiPart 按「各 note 声明的并集」物化（钉选的数值字段与侧栏画布 + 常驻的时间轴画布，见 MidiPart.RebuildLaneConfigs），
// 值本体在各 note.Properties / phoneme.Properties 里按 id 存取，本 entry 只带呈现所需的口径。
// 画布条目（Kind != Number）没有值轴：Scale / DefaultValue / Format / 端点文本都不用，只有轨色与名字。
// Resolved=false 是「未解析占位」：钉选在场但声明面暂不可得（如 phoneme lane 在合成音素产出前的信息真空）——
// tab 照常显示（lane 的存在性跟钉选意图走），面板不画段、不可编辑、不显上下界；声明可得后升级为已解析。
internal readonly record struct LaneEntry(INormalizedScale Scale, double DefaultValue, INumberFormat? Format, string? MinLabel, string? MaxLabel, string Color, bool Resolved = true, LaneKind Kind = LaneKind.Number)
{
    // 字段在声明树里的路径各段的标签名（见 LanePath.Leaf）：标签栏的名字按它算（最短不重名）。null = 顶层占位条目（只有 id）。
    public IReadOnlyList<LanePath.LabelPart>? Label { get; init; }

    // 量程由标度定义，沿用 config 家族同一换算（约定标度单调递增）。
    public double MinValue => Scale.ToValue(0);
    public double MaxValue => Scale.ToValue(1);

    public bool IsNumber => Kind == LaneKind.Number;

    // 从 config 提取有界数值形态（值轴标度/默认值/格式/端点描述文本，即 LaneEntry 去轨色的部分——Color 由钉选存储补足）；
    // 非数值/无界返回 false（无 lane 资格）。lane 资格 = 有界数值：SliderConfig（恒双界）或设了双界的
    // DraggableNumberBoxConfig——面板表面吃具体 config 类型（作者呈现意图），跨表面功能只吃这里抽出的值形态口径。
    public static bool TryGetBoundedNumber(IControllerConfig config, out LaneEntry shape)
    {
        switch (config)
        {
            case SliderConfig slider:
                shape = new LaneEntry(slider.Scale, slider.DefaultValue,
                    slider.Format, slider.MinLabel, slider.MaxLabel, string.Empty);
                return true;
            case DraggableNumberBoxConfig box when box.Min.HasValue && box.Max.HasValue:
                shape = new LaneEntry(BoxScale(box.Min.Value, box.Max.Value, box.Step), box.DefaultValue,
                    box.Format, null, null, string.Empty);
                return true;
            default:
                shape = default;
                return false;
        }
    }

    // 能被用户钉进参数面板的字段：有界数值，或侧栏画布。时间轴画布不在此列——它本就常驻，不用钉。
    public static bool TryGetPinnable(IControllerConfig config, out LaneEntry shape)
    {
        if (TryGetBoundedNumber(config, out shape))
            return true;
        if (config is CanvasConfig)
        {
            shape = Canvas(LaneKind.Canvas, string.Empty);
            return true;
        }
        shape = default;
        return false;
    }

    public static bool IsPinnable(IControllerConfig config) => TryGetPinnable(config, out _);

    // 画布条目：没有值轴，只带种类与轨色。
    public static LaneEntry Canvas(LaneKind kind, string color)
        => new(NormalizedScale.Linear(0, 1), 0, null, null, null, color, Resolved: true, Kind: kind);

    // DraggableNumberBox 的值轴：线性量程 + 按 Step 吸附（与控件本体的吸附语义同口径；吸附后钳回量程，
    // 防边界非步长整数倍时越界）。Step 为空 = 连续。
    static INormalizedScale BoxScale(double min, double max, double? step)
    {
        var linear = NormalizedScale.Linear(min, max);
        if (step is not { } s || s <= 0)
            return linear;

        return NormalizedScale.Custom(
            normalized => Math.Clamp(Math.Round(linear.ToValue(normalized) / s) * s, min, max),
            linear.ToNormalized);
    }
}
