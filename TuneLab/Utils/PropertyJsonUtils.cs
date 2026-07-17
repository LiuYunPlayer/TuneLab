using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TuneLab.Foundation;

namespace TuneLab.Utils;

// PropertyValue/PropertyObject/PropertyArray ↔ 原生 JSON（Newtonsoft JToken）的唯一共用转换。
// 语义与 TLP 二进制版（TuneLabProjectCbor.WritePropertyValue/ReadPropertyValue）逐条对齐：
//   写对象字段：Null/Multiple 哨兵不写键（稀疏，absent = 默认，presence 语义）；
//   写数组元素：按位写齐，哨兵/不可表示元素落 JSON null 占位（保序）；空数组照写 []（present-[] 是真实值，不可省）；
//   读：JSON null / 未知 token → PropertyValue.Null；number 一律读成 double。
// 消费者：TLP JSON（TuneLabProject）、part preset（PresetConfigManager）、扩展设置（ExtensionSettingsStore）。
// PropertyValue 新增类型臂时须同步本类与 TuneLabProjectCbor（全部序列化站点，见 PropertyValue 头注释）。
internal static class PropertyJsonUtils
{
    public static JObject ToJson(PropertyObject properties)
    {
        var json = new JObject();
        foreach (var property in properties.Map)
        {
            // 稀疏：null / multiple 哨兵不写键（与 presence 语义一致：absent = 默认）。
            if (property.Value.IsNull() || property.Value.IsMultiple())
                continue;

            json.Add(property.Key, ToJson(property.Value));
        }
        return json;
    }

    public static JArray ToJson(PropertyArray array)
    {
        var json = new JArray();
        foreach (var value in array)
            json.Add(ToJson(value));
        return json;
    }

    // 递归写任意 PropertyValue（对象值与数组元素共用）。数组元素须按位写齐，故哨兵/不可表示写成 JSON null 占位。
    public static JToken ToJson(PropertyValue value)
    {
        if (value.ToObject(out var propertyObject))
            return ToJson(propertyObject);
        if (value.ToArray(out var propertyArray))
            return ToJson(propertyArray);
        if (value.ToBool(out var boolValue))
            return new JValue(boolValue);
        if (value.ToDouble(out var doubleValue))
            return new JValue(doubleValue);
        if (value.ToString(out var stringValue))
            return new JValue(stringValue);
        return JValue.CreateNull();
    }

    public static PropertyObject ToPropertyObject(JToken? token)
    {
        var map = new Map<string, PropertyValue>();
        if (token is JObject jObject)
        {
            foreach (var property in jObject.Properties())
                map.Add(property.Name, ToPropertyValue(property.Value));
        }
        return new PropertyObject(map);
    }

    public static PropertyArray ToPropertyArray(JArray array)
    {
        var values = new List<PropertyValue>(array.Count);
        foreach (var item in array)
            values.Add(ToPropertyValue(item));
        return new PropertyArray(values);
    }

    // 递归读任意 JSON token（对象值与数组元素共用）。
    public static PropertyValue ToPropertyValue(JToken? token)
    {
        switch (token?.Type)
        {
            case JTokenType.Boolean:
                return (bool)token;
            case JTokenType.Integer:
            case JTokenType.Float:
                return (double)token;
            case JTokenType.String:
                return (string?)token ?? string.Empty;
            case JTokenType.Object:
                return ToPropertyObject(token);
            case JTokenType.Array:
                return ToPropertyArray((JArray)token);
            default:
                return PropertyValue.Null;
        }
    }
}
