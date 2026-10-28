using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 参数栏 lane 按每个 note 自己的声明取形状（量程 / 字段在不在），而不是拿整个 part 合并出的一份套到所有 note 上。
// 测试引擎的声明随所选 note 的值变：选区里全是高音（≥ 62）时多出 adv、level 的量程是 0..100，否则没有 adv、level 是 0..10。
// 整个 part 合并来问时（一高一低）adv 消失、level 只到 10——这正是旧 lane 的口径，单选高音 note 时侧栏里看得见的字段钉了却不出 lane。
[Collection("DataThreadProject")]   // 见 FinalPronunciationTests 的 CollectionDefinition
public class NoteLaneShapeTests
{
    const string EngineType = "TLTestLaneShape";

    static NoteLaneShapeTests()
    {
        TestVoices.EnsureBuiltIn();
        VoicesManager.RegisterEngine("tests.lane-shape", EngineType, "Lane Shape Test", new ShapeEngine());
        ParameterPinning.Init(Path.Combine(Path.GetTempPath(), "TuneLab.Tests", Guid.NewGuid().ToString("N"), "ParameterPins.json"));
    }

    sealed class ShapeEngine : IVoiceSynthesisEngine
    {
        public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos { get; } = new OrderedMap<string, VoiceSourceInfo> { { "v", new VoiceSourceInfo { Name = "v", Description = "" } } };
        public void Init() { }
        public void Destroy() { }
        public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context) => mEmpty.CreateSession(context);
        public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => new OrderedMap<PropertyKey, AutomationConfig>();
        public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => new OrderedMap<PropertyKey, AutomationConfig>();
        public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
        public IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context) => [];

        public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context)
        {
            DeclarationCount++;
            bool high = context.Notes.Count > 0 && context.Notes.All(n => n.Pitch >= 62);
            var map = new OrderedMap<PropertyKey, IControllerConfig> { { "level", SliderConfig.Linear(0, 0, high ? 100 : 10) } };
            if (high)
            {
                map.Add("adv", SliderConfig.Linear(0, -12, 12));
                map.Add("timeline", TimelineCanvasConfig.Create(PropertyObject.Empty, _ => new NullControl()));
            }
            map.Add("side", CanvasConfig.Create(PropertyObject.Empty, 200, 80, _ => new NullControl()));
            return ObjectConfig.Create(map);
        }

        readonly EmptyVoiceSynthesisEngine mEmpty = new();
    }

    static int DeclarationCount;

    sealed class NullControl : ICanvasControl { }

    sealed class DataThreadContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }

    static void OnDataThread(Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DataThreadContext());
        try { action(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    // 一低（60）一高（64）。
    static IMidiPart SamplePart()
    {
        var part = new MidiPartInfo { Name = "p", Pos = 0, StartOffset = 0, EndOffset = 1920, SoundSource = new() { Type = EngineType, Id = "v" } };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "la" });
        part.Notes.Add(new NoteInfo { Pos = 480, Dur = 480, Pitch = 64, Lyric = "la" });
        var track = new NativeTrackInfo { Name = "t1" };
        track.Parts.Add(part);
        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo { Tracks = { track } }));
        return (IMidiPart)document.Project!.Tracks.First().Parts.First();
    }

    static void WithPinned(IMidiPart part, string id, Action action)
    {
        ParameterPinning.SetPinned(part, ParameterPinKind.NoteProperty, id, true);
        try { action(); }
        finally { ParameterPinning.SetPinned(part, ParameterPinKind.NoteProperty, id, false); }
    }

    [Fact]
    public void FieldDeclaredByOnlySomeNotes_StillGetsALane_OnlyOnThoseNotes()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var (low, high) = (part.Notes.First(), part.Notes.Last());
            WithPinned(part, "adv", () =>
            {
                Assert.True(part.NoteLaneConfigs.ContainsKey("adv"));
                Assert.False(part.TryGetNoteLaneShape(low, "adv", out _));
                Assert.True(part.TryGetNoteLaneShape(high, "adv", out var shape));
                Assert.Equal((-12.0, 12.0), (shape.MinValue, shape.MaxValue));
            });
        });
    }

    [Fact]
    public void EachNoteUsesItsOwnRange()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var (low, high) = (part.Notes.First(), part.Notes.Last());
            WithPinned(part, "level", () =>
            {
                Assert.True(part.TryGetNoteLaneShape(low, "level", out var lowShape));
                Assert.True(part.TryGetNoteLaneShape(high, "level", out var highShape));
                Assert.Equal(10.0, lowShape.MaxValue);
                Assert.Equal(100.0, highShape.MaxValue);
            });
        });
    }

    // 改了数据立刻按新值回答（不等提交）：声明 context 经 Part 读得到整个 part，任何一处变了缓存整体作废。
    [Fact]
    public void DataChangeInvalidatesTheCache()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var low = part.Notes.First();
            Assert.False(part.TryGetNoteLaneShape(low, "adv", out _));
            low.Pitch.Set(67);
            Assert.True(part.TryGetNoteLaneShape(low, "adv", out _));
        });
    }

    // 单 note 声明按 note 缓存：没有数据变化时重复取不再问插件。
    [Fact]
    public void SingleNoteDeclarationIsCachedUntilDataChanges()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var high = part.Notes.Last();
            var first = part.GetNotePropertyConfig(high);
            int asked = DeclarationCount;
            Assert.Same(first, part.GetNotePropertyConfig(high));
            Assert.True(part.TryGetNoteLaneShape(high, "level", out _));
            Assert.Equal(asked, DeclarationCount);

            high.Pitch.Set(65);
            Assert.NotSame(first, part.GetNotePropertyConfig(high));
        });
    }

    // 时间轴画布常驻：只有部分 note 声明也进 lane 集合，不用钉；颜色没指定就由宿主分配（实色）。
    [Fact]
    public void TimelineCanvasIsAlwaysALane_WithAHostAssignedColor()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            Assert.True(part.NoteLaneConfigs.TryGetValue("timeline", out var lane));
            Assert.Equal(LaneKind.TimelineCanvas, lane.Kind);
            Assert.False(TuneLab.Configs.TrackColors.IsUnassigned(lane.Color));
            Assert.False(part.IsNumberLane(AutomationKey.NoteLane("timeline")));
            // 画布 lane 没有数值形状：数值的绘制 / 拖写不碰它。
            Assert.False(part.TryGetNoteLaneShape(part.Notes.Last(), "timeline", out _));
        });
    }

    // 侧栏画布要钉了才进；钉了是 Canvas 种类。
    [Fact]
    public void SidePanelCanvasIsALaneOnlyWhenPinned()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            Assert.False(part.NoteLaneConfigs.ContainsKey("side"));
            Assert.Contains(part.PinnableProperties(), p => p.Key.Id == "side");
            Assert.DoesNotContain(part.PinnableProperties(), p => p.Key.Id == "timeline");
            WithPinned(part, "side", () =>
            {
                Assert.True(part.NoteLaneConfigs.TryGetValue("side", out var lane));
                Assert.Equal(LaneKind.Canvas, lane.Kind);
            });
        });
    }

    // 时间轴画布出现过就一直在：哪个 note 都不再声明它时标签仍在（颜色不变），音源一换才清。
    [Fact]
    public void SeenTimelineCanvasStaysUntilTheSourceChanges()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var color = part.NoteLaneConfigs["timeline"].Color;
            part.Notes.Last().Pitch.Set(60);   // 两个 note 都是低音了：谁都不声明 timeline
            part.RefreshPinnedLaneConfigs();
            Assert.True(part.NoteLaneConfigs.TryGetValue("timeline", out var kept));
            Assert.Equal(color, kept.Color);

            part.SoundSource.SetInfo(new SoundSourceInfo());   // 换成空音源
            part.RefreshPinnedLaneConfigs();
            Assert.False(part.NoteLaneConfigs.ContainsKey("timeline"));
        });
    }
}
