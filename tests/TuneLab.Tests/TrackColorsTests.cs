using System;
using System.Linq;
using System.Threading;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 轨色：插件没指定（AutomationConfig.Color 默认全透明）就由宿主从调色板分配；在物化声明的入口统一做，下游读到的永远是实色。
[Collection("DataThreadProject")]   // 见 FinalPronunciationTests 的 CollectionDefinition
public class TrackColorsTests
{
    const string EngineType = "TLTestTrackColors";

    static TrackColorsTests()
    {
        TestVoices.EnsureBuiltIn();
        VoicesManager.RegisterEngine("tests.track-colors", EngineType, "Track Colors Test", new ColorEngine());
    }

    // 两条不指定颜色、一条指定了红色的轨；回显轨一条与可编辑轨同 id、一条独有，都不指定颜色。
    sealed class ColorEngine : IVoiceSynthesisEngine
    {
        public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos { get; } = new OrderedMap<string, VoiceSourceInfo> { { "v", new VoiceSourceInfo { Name = "v", Description = "" } } };
        public void Init() { }
        public void Destroy() { }
        public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context) => mEmpty.CreateSession(context);
        public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => new OrderedMap<PropertyKey, AutomationConfig>
        {
            { "a", AutomationConfig.Create(0, 1).WithDefault(0) },
            { "red", AutomationConfig.Create(0, 1).WithColor("#F1595B").WithDefault(0) },
            { "b", AutomationConfig.Create(0, 1).WithDefault(0) },
        };
        public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => new OrderedMap<PropertyKey, AutomationConfig>
        {
            { "a", AutomationConfig.Create(0, 1) },
            { "echo", AutomationConfig.Create(0, 1) },
        };
        public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
        public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
        public IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context) => [];

        readonly EmptyVoiceSynthesisEngine mEmpty = new();
    }

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

    static IMidiPart SamplePart()
    {
        var part = new MidiPartInfo { Name = "p", Pos = 0, StartOffset = 0, EndOffset = 1920, SoundSource = new() { Type = EngineType, Id = "v" } };
        var track = new NativeTrackInfo { Name = "t1" };
        track.Parts.Add(part);
        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo { Tracks = { track } }));
        return (IMidiPart)document.Project!.Tracks.First().Parts.First();
    }

    [Fact]
    public void DefaultColorIsTransparent_MeaningUnassigned()
    {
        Assert.True(TrackColors.IsUnassigned(AutomationConfig.Create(0, 1).Color));
        Assert.True(TrackColors.IsUnassigned("#00F1595B"));   // alpha 为 0 一律算不指定
        Assert.False(TrackColors.IsUnassigned("#F1595B"));
        Assert.False(TrackColors.IsUnassigned("#80F1595B"));
        Assert.False(TrackColors.IsUnassigned("not a color"));
    }

    [Fact]
    public void UnassignedTracksGetDistinctPaletteColors_ExplicitOnesKept()
    {
        OnDataThread(() =>
        {
            var configs = SamplePart().SoundSource.AutomationConfigs;
            Assert.All(configs, kvp => Assert.False(TrackColors.IsUnassigned(kvp.Value.Color)));
            Assert.Equal("#F1595B", configs["red"].Color);
            var colors = configs.Select(kvp => kvp.Value.Color).ToList();
            Assert.Equal(colors.Count, colors.Distinct().Count());
        });
    }

    [Fact]
    public void AssignmentIsDeterministic()
    {
        OnDataThread(() =>
        {
            var first = SamplePart().SoundSource.AutomationConfigs.Select(kvp => kvp.Value.Color).ToList();
            var second = SamplePart().SoundSource.AutomationConfigs.Select(kvp => kvp.Value.Color).ToList();
            Assert.Equal(first, second);
        });
    }

    // 回显轨与同 id 的可编辑轨同色；独有的再分一个，不与可编辑轨撞色。
    [Fact]
    public void SynthesizedTrackTakesItsEditableTwinsColor()
    {
        OnDataThread(() =>
        {
            var source = SamplePart().SoundSource;
            Assert.Equal(source.AutomationConfigs["a"].Color, source.SynthesizedParameterConfigs["a"].Color);
            var echo = source.SynthesizedParameterConfigs["echo"].Color;
            Assert.False(TrackColors.IsUnassigned(echo));
            Assert.DoesNotContain(echo, source.AutomationConfigs.Select(kvp => kvp.Value.Color));
        });
    }
}
