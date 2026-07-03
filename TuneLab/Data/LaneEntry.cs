using TuneLab.SDK;

namespace TuneLab.Data;

// 参数面板 lane 的一条呈现快照（宿主消费形态）：有界数值的值域/默认值/显示格式/量程端点描述文本
// + 宿主分配的轨色。lane 是「参数面板钉选」当前各种类的呈现形式（钉选存储见 TuneLab.Configs.ParameterPinning，
// 未来非 lane 形式的新种类另立自己的呈现快照）。由 MidiPart 按「钉选集合 ∩ 当前 note/phoneme 属性 config 中的
// 有界数值条目」物化（见 MidiPart.RebuildPinnedLaneConfigs），值本体在各 note.Properties / phoneme.Properties
// 里按 id 存取，本 entry 只带呈现所需的量程口径。
// Resolved=false 是「未解析占位」：钉选在场但声明面暂不可得（如 phoneme lane 在合成音素产出前的信息真空）——
// tab 照常显示（lane 的存在性跟钉选意图走），面板不画段、不可编辑、不显上下界；声明可得后升级为已解析。
internal readonly record struct LaneEntry(double MinValue, double MaxValue, double DefaultValue, INumberFormat? Format, string? MinLabel, string? MaxLabel, string Color, bool Resolved = true)
{
    // 从 config 提取有界数值形态（量程/默认值/格式/端点描述文本，即 LaneEntry 去轨色的部分——Color 由钉选存储补足）；
    // 非数值/无界返回 false（无 lane 资格）。lane 资格 = 有界数值：SliderConfig（恒双界）或设了双界的
    // DraggableNumberBoxConfig——面板表面吃具体 config 类型（作者呈现意图），跨表面功能只吃这里抽出的值形态口径。
    public static bool TryGetBoundedNumber(IControllerConfig config, out LaneEntry shape)
    {
        switch (config)
        {
            case SliderConfig slider:
                shape = new LaneEntry(slider.Scale.ToValue(0), slider.Scale.ToValue(1), slider.DefaultValue,
                    slider.Format, slider.MinLabel, slider.MaxLabel, string.Empty);
                return true;
            case DraggableNumberBoxConfig box when box.Min.HasValue && box.Max.HasValue:
                shape = new LaneEntry(box.Min.Value, box.Max.Value, box.DefaultValue,
                    box.Format, null, null, string.Empty);
                return true;
            default:
                shape = default;
                return false;
        }
    }
}
