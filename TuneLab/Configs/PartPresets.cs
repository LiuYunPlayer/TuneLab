using System;
using System.Collections.Generic;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;   // ControllerConfigDefaults.GetDefaultValue（config → 默认值的递归求值，与属性面板共用）
using TuneLab.SDK;

namespace TuneLab.Configs;

// 【把一条 part preset 用到 part 上 / 从 part 抓一条】的收口。存储那一半在 PresetConfigManager
//（文件怎么放、名字怎么校验），这里是数据那一半：读写 IMidiPart。
//
// 【为什么单拎出来】两个入口按下去必须是同一件事——侧栏 Part 面板的预设行，与命令面的
// `preset apply` / `preset save`。preset 的语义细到"哪些字段进快照、默认值算不算声音、
// 换引擎后剩下的键留不留"，各写一遍必然分叉，而分叉的表现是同一条 preset 从两条路应用出不同的声音。
// 同 ParameterPinning.SetPinned 的理由。
internal static class PartPresets
{
    // 一次应用动了多少：**当前声源声明的**属性叶子数与自动化轨数（多 part 时累加）。
    // 【为什么要报出来】声源声明了 0 个属性时（换过引擎的 part 上很常见），"恢复默认"其实一个字段都没碰，
    // 而回一句笼统的"已重置"就是假装成功——实测里正是这一点让人以为重置坏掉了。
    internal readonly record struct Applied(int Properties, int Automations);

    // 应用到一批 part：preset = null 即界面上那条「None」（恢复声源自己的默认值）。
    // 扇出到全部目标后**归为一个撤销步**（共享文档，commit 一次），故多选应用与单选一样能一次撤销。
    // 多选混源亦可——apply 会统一各 part 的音源。
    //
    // 【不动声源的那一支】preset = null 只重置参数，**不碰声源**：没有一个说得通的"默认声源"可回
    //（清空会让 part 哑掉，而"新建时用的那个"是最近使用、属会话状态）；声源是用户在面板上单独选的东西，
    // 预设只是顺带把它一起打包。"撤销刚才那次应用"的正解是撤销，不是这一支。
    //
    // 【孤儿键原样保留】换过引擎的 part 上留着旧引擎的属性键，当前声源不声明它们，故这里一概不碰——
    // 那是有意的（换回去时数据还在，与换引擎保留数据同判例），不是漏清。
    public static Applied Apply(IReadOnlyList<IMidiPart> parts, PartPreset? preset)
    {
        if (parts.Count == 0)
            return default;

        int properties = 0, automations = 0;
        foreach (var part in parts)
            part.BeginMergeDirty();
        foreach (var part in parts)
        {
            var applied = preset == null ? ApplyDefaults(part) : ApplyTo(part, preset);
            properties += applied.Properties;
            automations += applied.Automations;
        }
        foreach (var part in parts)
            part.EndMergeDirty();
        parts[0].Commit();
        return new Applied(properties, automations);
    }

    // 从一个 part 抓一份快照。**物化 dense**：显式值优先、absent 字段落抓取时的声明默认值——
    // 默认值也是声音的一部分，引擎日后改默认值不应改变既存 preset 的声音。
    // 只抓自动化的默认值、不含曲线点（preset 口径：不含时间轴内容）。
    public static PartPreset Capture(IMidiPart part, string presetName)
    {
        var preset = new PartPreset()
        {
            Name = presetName,
            Source = part.SoundSource.GetInfo(),
            Properties = MaterializeProperties(
                part.SoundSource.GetPartPropertyConfig(PartPropertyContext.Single(part)),
                part.Properties.GetInfo()),
        };

        foreach (var kvp in part.SoundSource.AutomationConfigs)
        {
            var key = kvp.Key.Id;
            double value = part.Automations.TryGetValue(key, out var automation) ? automation.DefaultValue.Value : kvp.Value.DefaultValue;
            preset.Automations.Add(key, new AutomationInfo() { DefaultValue = value });
        }

        return preset;
    }

    // 单 part 的应用（config 按该 part 自身音源现算：apply 可能正在改音源，须 per-part 单元素 context）。
    static Applied ApplyDefaults(IMidiPart part)
    {
        int properties = ResetPropertiesToDefaults(part.SoundSource.GetPartPropertyConfig(PartPropertyContext.Single(part)), part.Properties);
        return new Applied(properties, ResetAutomationDefaults(part));
    }

    static Applied ApplyTo(IMidiPart part, PartPreset preset)
    {
        part.SoundSource.SetInfo(preset.Source);
        int properties = ResetPropertiesToDefaults(part.SoundSource.GetPartPropertyConfig(PartPropertyContext.Single(part)), part.Properties);
        ApplyProperties(part, preset.Properties);
        // 自动化轨集合可随属性涌现（条件轨，如某条目被选中才声明的轨）：属性写入即经 part 的属性订阅重算
        //（同步，见 MidiPart.OnPartPropertiesModified），故走到这里时 AutomationConfigs 已是应用后的声明集。
        return new Applied(properties, ApplyAutomationDefaults(part, preset));
    }

    // 沿 ObjectConfig 结构与数据节点并行导航：嵌套 config 走 node.Object(key) 下降，叶子 config 写 node.SetValue。
    // 返回写了几个叶子（回报用；声明为空就是 0，那正是"什么都没重置"的实情）。
    static int ResetPropertiesToDefaults(ObjectConfig config, IDataPropertyObject node)
    {
        int count = 0;
        foreach (var kvp in config.Properties)
        {
            if (kvp.Value is ObjectConfig objectConfig)
            {
                count += ResetPropertiesToDefaults(objectConfig, node.Object(kvp.Key.Id));
            }
            else if (kvp.Value is ArrayConfig or ListConfig or ExtensibleObjectConfig)
            {
                // 数组/列表/变长键控容器：写入默认值（递归各元素/键 config 默认值拼成 PropertyArray/PropertyObject）。
                // 显式重置即物化该值（变长键控 = 当前声明键的默认对象，替换整个容器值）。
                node.SetValue(kvp.Key.Id, kvp.Value.GetDefaultValue());
                count++;
            }
            else if (kvp.Value is IValueConfig valueConfig)
            {
                node.SetValue(kvp.Key.Id, valueConfig.DefaultValue);
                count++;
            }
        }
        return count;
    }

    // preset 抓取用：按 config 树把默认值物化进快照——显式值优先、absent 字段落抓取时的声明默认值。
    // config 之外的既有键（孤儿/条件面板当前隐藏字段的存值）原样保留，与换引擎保留数据同判例。
    static PropertyObject MaterializeProperties(ObjectConfig config, PropertyObject current)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var kvp in config.Properties)
        {
            var key = kvp.Key.Id;
            bool hasCurrent = current.Map.TryGetValue(key, out var currentValue);
            if (kvp.Value is ObjectConfig objectConfig)
            {
                var sub = hasCurrent && currentValue.ToObject(out var currentObject) ? currentObject : PropertyObject.Empty;
                map.Add(key, MaterializeProperties(objectConfig, sub));
            }
            else if (kvp.Value is ArrayConfig or ListConfig or ExtensibleObjectConfig)
            {
                map.Add(key, hasCurrent ? currentValue : kvp.Value.GetDefaultValue());
            }
            else if (kvp.Value is IValueConfig valueConfig)
            {
                map.Add(key, hasCurrent ? currentValue : valueConfig.DefaultValue);
            }
        }
        foreach (var kvp in current.Map)
        {
            if (!map.ContainsKey(kvp.Key))
                map.Add(kvp.Key, kvp.Value);
        }
        return new PropertyObject(map);
    }

    // 声明可随属性值涌现（条件面板：某字段取某值才出现的容器/字段）：应用前那份声明里看不见的键，
    // 写进去之后才被声明。故按「求声明 → 照声明应用」反复，直到声明形状不再变——应用本身幂等，多跑一轮无副作用。
    // 上限只防插件声明来回振荡（值 A 声明出 B、B 又撤掉 A），正常一两轮即收敛。
    static void ApplyProperties(IMidiPart part, PropertyObject preset)
    {
        string? previousShape = null;
        for (int pass = 0; pass < MaxApplyPasses; pass++)
        {
            var config = part.SoundSource.GetPartPropertyConfig(PartPropertyContext.Single(part));
            var shape = ConfigShape(config);
            if (shape == previousShape)
                break;
            previousShape = shape;
            ApplyProperties(config, preset, part.Properties);
        }
    }

    const int MaxApplyPasses = 4;

    // 沿声明把 preset 写进数据节点，按 config 种类分两种语义：
    // · 固定结构对象（ObjectConfig）= 字段合并：下降逐字段写，目标里 preset 没有的键（孤儿）原样保留；
    // · 变长容器（ExtensibleObjectConfig / ListConfig / ArrayConfig）= 整值替换：容器的键集合/元素序列本身就是选择，
    //   合并会把「preset 没选的条目」留在目标里，而值为 {} 的条目（只靠键存在表达"选中"）下降后无字段可写、键永远落不下。
    // 声明之外的键（孤儿 / 条件面板当前隐藏的字段）按旧口径递归合并——不知道它是什么，就不替它做替换的决定。
    internal static void ApplyProperties(ObjectConfig config, PropertyObject properties, IDataPropertyObject node)
    {
        var declared = new Dictionary<string, IControllerConfig>();
        foreach (var kvp in config.Properties)
            declared[kvp.Key.Id] = kvp.Value;

        foreach (var property in properties.Map)
        {
            declared.TryGetValue(property.Key, out var propertyConfig);
            bool isObject = property.Value.ToObject(out var propertyObject);
            if (propertyConfig is ObjectConfig objectConfig && isObject)
            {
                ApplyProperties(objectConfig, propertyObject, node.Object(property.Key));
            }
            else if (propertyConfig == null && isObject)
            {
                MergeUndeclared(propertyObject, node.Object(property.Key));
            }
            else
            {
                node.SetValue(property.Key, property.Value);
            }
        }
    }

    static void MergeUndeclared(PropertyObject properties, IDataPropertyObject node)
    {
        foreach (var property in properties.Map)
        {
            if (property.Value.ToObject(out var propertyObject))
                MergeUndeclared(propertyObject, node.Object(property.Key));
            else
                node.SetValue(property.Key, property.Value);
        }
    }

    // 声明形状 = 各层键路径 + 种类（对象下降、其余只记一层）。只用于判断「再求一次声明有没有长出新东西」。
    static string ConfigShape(ObjectConfig config)
    {
        var sb = new System.Text.StringBuilder();
        AppendShape(sb, config, string.Empty);
        return sb.ToString();
    }

    static void AppendShape(System.Text.StringBuilder sb, ObjectConfig config, string prefix)
    {
        foreach (var kvp in config.Properties)
        {
            var path = prefix + "/" + kvp.Key.Id;
            if (kvp.Value is ObjectConfig objectConfig)
            {
                sb.Append(path).Append("{;");
                AppendShape(sb, objectConfig, path);
            }
            else
            {
                sb.Append(path).Append(':').Append(kvp.Value.GetType().Name).Append(';');
            }
        }
    }

    // 返回动了几条轨。没有曲线的轨（part 上还不存在这条 automation）算不上"重置"，故不计。
    static int ResetAutomationDefaults(IMidiPart part)
    {
        int count = 0;
        foreach (var kvp in part.SoundSource.AutomationConfigs)
        {
            if (part.Automations.TryGetValue(kvp.Key.Id, out var automation))
            {
                automation.DefaultValue.Set(kvp.Value.DefaultValue);
                count++;
            }
        }
        return count;
    }

    static int ApplyAutomationDefaults(IMidiPart part, PartPreset preset)
    {
        int count = 0;
        foreach (var kvp in part.SoundSource.AutomationConfigs)
        {
            double value = preset.Automations.TryGetValue(kvp.Key.Id, out var info) ? info.DefaultValue : kvp.Value.DefaultValue;
            if (part.Automations.TryGetValue(kvp.Key.Id, out var automation))
            {
                automation.DefaultValue.Set(value);
                count++;
            }
            else if (value != kvp.Value.DefaultValue)
            {
                part.AddAutomation(kvp.Key.Id)?.DefaultValue.Set(value);
                count++;
            }
        }
        return count;
    }
}
