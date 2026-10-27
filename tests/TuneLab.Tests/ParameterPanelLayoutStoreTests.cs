using System.Collections.Generic;
using System.IO;
using TuneLab.Data;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.UI;
using Xunit;
using Slot = TuneLab.UI.ParameterPanelLayoutStore.EffectSlot;

namespace TuneLab.Tests;

// 参数面板布局进出工程文件：参数的换算（effect 参数按实例 id → 位置 + 类型认回）与 .tlpx 的读写往返。
public class ParameterPanelLayoutStoreTests
{
    static readonly IReadOnlyList<Slot> Chain = [new("e1", "Gain"), new("e2", "Reverb")];

    static AutomationKey? RoundTrip(AutomationKey key, IReadOnlyList<Slot> saved, IReadOnlyList<Slot> opened)
        => ParameterPanelLayoutStore.ToRef(key, saved) is { } r ? ParameterPanelLayoutStore.Resolve(r, opened) : null;

    [Fact]
    public void NonEffectSourcesRoundTrip()
    {
        foreach (var key in new[] { AutomationKey.Voice("volume"), AutomationKey.NoteLane("vel"), AutomationKey.PhonemeLane("mix") })
            Assert.Equal(key, RoundTrip(key, Chain, []));
    }

    // 链重排过：跟着实例 id 走，不看位置。
    [Fact]
    public void EffectFollowsItsInstanceId()
    {
        IReadOnlyList<Slot> reordered = [new("e2", "Reverb"), new("e1", "Gain")];
        Assert.Equal(AutomationKey.Effect(1, "gain"), RoundTrip(AutomationKey.Effect(0, "gain"), Chain, reordered));
    }

    // 换了 part（实例 id 都不同）：该位置恰是同类型才认。
    [Fact]
    public void EffectFallsBackToPositionAndType()
    {
        IReadOnlyList<Slot> otherPart = [new("x1", "Gain"), new("x2", "Delay")];
        Assert.Equal(AutomationKey.Effect(0, "gain"), RoundTrip(AutomationKey.Effect(0, "gain"), Chain, otherPart));
        Assert.Null(RoundTrip(AutomationKey.Effect(1, "mix"), Chain, otherPart));
    }

    [Fact]
    public void EffectOutOfRangeIsNotSaved()
    {
        Assert.Null(ParameterPanelLayoutStore.ToRef(AutomationKey.Effect(5, "gain"), Chain));
    }

    [Fact]
    public void UnknownSourceIsSkipped()
    {
        Assert.Null(ParameterPanelLayoutStore.Resolve(new ParameterRefInfo { Source = "future", Id = "x" }, Chain));
    }

    [Fact]
    public void TlpxRoundTripsTheLayout()
    {
        var file = new NativeProjectFile();
        file.Editor.PlayheadPos = 480;
        file.Editor.FocusedParameterPanel = 1;
        file.Editor.ParameterPanels =
        [
            new() { Height = 120, Active = new() { Source = "voice", Id = "volume" }, Visible = [new() { Source = "voice", Id = "volume" }] },
            new()
            {
                Height = 80,
                Collapsed = true,
                Active = new() { Source = "effect", Id = "gain", EffectId = "e1", EffectIndex = 0, EffectType = "Gain" },
                VisibleSynthesized = [new() { Source = "voice", Id = "pitch" }],
            },
        ];

        var format = new TuneLabProjectCbor();
        using var stream = new MemoryStream();
        format.SerializeNative(stream, file);
        stream.Position = 0;
        var editor = format.DeserializeNative(stream).Editor;

        Assert.Equal(480, editor.PlayheadPos);
        Assert.Equal(1, editor.FocusedParameterPanel);
        Assert.NotNull(editor.ParameterPanels);
        Assert.Equal(2, editor.ParameterPanels!.Count);
        Assert.Equal(120, editor.ParameterPanels[0].Height);
        Assert.False(editor.ParameterPanels[0].Collapsed);
        Assert.Equal("volume", Assert.Single(editor.ParameterPanels[0].Visible).Id);
        var second = editor.ParameterPanels[1];
        Assert.True(second.Collapsed);
        Assert.Equal(80, second.Height);
        Assert.Equal(("effect", "gain", "e1", 0, "Gain"), (second.Active!.Source, second.Active.Id, second.Active.EffectId, second.Active.EffectIndex, second.Active.EffectType));
        Assert.Equal("pitch", Assert.Single(second.VisibleSynthesized).Id);
    }

    // 没存布局（headless 导出、旧工程）：不写这一项，读回来仍是 null（= 默认单面板）。
    [Fact]
    public void MissingLayoutStaysMissing()
    {
        var format = new TuneLabProjectCbor();
        using var stream = new MemoryStream();
        format.SerializeNative(stream, new NativeProjectFile());
        stream.Position = 0;
        Assert.Null(format.DeserializeNative(stream).Editor.ParameterPanels);
    }
}
