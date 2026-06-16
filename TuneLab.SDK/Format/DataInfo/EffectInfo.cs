using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 一个效果器在工程里的序列化模型：类型 + 是否启用(bypass) + 参数 + 自动化。
// 效果器按声明顺序在所属 MidiPart 上构成串行处理链。
public class EffectInfo
{
    public string Type { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Map<string, AutomationInfo> Automations { get; set; } = new();
    // 声明分段轨（effect 暴露的可编辑分段曲线，同 pitch 形）：按轨 id 键。孤儿数据保留隐藏，按 map 现有内容整存。
    public Map<string, List<List<Point>>> PiecewiseAutomations { get; set; } = new();
    public PropertyObject Properties { get; set; } = PropertyObject.Empty;
}
