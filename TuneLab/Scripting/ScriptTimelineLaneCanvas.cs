using Jint;
using Jint.Native;
using TuneLab.Data;
using TuneLab.Foundation;

namespace TuneLab.Scripting;

// part / effect 句柄上时间轴 lane 画布读写的共用部分。值宿主不解读：读出什么给什么，写进什么存什么（形状由插件校验）。
// 脚本写入属宿主发起的写入：没有改动范围，按整条通知合成侧。
internal static class ScriptTimelineLaneCanvas
{
    // 没设过 = null。
    public static JsValue Read(Engine engine, TimelineLaneCanvases store, string id)
    {
        var value = store.GetValue(id);
        return value.IsNull() ? JsValue.Null : ScriptInfo.ToJs(engine, value);
    }

    // 值里 tick 0 所在的绝对 tick = part 锚点 + 偏移（平时偏移为 0；拆分 / 合并后不为 0）。没设过的按偏移 0。
    public static double Origin(IMidiPart part, TimelineLaneCanvases store, string id)
        => part.Pos.Value + (store.TryGetValue(id, out var entry) ? entry.Offset : 0);

    // null / undefined = 删掉存值（回到"没设过"，插件按自己的默认值补）。
    public static PropertyValue ToValue(JsValue value)
        => value is null || value.IsNull() || value.IsUndefined() ? PropertyValue.Null : ScriptInfo.ReadPropertyValue(value);
}
