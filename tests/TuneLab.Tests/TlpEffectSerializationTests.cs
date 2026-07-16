using System.Collections.Generic;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 效果链的工程序列化 round-trip：MidiPartInfo.Effects（类型/启用/参数/自动化/分段轨）与
// part 级 PiecewiseAutomations 经两种原生格式（tlp=JSON、tlpx=CBOR）Serialize→Deserialize 后全等。
// 背景：这两个字段曾在两个序列化器中双双缺失（存不进、读不出），本文件是该缺口的回归封条。
public class TlpEffectSerializationTests
{
    static ProjectInfo SampleProject()
    {
        var effect = new EffectInfo
        {
            Type = "TLTestGain",
            IsEnabled = false,
            Properties = new PropertyObject(new Map<string, PropertyValue>
            {
                { "gain", 0.8 },
                { "env_enabled", true },
                { "label", "warm" },
            }),
        };
        effect.Automations.Add("gain_env", new AutomationInfo
        {
            DefaultValue = 1.0,
            Points = new List<Point> { new(0, 1), new(480, 1.5), new(960, 0.5) },
        });
        effect.PiecewiseAutomations.Add("formant", new List<List<Point>>
        {
            new() { new(0, -20), new(240, 40) },
            new() { new(480, 10) },
        });

        var second = new EffectInfo { Type = "TLTestReverse" };   // 默认启用、空参数：验证最小形态

        var part = new MidiPartInfo
        {
            Name = "p",
            Pos = 0,
            StartOffset = 0,
            EndOffset = 1920,
        };
        part.Effects.Add(effect);
        part.Effects.Add(second);
        part.PiecewiseAutomations.Add("Bend", new List<List<Point>>
        {
            new() { new(10, 5), new(20, -5) },
        });

        var track = new TrackInfo { Name = "t" };
        track.Parts.Add(part);

        var project = new ProjectInfo();
        project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        project.TimeSignatures.Add(new TimeSignatureInfo { BarIndex = 0, Numerator = 4, Denominator = 4 });
        project.Tracks.Add(track);
        return project;
    }

    static void AssertRoundTrip(ProjectInfo roundtrip)
    {
        var part = Assert.IsType<MidiPartInfo>(Assert.Single(Assert.Single(roundtrip.Tracks).Parts));

        Assert.Equal(2, part.Effects.Count);
        var effect = part.Effects[0];
        Assert.Equal("TLTestGain", effect.Type);
        Assert.False(effect.IsEnabled);
        Assert.Equal(0.8, effect.Properties.GetValue("gain", 0.0));
        Assert.True(effect.Properties.GetValue("env_enabled", false));
        Assert.Equal("warm", effect.Properties.GetValue("label", ""));

        var env = effect.Automations["gain_env"];
        Assert.Equal(1.0, env.DefaultValue);
        Assert.Equal(3, env.Points.Count);
        Assert.Equal(480, env.Points[1].X);
        Assert.Equal(1.5, env.Points[1].Y);

        var formant = effect.PiecewiseAutomations["formant"];
        Assert.Equal(2, formant.Count);
        Assert.Equal(40, formant[0][1].Y);

        var second = part.Effects[1];
        Assert.Equal("TLTestReverse", second.Type);
        Assert.True(second.IsEnabled);
        Assert.Empty(second.Automations);

        var bend = part.PiecewiseAutomations["Bend"];
        Assert.Equal(-5, Assert.Single(bend)[1].Y);
    }

    [Fact]
    public void Json_RoundTrip_PreservesEffects()
    {
        var format = new TuneLabProject();
        using var stream = format.Serialize(SampleProject());
        stream.Position = 0;
        AssertRoundTrip(format.Deserialize(stream));
    }

    [Fact]
    public void Cbor_RoundTrip_PreservesEffects()
    {
        var format = new TuneLabProjectCbor();
        using var stream = format.Serialize(SampleProject());
        stream.Position = 0;
        AssertRoundTrip(format.Deserialize(stream));
    }

    // 无 effect 的 part：不落键、读回空集合（文件不因新字段膨胀）。
    [Fact]
    public void EmptyEffects_StayEmpty_BothFormats()
    {
        var project = SampleProject();
        var part = (MidiPartInfo)project.Tracks[0].Parts[0];
        part.Effects.Clear();
        part.PiecewiseAutomations.Clear();

        foreach (var format in new object[] { new TuneLabProject(), (object)new TuneLabProjectCbor() })
        {
            var import = (IImportFormat)format;
            var export = (IExportFormat)format;
            using var stream = export.Serialize(project);
            stream.Position = 0;
            var roundtrip = import.Deserialize(stream);
            var rtPart = Assert.IsType<MidiPartInfo>(roundtrip.Tracks[0].Parts[0]);
            Assert.Empty(rtPart.Effects);
            Assert.Empty(rtPart.PiecewiseAutomations);
        }
    }
}
