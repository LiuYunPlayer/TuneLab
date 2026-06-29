using LProp = TuneLab.Base.Properties;
using LStruct = TuneLab.Base.Structures;
using LVoice = TuneLab.Extensions.Voices;
using VConfig = TuneLab.SDK;
using PStruct = TuneLab.Foundation;

namespace TuneLab.Hosting.Compat.Legacy.Conversion;

// Config 家族跨代转换（改名 1:1）：Legacy IPropertyConfig 族 → V1 IControllerConfig 族。
//   NumberConfig→SliderConfig、BooleanConfig→CheckBoxConfig、StringConfig→TextBoxConfig、
//   EnumConfig→ComboBoxConfig、ObjectConfig→ObjectConfig、AutomationConfig→AutomationConfig。
internal static class ControllerConfigConvert
{
    public static VConfig.IControllerConfig ToV1(this LProp.IPropertyConfig old)
    {
        switch (old)
        {
            // AutomationConfig : NumberConfig —— 必须先于 NumberConfig 匹配。
            case LVoice.AutomationConfig a:
                return a.ToV1();
            case LProp.NumberConfig n:
                return n.IsInterger
                    ? VConfig.SliderConfig.Integer(n.DefaultValue, n.MinValue, n.MaxValue)
                    : VConfig.SliderConfig.Linear(n.DefaultValue, n.MinValue, n.MaxValue);
            case LProp.BooleanConfig b:
                return VConfig.CheckBoxConfig.Create(b.DefaultValue);
            case LProp.StringConfig s:
                return VConfig.TextBoxConfig.Create(s.DefaultValue);
            case LProp.EnumConfig e:
            {
                // legacy EnumConfig 是 string 选项 + 索引默认值；V1 ComboBoxConfig 是 ComboBoxItem 选项 + 值默认值。
                var options = new VConfig.ComboBoxItem[e.Options.Count];
                for (int i = 0; i < e.Options.Count; i++)
                    options[i] = e.Options[i];
                var defaultValue = (uint)e.DefaultIndex < (uint)options.Length ? options[e.DefaultIndex]
                    : options.Length > 0 ? options[0] : default;
                return VConfig.ComboBoxConfig.Create(options).WithDefault(defaultValue);
            }
            case LProp.ObjectConfig o:
                return VConfig.ObjectConfig.Create(o.Properties.ToV1ConfigMap());
            default:
                // 未知 config（含内部 IntegerConfig/ListConfig，正常不会从插件公共面出现）：优雅降级为空对象。
                return VConfig.ObjectConfig.Create(new PStruct.OrderedMap<VConfig.PropertyKey, VConfig.IControllerConfig>());
        }
    }

    // 标签随键：legacy 属性无独立译名，键即标签（PropertyKey.DisplayText 留空、回退 Id）；
    // legacy AutomationConfig 的 Name 进键的 DisplayText。
    public static VConfig.AutomationConfig ToV1(this LVoice.AutomationConfig a)
        => VConfig.AutomationConfig.Create(a.MinValue, a.MaxValue).WithColor(a.Color).WithDefault(a.DefaultValue);

    public static PStruct.IReadOnlyOrderedMap<VConfig.PropertyKey, VConfig.IControllerConfig> ToV1ConfigMap(
        this LStruct.IReadOnlyOrderedMap<string, LProp.IPropertyConfig> old)
    {
        var map = new PStruct.OrderedMap<VConfig.PropertyKey, VConfig.IControllerConfig>();
        foreach (var kv in old)
            map.Add(kv.Key, kv.Value.ToV1());
        return map;
    }

    public static PStruct.IReadOnlyOrderedMap<VConfig.PropertyKey, VConfig.AutomationConfig> ToV1AutomationMap(
        this LStruct.IReadOnlyOrderedMap<string, LVoice.AutomationConfig> old)
    {
        var map = new PStruct.OrderedMap<VConfig.PropertyKey, VConfig.AutomationConfig>();
        foreach (var kv in old)
            map.Add((kv.Key, kv.Value.Name), kv.Value.ToV1());
        return map;
    }
}
