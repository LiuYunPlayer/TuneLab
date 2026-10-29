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
        var configs = Path.Combine(Path.GetTempPath(), "TuneLab.Tests", Guid.NewGuid().ToString("N"));
        ParameterPinning.Init(Path.Combine(configs, "ParameterPins.json"));
        ParameterHiding.Init(Path.Combine(configs, "ParameterHidden.json"));
    }

    sealed class ShapeEngine : IVoiceSynthesisEngine
    {
        public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos { get; } = new OrderedMap<string, VoiceSourceInfo> { { "v", new VoiceSourceInfo { Name = "v", Description = "" } } };
        public void Init() { }
        public void Destroy() { }
        public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context) => mEmpty.CreateSession(context);
        public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context)
            => new OrderedMap<PropertyKey, AutomationConfig> { { "legacy", AutomationConfig.Create(0, 1).WithDefault(0).WithShownByDefault(false) } };
        public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => new OrderedMap<PropertyKey, AutomationConfig>();
        public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
        // 高音 note 的核音素（slot 0）声明一个时间轴画布。
        public IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context)
        {
            var map = new Map<int, ObjectConfig>();
            if (context.Notes.Count > 0 && context.Notes.All(n => n.Pitch >= 62))
                map.Add(0, ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig> { { "ptimeline", TimelineCanvasConfig.Create(PropertyObject.Empty, _ => new NullControl()) } }));
            return map;
        }

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

    // note 声明随时问得到：哪个 note 都不再声明时间轴画布，标签就去掉；再被声明时回来，颜色沿用上次的。
    [Fact]
    public void NoteTimelineCanvasGoesWhenNoNoteDeclaresIt_AndComesBackInTheSameColor()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var color = part.NoteLaneConfigs["timeline"].Color;
            part.Notes.Last().Pitch.Set(60);   // 两个 note 都是低音了：谁都不声明 timeline
            part.Commit();
            Assert.False(part.NoteLaneConfigs.ContainsKey("timeline"));

            part.Notes.Last().Pitch.Set(64);
            part.Commit();
            Assert.True(part.NoteLaneConfigs.TryGetValue("timeline", out var back));
            Assert.Equal(color, back.Color);
        });
    }

    // 编辑进行中（文档里有未提交的改动，如拖动）不重新询问单 note 声明，落定（提交）后才问：画一笔不至于逐帧把全部声明重问一遍。
    [Fact]
    public void NoteDeclarationsAreAskedAgainOnlyWhenTheEditSettles()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var high = part.Notes.Last();
            high.Pitch.Set(63);   // 开始编辑（第一笔到达时文档里还没有未提交的命令，照常作废）
            var before = part.GetNotePropertyConfig(high);
            high.Pitch.Set(60);   // 还在编辑：上一笔没提交——不重问
            high.Pitch.Set(61);
            Assert.Same(before, part.GetNotePropertyConfig(high));
            part.Commit();
            Assert.NotSame(before, part.GetNotePropertyConfig(high));
            Assert.False(part.TryGetNoteLaneShape(high, "adv", out _));   // 落定后按低音的声明：没有 adv
        });
    }

    // 音素声明要等引擎对音素表态：还有 note 没表态（这里从没合成过）时，出现过的音素级时间轴画布保留，音源一换才清。
    [Fact]
    public void PhonemeTimelineCanvasStaysWhileSomeNoteHasNoPhonemeAnswer()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var color = part.PhonemeLaneConfigs["ptimeline"].Color;
            part.Notes.Last().Pitch.Set(60);
            part.RefreshPinnedLaneConfigs();
            Assert.True(part.PhonemeLaneConfigs.TryGetValue("ptimeline", out var kept));
            Assert.Equal(color, kept.Color);

            part.SoundSource.SetInfo(new SoundSourceInfo());   // 换成空音源
            part.RefreshPinnedLaneConfigs();
            Assert.False(part.PhonemeLaneConfigs.ContainsKey("ptimeline"));
        });
    }

    // 常驻的（自动化轨、时间轴画布）能从标签栏隐藏，按声源记、终态幂等；钉选的 lane 不归隐藏管（不想要就解钉）。
    [Fact]
    public void ResidentParametersCanBeHidden_PinnedLanesCannot()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var volume = AutomationKey.Voice(ConstantDefine.PreCommonAutomationConfigs[0].Key.Id);
            var timeline = AutomationKey.NoteLane("timeline");
            try
            {
                Assert.True(ParameterHiding.SetHidden(part, volume, true));
                Assert.False(ParameterHiding.SetHidden(part, volume, true));   // 已在目标态：什么都不做
                Assert.True(ParameterHiding.IsHidden(part, volume));
                Assert.True(ParameterHiding.IsHidden(SamplePart(), volume));   // 同一个声源的别的 part 也藏着

                Assert.True(ParameterHiding.SetHidden(part, timeline, true));
                Assert.True(ParameterHiding.IsHidden(part, timeline));

                WithPinned(part, "side", () =>
                {
                    Assert.False(ParameterHiding.IsHideable(part, AutomationKey.NoteLane("side")));
                    Assert.False(ParameterHiding.SetHidden(part, AutomationKey.NoteLane("side"), true));
                });
            }
            finally
            {
                ParameterHiding.SetHidden(part, volume, false);
                ParameterHiding.SetHidden(part, timeline, false);
            }
            Assert.False(ParameterHiding.IsHidden(part, volume));
        });
    }

    // 声明说默认不显示：用户没设过就藏着；用户显式显示 / 隐藏过就按用户的。已在目标态的操作不记成显式（仍跟着声明走）。
    [Fact]
    public void ShownByDefaultIsOnlyTheDefault_TheUsersChoiceWins()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var legacy = AutomationKey.Voice("legacy");
            var volume = AutomationKey.Voice(ConstantDefine.PreCommonAutomationConfigs[0].Key.Id);
            try
            {
                Assert.True(ParameterHiding.IsHidden(part, legacy));
                Assert.False(ParameterHiding.SetHidden(part, legacy, true));   // 本来就藏着：什么都不做
                Assert.True(ParameterHiding.SetHidden(part, legacy, false));   // 用户打开
                Assert.False(ParameterHiding.IsHidden(part, legacy));
                Assert.False(ParameterHiding.IsHidden(SamplePart(), legacy));  // 按声源记

                Assert.False(ParameterHiding.SetHidden(part, volume, false));  // 默认显示的、本来就显示
                Assert.False(ParameterHiding.IsHidden(part, volume));
            }
            finally
            {
                ParameterHiding.SetHidden(part, legacy, true);
            }
            Assert.True(ParameterHiding.IsHidden(part, legacy));
        });
    }
}
