using TuneLab.Foundation;

namespace TuneLab.SDK;

// 一条时间轴 lane 画布（TimelineLaneCanvasConfig）在工程里的存储形：插件的整值 + 宿主维护的原点偏移。
// Offset = 值的原点在 part 里的位置（相对 part 原点的 tick）：值里的 tick t 落在 part 的 t + Offset 处。平时为 0；拆分 / 合并后
// part 的原点变了，而值宿主不解读、改写不了，就只记下偏移（如在 S 处拆分，右半的 Offset = −S）。插件看到的原点已含偏移，对它无感。
// 格式插件不认识这张表的，导出到别的格式时丢掉即可（值的含义只有声明它的插件知道）。
public class TimelineLaneCanvasInfo
{
    public double Offset { get; set; } = 0;
    public PropertyValue Value { get; set; } = PropertyValue.Null;
}
