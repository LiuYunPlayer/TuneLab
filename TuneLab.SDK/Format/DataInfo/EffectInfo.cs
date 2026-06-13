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
    public PropertyObject Properties { get; set; } = PropertyObject.Empty;
}
