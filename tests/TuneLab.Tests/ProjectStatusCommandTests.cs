using System;
using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// `project status`（搬家前的 get_project_overview）的封条。
//
// 命令面把一条命令切成两半：结构化 Data（给 CLI --json 与 CI 断言）+ Render 文本（给人与模型）。
// 这里两半都钉住：
//  · Data 的字段名与 1-based 编号——CI 与 --json 的消费者按它写断言，改了就是破坏性变更；
//  · Render 的措辞【逐字】——搬家的验收标准是"内置 agent 行为不变"，措辞就是那个行为的可见部分。
//    肉眼比对靠不住，故固定在这里。
public class ProjectStatusCommandTests
{
    // 内建空音源引擎（建 MidiPart 需要它兜底）。走共享 helper 而非直接 LoadBuiltIn：并发调用不安全，
    // 见 TestVoices。
    static ProjectStatusCommandTests()
    {
        TestVoices.EnsureBuiltIn();
    }

    // 两条轨：第一条 mute + 有 gain/pan + 一个 3 音符的 part，第二条空（覆盖 flags 有/无两个分支）。
    static IProject SampleProject()
    {
        var part = new MidiPartInfo { Name = "p", Pos = 0, StartOffset = 0, EndOffset = 1920 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "do" });
        part.Notes.Add(new NoteInfo { Pos = 480, Dur = 480, Pitch = 62, Lyric = "re" });
        part.Notes.Add(new NoteInfo { Pos = 960, Dur = 480, Pitch = 64, Lyric = "mi" });

        var track = new NativeTrackInfo { Name = "vocal", Gain = -2, Pan = 0.5, Mute = true };
        track.Parts.Add(part);

        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo
        {
            Tempos = { new TempoInfo { Pos = 0, Bpm = 120 } },
            TimeSignatures = { new TimeSignatureInfo { BarIndex = 0, Numerator = 4, Denominator = 4 } },
            Tracks = { track, new TrackInfo { Name = "empty" } },
        }));
        return document.Project!;
    }

    static (JsonNode? Data, string Text) Run(IProject? project)
    {
        var command = new ProjectStatusCommand();
        var result = command.ExecuteAsync(CommandArgs.Empty, new CommandContext { Project = project }, CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert.False(result.IsError, result.Error?.Message);
        return (result.Data, command.Render(result.Data, CommandArgs.Empty));
    }

    [Fact]
    public void RenderKeepsTheWordingItHadBeforeTheMoveToTheCommandSurface()
    {
        var (_, text) = Run(SampleProject());

        var expected = string.Join(Environment.NewLine, [
            "Project: PPQ=480 (ticks per quarter note). Positions/durations are in ticks.",
            "Tempo: 120bpm@tick0",
            "Time signature: 4/4@bar1",
            "Tracks (2):",
            "  Track 1: \"vocal\" [mute], gain=-2dB, pan=0.5, parts=1, notes=3",
            "  Track 2: \"empty\", gain=0dB, pan=0, parts=0, notes=0",
            "",
        ]);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void DataCarriesTheFactsCliAndCiAssertOn()
    {
        var (data, _) = Run(SampleProject());
        var obj = Assert.IsType<JsonObject>(data);

        Assert.Equal(480, obj["ppq"]!.GetValue<int>());

        var tempos = obj["tempos"]!.AsArray();
        Assert.Single(tempos);
        Assert.Equal(120, tempos[0]!["bpm"]!.GetValue<double>());

        var sigs = obj["timeSignatures"]!.AsArray();
        Assert.Single(sigs);
        Assert.Equal(1, sigs[0]!["bar"]!.GetValue<int>());   // 1-based，与呈现一致

        var tracks = obj["tracks"]!.AsArray();
        Assert.Equal(2, tracks.Count);
        Assert.Equal(1, tracks[0]!["number"]!.GetValue<int>());   // 1-based
        Assert.Equal("vocal", tracks[0]!["name"]!.GetValue<string>());
        Assert.True(tracks[0]!["mute"]!.GetValue<bool>());
        Assert.False(tracks[0]!["solo"]!.GetValue<bool>());
        Assert.Equal(3, tracks[0]!["notes"]!.GetValue<int>());
        Assert.Equal(0, tracks[1]!["notes"]!.GetValue<int>());
    }

    // 没有工程时如实报错，而不是让命令从工具面消失——后者让调用方连"为什么没有"都问不到。
    [Fact]
    public void FailsWithAnExplanationWhenNoProjectIsOpen()
    {
        var result = new ProjectStatusCommand()
            .ExecuteAsync(CommandArgs.Empty, new CommandContext { Project = null }, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("no_project", result.Error!.Value.Code);
    }
}
