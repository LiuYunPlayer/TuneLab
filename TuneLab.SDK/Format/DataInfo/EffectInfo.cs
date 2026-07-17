using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 一个效果器在工程里的序列化模型：类型 + 是否启用(bypass) + 参数 + 自动化。
// 效果器按声明顺序在所属 MidiPart 上构成串行处理链。
public class EffectInfo
{
    // 实例稳定标识（不透明字符串、永不复用；承诺作用域 = 所在 part 的 effect 链内唯一）：
    // 供持久化的对象间横向引用锚定身份（当前消费者 = 颤音影响表 VibratoInfo.AffectedEffectAutomations）。
    // 空 = 宿主构造时发号；非空 = 沿用（undo/复制/装载天然保持身份）。克隆进同一条链的操作须显式清空再插入。
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Map<string, AutomationInfo> Automations { get; set; } = new();
    // 声明分段轨（effect 暴露的可编辑分段曲线，同 pitch 形）：按轨 id 键。孤儿数据保留隐藏，按 map 现有内容整存。
    public Map<string, List<List<Point>>> PiecewiseAutomations { get; set; } = new();
    public PropertyObject Properties { get; set; } = PropertyObject.Empty;
}
