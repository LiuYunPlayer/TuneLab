using System;
using System.Collections.Generic;

namespace TuneLab.SDK;

// 快照 TimeConverter 字段的默认值（快照发布后才加的字段，按演进规矩不标 required、带默认）：宿主经 GetSnapshot 构造的快照
// 永远填上真实的换算器，只有插件自己拼的快照（测试里）忘了填才会走到这里——当场报错，不悄悄按某个假速度算错。
internal sealed class UnprovidedTimeConverter : ITimeConverter
{
    public static readonly UnprovidedTimeConverter Instance = new();

    public double[] ToSeconds(IReadOnlyList<double> ticks) => throw Missing();
    public double[] ToTicks(IReadOnlyList<double> seconds) => throw Missing();

    static InvalidOperationException Missing()
        => new("This snapshot has no TimeConverter: the host always fills it in; a snapshot you build yourself (in a test, say) has to set it.");
}
