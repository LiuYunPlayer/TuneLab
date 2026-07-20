using System.Linq;
using Newtonsoft.Json.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Utils;

// DataInfo 叶子 ↔ 原生 JSON（Newtonsoft JToken）的唯一共用转换，与 PropertyJsonUtils 同族分工：
// 值树（PropertyValue 家族）归 PropertyJsonUtils，DataInfo 叶子（SoundSourceInfo / automation 轨集合）归本类。
// 消费者：TLP JSON（TuneLabProject）、part preset（PresetConfigManager）。
// 叶子加字段时只改本类，全部 JSON 站点同步生效；TLP 二进制版（TuneLabProjectCbor）需另行对齐。
internal static class DataInfoJsonUtils
{
    public static JObject ToJson(SoundSourceInfo source) => new()
    {
        ["type"] = source.Type,
        ["id"] = source.Id,
        ["kind"] = source.Kind == SourceKind.Instrument ? "instrument" : "voice",
    };

    // kind 缺键 = Voice（既有数据无此键时的解释，与 SourceKind 缺省语义一致）。
    public static SoundSourceInfo ToSoundSourceInfo(JToken? json) => new()
    {
        Type = (string?)json?["type"] ?? string.Empty,
        Id = (string?)json?["id"] ?? string.Empty,
        Kind = (string?)json?["kind"] == "instrument" ? SourceKind.Instrument : SourceKind.Voice,
    };

    // automation 轨集合形：{ "<key>": { "default": d, "values": [x,y,x,y,…] } }（点表为 x/y 扁平交替对）。
    public static JObject ToJson(Map<string, AutomationInfo> map)
    {
        var automations = new JObject();
        foreach (var kvp in map)
        {
            var automation = new JObject();
            automation.Add("default", kvp.Value.DefaultValue);
            var values = new JArray();
            foreach (var point in kvp.Value.Points)
            {
                values.Add(point.X);
                values.Add(point.Y);
            }
            automation.Add("values", values);
            automations.Add(kvp.Key, automation);
        }
        return automations;
    }

    public static void ReadAutomations(JToken automations, Map<string, AutomationInfo> map)
    {
        foreach (JProperty property in automations.Children())
        {
            var automationInfo = new AutomationInfo
            {
                DefaultValue = (double?)property.Value["default"] ?? 0,
            };
            bool flag = false;
            double x = 0;
            foreach (double value in property.Value["values"]!.ToArray())
            {
                if (flag)
                    automationInfo.Points.Add(new Point(x, value));
                else
                    x = value;
                flag = !flag;
            }
            map.Add(property.Name, automationInfo);
        }
    }
}
