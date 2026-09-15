using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using TuneLab.Data;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// `project synthesize` / `project synthesis-status` 的封条。
//
// 测试进程里注册的是**空引擎**（TestVoices），故这里跑不出真的合成产物——那正好是本组用例要守的那条
// 边界：没有产出时，这条命令绝不许把"我等完了"说成"合成好了"。真引擎下的时序（派活、在飞、落定）
// 由 headless 跑批实测覆盖，不在单元测试里假造。
[Collection("DataThreadProject")]   // 见 FinalPronunciationTests 的 CollectionDefinition
public class SynthesisCommandsTests
{
    static SynthesisCommandsTests()
    {
        TestVoices.EnsureBuiltIn();
    }

    static readonly ProjectSynthesizeCommand Synthesize = new();
    static readonly ProjectSynthesisStatusCommand Status = new();

    // 第 1 轨两个 part（第 2 个带音符），第 2 轨一个。
    static IProject SampleProject()
    {
        var intro = new MidiPartInfo { Name = "intro", Pos = 0, StartOffset = 0, EndOffset = 1920 };

        var chorus = new MidiPartInfo { Name = "chorus", Pos = 1920, StartOffset = 0, EndOffset = 1920 };
        chorus.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "do" });
        chorus.Notes.Add(new NoteInfo { Pos = 480, Dur = 480, Pitch = 62, Lyric = "re" });

        var lead = new NativeTrackInfo { Name = "lead" };
        lead.Parts.Add(intro);
        lead.Parts.Add(chorus);

        var harmony = new NativeTrackInfo { Name = "harmony" };
        harmony.Parts.Add(new MidiPartInfo { Name = "pad", Pos = 0, StartOffset = 0, EndOffset = 3840 });

        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo
        {
            Tempos = { new TempoInfo { Pos = 0, Bpm = 120 } },
            TimeSignatures = { new TimeSignatureInfo { BarIndex = 0, Numerator = 4, Denominator = 4 } },
            Tracks = { lead, harmony },
        }));
        return document.Project!;
    }

    // 有编辑器的进程长什么样，这条命令只看"给不给得出 EditorStatus"，故替身的取值本身无关紧要。
    sealed class StubEditorStatus : IEditorStatusAccess
    {
        public bool IsPlaying => false;
        public double PlayheadTime => 0;
        public double? PlayheadTick => 0;
        public double EndTime => 0;
        public string CurrentToolActionId => "tool.note";
        public bool IsParameterPanelVisible => false;
        public bool IsWaveformVisible => false;
        public string? SidebarPanelActionId => null;
        public IReadOnlyList<string> BlockingDialogs => [];
        public string? FocusedSurface => null;
    }

    static (CommandResult Result, string Text) Run(ICommand command, string json, IProject? project, IEditorStatusAccess? editor = null)
    {
        var result = command.ExecuteAsync(CommandArgs.Parse(json), new CommandContext { Project = project, EditorStatus = editor }, CancellationToken.None)
            .GetAwaiter().GetResult();
        return (result, result.IsError ? string.Empty : command.Render(result.Data, CommandArgs.Empty));
    }

    // ── headless 才驱动合成

    // 这条限制与 project save / open 方向相反（那两条缺了编辑器做不到，这一条有编辑器才不该做），
    // 是命令面上「有编辑器 → 不可用」的第一例，故单独封住。
    [Fact]
    public void SynthesizeRefusesWhereThereIsAnEditor()
    {
        var (result, _) = Run(Synthesize, "{}", SampleProject(), new StubEditorStatus());
        Assert.True(result.IsError);
        Assert.Equal("has_editor", result.Error!.Value.Code);
        Assert.Contains("--headless", result.Error!.Value.Message);
    }

    [Fact]
    public void SynthesizeRunsWhereThereIsNoEditor()
    {
        var (result, _) = Run(Synthesize, "{}", SampleProject());
        Assert.False(result.IsError);
    }

    // 查状态是纯读、零代价，故两边都给——外部工具因此能问"用户那份工程合成完了没"。
    [Fact]
    public void StatusWorksWithOrWithoutAnEditor()
    {
        Assert.False(Run(Status, "{}", SampleProject(), new StubEditorStatus()).Result.IsError);
        Assert.False(Run(Status, "{}", SampleProject()).Result.IsError);
    }

    [Fact]
    public void BothRefuseWithoutAProject()
    {
        Assert.Equal("no_project", Run(Synthesize, "{}", null).Result.Error!.Value.Code);
        Assert.Equal("no_project", Run(Status, "{}", null).Result.Error!.Value.Code);
    }

    // ── 范围参数

    [Fact]
    public void PartNeedsTrack()
    {
        Assert.Equal("part_needs_track", Run(Synthesize, """{"part": 1}""", SampleProject()).Result.Error!.Value.Code);
    }

    // 成对或都不给，与脚本面的范围参数同一条纪律。
    [Fact]
    public void TimeWindowComesInPairs()
    {
        var project = SampleProject();
        Assert.Equal("half_a_window", Run(Synthesize, """{"startSeconds": 1}""", project).Result.Error!.Value.Code);
        Assert.Equal("half_a_window", Run(Synthesize, """{"endSeconds": 1}""", project).Result.Error!.Value.Code);
    }

    [Fact]
    public void BackwardsTimeWindowRefused()
    {
        Assert.Equal("empty_window",
            Run(Synthesize, """{"startSeconds": 9, "endSeconds": 2}""", SampleProject()).Result.Error!.Value.Code);
    }

    [Fact]
    public void UnknownTrackOrPartRefused()
    {
        var project = SampleProject();
        Assert.Equal("no_such_track", Run(Synthesize, """{"track": 9}""", project).Result.Error!.Value.Code);
        Assert.Equal("no_such_part", Run(Synthesize, """{"track": 1, "part": 7}""", project).Result.Error!.Value.Code);
    }

    // ── 范围真的收窄了

    [Fact]
    public void ScopeNarrowsToOneTrack()
    {
        var (result, _) = Run(Status, """{"track": 1}""", SampleProject());
        var parts = result.Data!["parts"]!.AsArray();

        Assert.Equal(2, parts.Count);   // 第 1 轨的两个 part，第 2 轨的不在内
        Assert.All(parts, part => Assert.StartsWith("track 1 ", part!["where"]!.GetValue<string>()));
    }

    [Fact]
    public void ScopeNarrowsToOnePart()
    {
        var (result, _) = Run(Status, """{"track": 1, "part": 2}""", SampleProject());
        var parts = result.Data!["parts"]!.AsArray();

        Assert.Single(parts);
        Assert.Equal("track 1 part 2 (\"chorus\")", parts[0]!["where"]!.GetValue<string>());
    }

    // 编号含音频 part，与 get_project_overview / preset 那一族同一套——编号一旦各数各的，
    // 调用方就会指到别的 part 上去。
    [Fact]
    public void ScopeDescribesItselfInTheCallersOwnNumbers()
    {
        var (result, _) = Run(Synthesize, """{"track": 2}""", SampleProject());
        Assert.Equal("track 2", result.Data!["scope"]!.GetValue<string>());

        var (windowed, _) = Run(Synthesize, """{"track": 1, "part": 1, "startSeconds": 1.5, "endSeconds": 3}""", SampleProject());
        Assert.Equal("track 1 part 1, 1.5s–3s", windowed.Data!["scope"]!.GetValue<string>());
    }

    // ── 落定 ≠ 合成好了

    // 空引擎下一个 part 都产不出内容。这一条封的就是那句话不许被说成成功：跑批里音源在这个进程里
    // 不可用是最常见的失败，而它恰恰会让 Done 为真。
    [Fact]
    public void SettlingWithNoOutputIsNotReportedAsSuccess()
    {
        var (result, text) = Run(Synthesize, "{}", SampleProject());

        Assert.Equal("done", result.Data!["outcome"]!.GetValue<string>());
        Assert.Equal(0, result.Data!["partsWithOutput"]!.GetValue<int>());
        Assert.Equal(3, result.Data!["parts"]!.GetValue<int>());

        Assert.Contains("NOT ONE", text);
        Assert.Contains("list_extensions", text);   // 指路：最常见的原因是音源不可用
    }

    // 只看、不开工：查状态不该顺手把合成派出去，否则读命令变成了写命令。
    [Fact]
    public void StatusStartsNothing()
    {
        var project = SampleProject();
        Run(Status, "{}", project);

        var (after, _) = Run(Status, "{}", project);
        Assert.Equal(0, after.Data!["busy"]!.GetValue<int>());
    }
}
