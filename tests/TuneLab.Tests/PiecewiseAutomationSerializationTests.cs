using System.Collections.Generic;
using TuneLab.Data;
using TuneLab.Foundation;
using Xunit;

namespace TuneLab.Tests;

// 声明分段轨集合的序列化 round-trip：host DataObjectMap<string, IPiecewiseAutomation> ↔ Map<string, List<List<Point>>>。
// 走 SetInfo/GetInfo 的真实转换路径（ToPiecewiseAutomations / PiecewiseAutomationsToInfo），与 MidiPart/Effect 同。
// 覆盖：多轨多段往返全等、空往返、序列化层整存（不依赖任何 config——孤儿保留的底层保证）。
public class PiecewiseAutomationSerializationTests
{
    static Map<string, List<List<Point>>> SampleInfo()
    {
        var info = new Map<string, List<List<Point>>>();
        info.Add("Bend", new List<List<Point>>
        {
            new() { new(0, 10), new(120, 90), new(240, -30) },
            new() { new(480, 0), new(600, 50) },
        });
        info.Add("Formant", new List<List<Point>>
        {
            new() { new(50, -100), new(300, 100) },
        });
        return info;
    }

    static void AssertSameShape(IReadOnlyMap<string, List<List<Point>>> a, IReadOnlyMap<string, List<List<Point>>> b)
    {
        Assert.Equal(a.Count, b.Count);
        foreach (var kvp in a)
        {
            Assert.True(b.ContainsKey(kvp.Key));
            var ga = kvp.Value;
            var gb = b[kvp.Key];
            Assert.Equal(ga.Count, gb.Count);
            for (int s = 0; s < ga.Count; s++)
            {
                Assert.Equal(ga[s].Count, gb[s].Count);
                for (int p = 0; p < ga[s].Count; p++)
                {
                    Assert.Equal(ga[s][p].X, gb[s][p].X);
                    Assert.Equal(ga[s][p].Y, gb[s][p].Y);
                }
            }
        }
    }

    [Fact]
    public void RoundTrip_PreservesAllTracksAndSegments()
    {
        var info = SampleInfo();
        var map = new DataObjectMap<string, IPiecewiseAutomation>();
        map.SetInfo(info.ToPiecewiseAutomations());       // 镜像 MidiPart/Effect.SetInfo
        var roundtrip = map.PiecewiseAutomationsToInfo();  // 镜像 GetInfo
        AssertSameShape(info, roundtrip);
    }

    [Fact]
    public void RoundTrip_Empty_StaysEmpty()
    {
        var info = new Map<string, List<List<Point>>>();
        var map = new DataObjectMap<string, IPiecewiseAutomation>();
        map.SetInfo(info.ToPiecewiseAutomations());
        Assert.Empty(map.PiecewiseAutomationsToInfo());
    }

    // 序列化层整存：map 不依赖任何 config，存进什么轨就整存什么轨（孤儿轨——声明已消失但数据仍在——不丢）。
    [Fact]
    public void RoundTrip_StoresTracksRegardlessOfDeclaration()
    {
        var info = SampleInfo();
        var map = new DataObjectMap<string, IPiecewiseAutomation>();
        map.SetInfo(info.ToPiecewiseAutomations());
        Assert.True(map.ContainsKey("Bend"));
        Assert.True(map.ContainsKey("Formant"));
    }
}
