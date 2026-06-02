using LProp = TuneLab.Base.Properties;
using LStruct = TuneLab.Base.Structures;
using LVoice = TuneLab.Extensions.Voices;
using VBase = TuneLab.SDK.Base;
using VVoice = TuneLab.SDK.Voice;
using PStruct = TuneLab.Primitives.DataStructures;

namespace TuneLab.Hosting.Compat.Legacy.Conversion;

// Config 家族跨代转换（§三.12 改名 1:1）：Legacy IPropertyConfig 族 → V1 IControllerConfig 族。
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
                return new VBase.SliderConfig(n.DefaultValue, n.MinValue, n.MaxValue, n.IsInterger);
            case LProp.BooleanConfig b:
                return new VBase.CheckBoxConfig(b.DefaultValue);
            case LProp.StringConfig s:
                return new VBase.TextBoxConfig(s.DefaultValue);
            case LProp.EnumConfig e:
                return new VBase.ComboBoxConfig(e.Options, e.DefaultIndex);
            case LProp.ObjectConfig o:
                return new VBase.ObjectConfig(o.Properties.ToV1ConfigMap());
            default:
                // 未知 config（含内部 IntegerConfig/ListConfig，正常不会从插件公共面出现）：优雅降级为空对象。
                return new VBase.ObjectConfig(new PStruct.OrderedMap<string, VBase.IControllerConfig>());
        }
    }

    public static VVoice.AutomationConfig ToV1(this LVoice.AutomationConfig a)
        => new(a.Name, a.DefaultValue, a.MinValue, a.MaxValue, a.Color);

    public static PStruct.IReadOnlyOrderedMap<string, VBase.IControllerConfig> ToV1ConfigMap(
        this LStruct.IReadOnlyOrderedMap<string, LProp.IPropertyConfig> old)
    {
        var map = new PStruct.OrderedMap<string, VBase.IControllerConfig>();
        foreach (var kv in old)
            map.Add(kv.Key, kv.Value.ToV1());
        return map;
    }

    public static PStruct.IReadOnlyOrderedMap<string, VVoice.AutomationConfig> ToV1AutomationMap(
        this LStruct.IReadOnlyOrderedMap<string, LVoice.AutomationConfig> old)
    {
        var map = new PStruct.OrderedMap<string, VVoice.AutomationConfig>();
        foreach (var kv in old)
            map.Add(kv.Key, kv.Value.ToV1());
        return map;
    }
}
