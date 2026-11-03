using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Formats.TLP;

// 时间轴画布表在工程文件里的形状：{ id: { "offset": 偏移, "value": 整值 } }。借用属性对象的读写（JSON / CBOR 两套都有现成的），
// 值原样落盘、宿主不解读。没设过的 id 不在表里，所以值为 Null 的条目不会出现；读到时也丢掉。
internal static class TimelineCanvasCodec
{
    public static PropertyObject ToPropertyObject(Map<string, TimelineCanvasInfo> canvases)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var kvp in canvases)
        {
            if (kvp.Value.Value.IsNull())
                continue;

            var entry = new Map<string, PropertyValue>
            {
                { OffsetKey, kvp.Value.Offset },
                { ValueKey, kvp.Value.Value },
            };
            map.Add(kvp.Key, new PropertyObject(entry));
        }
        return new PropertyObject(map);
    }

    public static void Read(PropertyObject source, Map<string, TimelineCanvasInfo> canvases)
    {
        foreach (var kvp in source.Map)
        {
            if (!kvp.Value.ToObject(out var entry))
                continue;
            if (!entry.Map.TryGetValue(ValueKey, out var value) || value.IsNull())
                continue;

            double offset = entry.Map.TryGetValue(OffsetKey, out var offsetValue) && offsetValue.ToDouble(out var d) ? d : 0;
            canvases[kvp.Key] = new TimelineCanvasInfo { Offset = offset, Value = value };
        }
    }

    const string OffsetKey = "offset";
    const string ValueKey = "value";
}
