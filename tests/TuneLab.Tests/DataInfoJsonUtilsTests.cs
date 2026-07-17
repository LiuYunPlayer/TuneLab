using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;
using Xunit;

namespace TuneLab.Tests;

// DataInfo 叶子共用序列化件（DataInfoJsonUtils）的往返语义。
// 只测受影响范围：SoundSourceInfo（含 kind 三态解释）与 automation 轨集合的 JSON 回环；
// TLP 工程读写与 part preset 均消费本件，回环即两处站点的共同契约。
public class DataInfoJsonUtilsTests
{
    [Fact]
    public void SoundSource_Roundtrip_PreservesInstrumentKind()
    {
        var source = new SoundSourceInfo() { Kind = SourceKind.Instrument, Type = "engine", ID = "bank" };
        var restored = DataInfoJsonUtils.ToSoundSourceInfo(DataInfoJsonUtils.ToJson(source));

        Assert.Equal(SourceKind.Instrument, restored.Kind);
        Assert.Equal("engine", restored.Type);
        Assert.Equal("bank", restored.ID);
    }

    [Fact]
    public void SoundSource_MissingKind_DefaultsToVoice()
    {
        var restored = DataInfoJsonUtils.ToSoundSourceInfo(Newtonsoft.Json.Linq.JObject.Parse("""{"type":"t","id":"i"}"""));

        Assert.Equal(SourceKind.Voice, restored.Kind);
        Assert.Equal("t", restored.Type);
    }

    [Fact]
    public void SoundSource_NullToken_YieldsEmptyVoice()
    {
        var restored = DataInfoJsonUtils.ToSoundSourceInfo(null);

        Assert.Equal(SourceKind.Voice, restored.Kind);
        Assert.Equal(string.Empty, restored.Type);
        Assert.Equal(string.Empty, restored.ID);
    }

    [Fact]
    public void Automations_Roundtrip_PreservesDefaultAndPoints()
    {
        var map = new Map<string, AutomationInfo>
        {
            { "gain", new AutomationInfo() { DefaultValue = 0.5, Points = [new Point(1, 2), new Point(3, 4)] } },
            { "flat", new AutomationInfo() { DefaultValue = -1 } },
        };

        var restored = new Map<string, AutomationInfo>();
        DataInfoJsonUtils.ReadAutomations(DataInfoJsonUtils.ToJson(map), restored);

        Assert.Equal(2, restored.Count);
        Assert.Equal(0.5, restored["gain"].DefaultValue);
        Assert.Equal([new Point(1, 2), new Point(3, 4)], restored["gain"].Points);
        Assert.Equal(-1, restored["flat"].DefaultValue);
        Assert.Empty(restored["flat"].Points);
    }
}
