using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// part 拆分 / 选区截取（IMidiPartExtension.RangeInfo）的回归：子段曾只带音符、颤音、音高与连续自动化轨，
// 效果链与分段自动化轨（part 级的、各效果器的）都被丢掉——拆分一次，两半的效果器和分段曲线就没了。
[Collection("DataThreadProject")]   // 要建真 part（见 FinalPronunciationTests 的 CollectionDefinition）
public class PartSplitTests
{
    static PartSplitTests()
    {
        TestVoices.EnsureBuiltIn();
    }

    // MidiPart 一建就起合成管线，要求在"数据线程"上（同 ParameterSyncMoveTests）。
    sealed class DataThreadContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }

    static IMidiPart SamplePart()
    {
        var info = new MidiPartInfo { Name = "p", Pos = 480, EndOffset = 1920 };
        info.PiecewiseAutomations.Add("Bend", new PiecewiseAutomationInfo { Segments = [[new(100, 1), new(300, 2)], [new(1200, 3), new(1500, 4)]] });
        var effect = new EffectInfo { Id = "fx1", Type = "TLTestGain", IsEnabled = false };
        effect.Automations.Add("gain", new AutomationInfo { DefaultValue = 1, Points = [new(200, 0.5), new(1400, -0.5)] });
        effect.PiecewiseAutomations.Add("formant", new PiecewiseAutomationInfo { Segments = [[new(1100, 10), new(1300, 20)]] });
        info.Effects.Add(effect);
        var track = new NativeTrackInfo { Name = "t1" };
        track.Parts.Add(info);
        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo { Tracks = { track } }));
        return (IMidiPart)document.Project!.Tracks.First().Parts.First();
    }

    [Fact]
    public void Split_KeepsTheEffectChainAndPiecewiseAutomations()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DataThreadContext());
        try
        {
            var part = SamplePart();
            var left = part.RangeInfo(0, 960);
            var right = part.RangeInfo(960, 1920);

            // 效果链两半都在，身份与启用状态沿用。
            foreach (var half in new[] { left, right })
            {
                var effect = Assert.Single(half.Effects);
                Assert.Equal("fx1", effect.Id);
                Assert.Equal("TLTestGain", effect.Type);
                Assert.False(effect.IsEnabled);
            }

            // part 级分段轨：各半只有落在自己里面的那一段，换到子段起点。
            Assert.Equal(100, Assert.Single(left.PiecewiseAutomations["Bend"].Segments)[0].X);
            Assert.Equal(1200 - 960, Assert.Single(right.PiecewiseAutomations["Bend"].Segments)[0].X);

            // 效果器的分段轨与连续轨同样裁剪、换起点。
            Assert.Empty(left.Effects[0].PiecewiseAutomations["formant"].Segments);
            Assert.Equal(1100 - 960, Assert.Single(right.Effects[0].PiecewiseAutomations["formant"].Segments)[0].X);
            Assert.Equal(1, right.Effects[0].Automations["gain"].DefaultValue);
            Assert.Contains(right.Effects[0].Automations["gain"].Points, p => p.X == 1400 - 960 && p.Y == -0.5);
            Assert.DoesNotContain(right.Effects[0].Automations["gain"].Points, p => p.X == 200 - 960);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
