using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TuneLab.Data;
using TuneLab.Data.Synthesis;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// part 级时间轴画布的存储：整值 + 偏移，写入 / 撤销 / 重做都通知改动范围（part 局部 tick），工程文件两种格式往返，拆分 / 合并换算偏移。
[Collection("DataThreadProject")]   // 拆分用例要建真 part（见 FinalPronunciationTests 的 CollectionDefinition）
public class TimelineCanvasStorageTests
{
    static TimelineCanvasStorageTests()
    {
        TestVoices.EnsureBuiltIn();
    }

    // MidiPart 一建就起合成管线，要求在"数据线程"上（同 ParameterSyncMoveTests）。
    sealed class DataThreadContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }

    static PropertyValue Value(string tag) => new PropertyObject(new Map<string, PropertyValue> { { "tag", tag } });

    sealed class Owner(DataDocument doc) : DataObject(doc) { }

    static (DataDocument Doc, TimelineCanvases Canvases, List<(string Id, IReadOnlyList<TickRange>? Ranges)> Events) Setup()
    {
        var doc = new DataDocument();
        var canvases = new TimelineCanvases(new Owner(doc));
        var events = new List<(string, IReadOnlyList<TickRange>?)>();
        canvases.CanvasModified.Subscribe((id, ranges) => events.Add((id, ranges)));
        return (doc, canvases, events);
    }

    [Fact]
    public void SetValue_StoresWholeValue_AndUndoRedoResendTheRanges()
    {
        var (doc, canvases, events) = Setup();
        canvases.SetValue("mix", Value("a"), [new TickRange(0, 480)]);
        doc.Commit();
        canvases.SetValue("mix", Value("b"), [new TickRange(960, 1920), new TickRange(0, 10)]);
        doc.Commit();
        Assert.Equal(Value("b"), canvases.GetValue("mix"));

        doc.Undo();
        Assert.Equal(Value("a"), canvases.GetValue("mix"));
        doc.Redo();
        Assert.Equal(Value("b"), canvases.GetValue("mix"));

        Assert.Equal(4, events.Count);
        Assert.All(events, e => Assert.Equal("mix", e.Id));
        // 撤销、重做原样重发提交时报的范围。
        Assert.Equal(events[1].Ranges, events[2].Ranges);
        Assert.Equal(events[1].Ranges, events[3].Ranges);
        Assert.Equal([new TickRange(960, 1920), new TickRange(0, 10)], events[1].Ranges!);
    }

    [Fact]
    public void SetNull_RemovesTheEntry_AndNoRangesMeansWhole()
    {
        var (doc, canvases, events) = Setup();
        canvases.SetValue("mix", Value("a"));
        canvases.SetValue("mix", PropertyValue.Null);
        Assert.False(canvases.TryGetValue("mix", out _));
        Assert.True(canvases.GetValue("mix").IsNull());
        Assert.Equal(0, canvases.Count);
        Assert.All(events, e => Assert.Null(e.Ranges));

        // 值没变不写、不通知。
        events.Clear();
        canvases.SetValue("mix", PropertyValue.Null);
        Assert.Empty(events);
    }

    // 范围按插件看到的 tick 报，存的是 part 局部 tick：值的原点在 part 的 Offset 处。
    [Fact]
    public void Ranges_AreShiftedByTheOffset()
    {
        var (doc, canvases, events) = Setup();
        canvases.SetEntry("mix", new TimelineCanvasEntry(-960, Value("a")));
        events.Clear();
        canvases.SetValue("mix", Value("b"), [new TickRange(1000, 1200)]);

        Assert.Equal(-960, canvases.GetInfo()["mix"].Offset);   // 偏移沿用
        Assert.Equal([new TickRange(40, 240)], Assert.Single(events).Ranges!);
    }

    [Fact]
    public void SetInfo_WritesOnlyChangedEntries()
    {
        var (doc, canvases, events) = Setup();
        canvases.SetValue("a", Value("1"));
        canvases.SetValue("b", Value("2"));
        events.Clear();

        var info = new Map<string, TimelineCanvasInfo>
        {
            { "a", new TimelineCanvasInfo { Value = Value("1") } },
            { "c", new TimelineCanvasInfo { Offset = 5, Value = Value("3") } },
        };
        canvases.SetInfo(info);

        Assert.Equal(["b", "c"], events.Select(e => e.Id).OrderBy(id => id));
        Assert.Equal(["a", "c"], canvases.Items.Select(kvp => kvp.Key).OrderBy(id => id));
        Assert.Equal(5, canvases.GetInfo()["c"].Offset);
    }

    static ProjectInfo SampleProject()
    {
        var part = new MidiPartInfo { Name = "p", EndOffset = 1920 };
        part.TimelineCanvases.Add("mix", new TimelineCanvasInfo
        {
            Offset = -480,
            Value = new PropertyObject(new Map<string, PropertyValue>
            {
                { "points", new PropertyArray([new PropertyArray([0.0, 0.5]), new PropertyArray([480.0, 1.0])]) },
                { "lang", "en" },
                { "on", true },
            }),
        });
        part.TimelineCanvases.Add("marks", new TimelineCanvasInfo { Value = new PropertyArray(["x", "y"]) });
        var effect = new EffectInfo { Type = "TLTestGain" };
        effect.TimelineCanvases.Add("env", new TimelineCanvasInfo { Offset = 3, Value = 0.25 });
        part.Effects.Add(effect);

        var track = new TrackInfo { Name = "t" };
        track.Parts.Add(part);
        var project = new ProjectInfo();
        project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        project.TimeSignatures.Add(new TimeSignatureInfo { BarIndex = 0, Numerator = 4, Denominator = 4 });
        project.Tracks.Add(track);
        return project;
    }

    [Fact]
    public void RoundTrips_BothFormats()
    {
        var expected = (MidiPartInfo)SampleProject().Tracks[0].Parts[0];
        foreach (var format in new object[] { new TuneLabProject(), new TuneLabProjectCbor() })
        {
            using var stream = new MemoryStream();
            ((IExportFormat)format).Serialize(stream, SampleProject());
            stream.Position = 0;
            var part = Assert.IsType<MidiPartInfo>(((IImportFormat)format).Deserialize(stream).Tracks[0].Parts[0]);

            Assert.Equal(["mix", "marks"], part.TimelineCanvases.Keys.OrderByDescending(k => k));
            Assert.Equal(-480, part.TimelineCanvases["mix"].Offset);
            Assert.Equal(expected.TimelineCanvases["mix"].Value, part.TimelineCanvases["mix"].Value);
            Assert.Equal(expected.TimelineCanvases["marks"].Value, part.TimelineCanvases["marks"].Value);
            var env = part.Effects[0].TimelineCanvases["env"];
            Assert.Equal(3, env.Offset);
            Assert.Equal(PropertyValue.Create(0.25), env.Value);
        }
    }

    [Fact]
    public void NoCanvases_NoKey()
    {
        var project = SampleProject();
        var part = (MidiPartInfo)project.Tracks[0].Parts[0];
        part.TimelineCanvases.Clear();
        part.Effects.Clear();
        var format = new TuneLabProject();
        using var stream = new MemoryStream();
        format.Serialize(stream, project);
        Assert.DoesNotContain("timelineCanvases", System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    // 拆分（RangeInfo）：每半各拿一份完整的值；锚点挪到子段起点，偏移随之减去，值的绝对原点不动。
    [Fact]
    public void Split_GivesEachHalfTheWholeValue_WithTheOriginKept()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DataThreadContext());
        try { SplitCore(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static void SplitCore()
    {
        var info = new MidiPartInfo { Name = "p", Pos = 480, EndOffset = 1920 };
        info.TimelineCanvases.Add("mix", new TimelineCanvasInfo { Offset = 100, Value = Value("a") });
        var track = new NativeTrackInfo { Name = "t1" };
        track.Parts.Add(info);
        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo { Tracks = { track } }));
        var part = (IMidiPart)document.Project!.Tracks.First().Parts.First();

        var left = part.RangeInfo(0, 960);
        var right = part.RangeInfo(960, 1920);

        Assert.Equal(Value("a"), left.TimelineCanvases["mix"].Value);
        Assert.Equal(Value("a"), right.TimelineCanvases["mix"].Value);
        Assert.Equal(480 + 100, left.Pos + left.TimelineCanvases["mix"].Offset);
        Assert.Equal(480 + 100, right.Pos + right.TimelineCanvases["mix"].Offset);
        Assert.Equal(100 - 960, right.TimelineCanvases["mix"].Offset);
    }

    // 合成侧：写入时报的范围（part 局部 tick）换成全局秒交给代理；没报 = 整条；没被声明的 id（孤儿）不转。
    // 换算器的原点 = part 原点 + 偏移。
    [Fact]
    public void SessionProxies_ForwardRangesInSeconds()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DataThreadContext());
        try { ProxiesCore(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static void ProxiesCore()
    {
        // 120 BPM、480 tick / 拍：480 tick = 0.5 秒。part 原点在 480 tick（0.5 秒）。
        var info = new MidiPartInfo { Name = "p", Pos = 480, EndOffset = 1920 };
        var track = new NativeTrackInfo { Name = "t1" };
        track.Parts.Add(info);
        var document = new ProjectDocument();
        var projectInfo = new ProjectInfo { Tracks = { track } };
        projectInfo.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        document.SetProject(new Project(projectInfo));
        var part = (MidiPart)document.Project!.Tracks.First().Parts.First();

        var declared = new OrderedMap<PropertyKey, TimelineCanvasConfig> { { "mix", TimelineCanvasConfig.Create(_ => null!) } };
        var received = new List<(string Id, double Start, double End)>();
        using var proxies = new TimelineCanvasProxies(part, part.TimelineCanvases, () => declared,
            (proxy, ranges) => { foreach (var (start, end) in ranges) received.Add(("mix", start, end)); });

        part.TimelineCanvases.SetEntry("mix", new TimelineCanvasEntry(-480, Value("a")));
        received.Clear();
        part.TimelineCanvases.SetValue("mix", Value("b"), [new TickRange(480, 960)]);   // 值的 tick → part 的 0~480 → 全局 480~960
        part.TimelineCanvases.SetValue("mix", Value("c"));
        part.TimelineCanvases.SetValue("orphan", Value("x"));

        Assert.Equal(2, received.Count);
        Assert.Equal(0.5, received[0].Start, 9);
        Assert.Equal(1.0, received[0].End, 9);
        Assert.Equal((double.NegativeInfinity, double.PositiveInfinity), (received[1].Start, received[1].End));

        var canvas = proxies.Map["mix"];
        Assert.Equal(Value("c"), canvas.Value);
        Assert.Equal(0.0, canvas.TimeConverter.ToSecond(0), 9);      // 值的原点 = part 原点 − 480 = 全局 0
        Assert.False(proxies.Map.ContainsKey("orphan"));
    }

    // 合并：逐 id 保留按时间顺序第一个有它的段的整值，偏移换到合并后的锚点（值的绝对原点不动）。
    [Fact]
    public void Merge_KeepsTheFirstSegmentWithTheId_AndRebasesItsOffset()
    {
        var first = new MidiPartInfo { Pos = 1000, EndOffset = 1000 };
        var second = new MidiPartInfo { Pos = 2000, EndOffset = 1000 };
        var third = new MidiPartInfo { Pos = 3000, EndOffset = 1000 };
        second.TimelineCanvases.Add("mix", new TimelineCanvasInfo { Offset = -200, Value = Value("second") });
        third.TimelineCanvases.Add("mix", new TimelineCanvasInfo { Value = Value("third") });
        third.TimelineCanvases.Add("only3", new TimelineCanvasInfo { Offset = 10, Value = Value("3") });

        var merged = IMidiPartExtension.MergePartInfos([third, first, second]);

        Assert.Equal(1000, merged.Pos);
        Assert.Equal(Value("second"), merged.TimelineCanvases["mix"].Value);
        Assert.Equal(2000 - 200 - 1000, merged.TimelineCanvases["mix"].Offset);   // 绝对原点 1800
        Assert.Equal(3000 + 10 - 1000, merged.TimelineCanvases["only3"].Offset);  // 绝对原点 3010
    }
}
