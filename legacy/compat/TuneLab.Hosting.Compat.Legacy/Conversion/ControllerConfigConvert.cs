using LProp = TuneLab.Base.Properties;
using LStruct = TuneLab.Base.Structures;
using LVoice = TuneLab.Extensions.Voices;
using VBase = TuneLab.SDK.Base;
using PStruct = TuneLab.Primitives.DataStructures;

namespace TuneLab.Hosting.Compat.Legacy.Conversion;

// Config 家族跨代转换（改名 1:1）：Legacy IPropertyConfig 族 → V1 IControllerConfig 族。
//   NumberConfig→SliderConfig、BooleanConfig→CheckBoxConfig、StringConfig→TextBoxConfig、
//   EnumConfig→ComboBoxConfig、ObjectConfig→ObjectConfig、AutomationConfig→AutomationConfig。
internal static class ControllerConfigConvert
{
    public static VBase.IControllerConfig ToV1(this LProp.IPropertyConfig old)
    {
        switch (old)
        {
            // AutomationConfig : NumberConfig —— 必须先于 NumberConfig 匹配。
            case LVoice.AutomationConfig a:
                return a.ToV1();
            case LProp.NumberConfig n:
                return new VBase.SliderConfig { DefaultValue = n.DefaultValue, MinValue = n.MinValue, MaxValue = n.MaxValue, IsInterger = n.IsInterger };
            case LProp.BooleanConfig b:
                return new VBase.CheckBoxConfig { DefaultValue = b.DefaultValue };
            case LProp.StringConfig s:
                return new VBase.TextBoxConfig { DefaultValue = s.DefaultValue };
            case LProp.EnumConfig e:
            {
                // legacy EnumConfig 是 string 选项 + 索引默认值；V1 ComboBoxConfig 是 ComboBoxOption 选项 + 值默认值。
                var options = new VBase.ComboBoxOption[e.Options.Count];
                for (int i = 0; i < e.Options.Count; i++)
                    options[i] = e.Options[i];
                var defaultValue = (uint)e.DefaultIndex < (uint)options.Length ? options[e.DefaultIndex]
                    : options.Length > 0 ? options[0] : default;
                return new VBase.ComboBoxConfig { Options = options, DefaultOption = defaultValue };
            }
            case LProp.ObjectConfig o:
                return new VBase.ObjectConfig { Properties = o.Properties.ToV1ConfigMap() };
            default:
                // 未知 config（含内部 IntegerConfig/ListConfig，正常不会从插件公共面出现）：优雅降级为空对象。
                return new VBase.ObjectConfig { Properties = new PStruct.OrderedMap<string, VBase.IControllerConfig>() };
        }
    }

    public static VBase.AutomationConfig ToV1(this LVoice.AutomationConfig a)
        => new() { Name = a.Name, DefaultValue = a.DefaultValue, MinValue = a.MinValue, MaxValue = a.MaxValue, Color = a.Color };

    public static PStruct.IReadOnlyOrderedMap<string, VBase.IControllerConfig> ToV1ConfigMap(
        this LStruct.IReadOnlyOrderedMap<string, LProp.IPropertyConfig> old)
    {
        var map = new PStruct.OrderedMap<string, VBase.IControllerConfig>();
        foreach (var kv in old)
            map.Add(kv.Key, kv.Value.ToV1());
        return map;
    }

    public static PStruct.IReadOnlyOrderedMap<string, VBase.AutomationConfig> ToV1AutomationMap(
        this LStruct.IReadOnlyOrderedMap<string, LVoice.AutomationConfig> old)
    {
        var map = new PStruct.OrderedMap<string, VBase.AutomationConfig>();
        foreach (var kv in old)
            map.Add(kv.Key, kv.Value.ToV1());
        return map;
    }
}
