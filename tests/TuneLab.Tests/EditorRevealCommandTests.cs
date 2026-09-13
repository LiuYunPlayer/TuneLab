using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using TuneLab.Data;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// `editor reveal` 的封条。
//
// 这条命令是 §11「视口不收」的部分翻案，故它的价值全在**边界守得住**：它只改用户此刻看到什么，
// 唯一有后果的那件事（切钢琴窗的 part，改的是用户的编辑目标）默认不做、要显式要，而且**不做的时候
// 要说出来**——否则调用方以为音符已经给用户看到了，其实钢琴窗还开着别的。
//
// 【不起窗口】轴的算术在 TuneLab.GUI 的 AnimateReveal 里，这里用替身把"哪几条轴被要求挪、挪到哪"
// 记下来：命令负责的是解析定位、决定动哪条轴、以及如实回报。
[Collection("DataThreadProject")]   // 见 FinalPronunciationTests 的 CollectionDefinition
public class EditorRevealCommandTests
{
    static EditorRevealCommandTests()
    {
        TestVoices.EnsureBuiltIn();
    }

    static readonly EditorRevealCommand Command = new();

    // 两轨：第 1 轨有两个 part（第 2 个带三个音符，音域 60–67），第 2 轨空。
    static IProject SampleProject()
    {
        var empty = new MidiPartInfo { Name = "intro", Pos = 0, StartOffset = 0, EndOffset = 1920 };

        var tune = new MidiPartInfo { Name = "chorus", Pos = 1920, StartOffset = 0, EndOffset = 1920 };
        tune.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "do" });
        tune.Notes.Add(new NoteInfo { Pos = 480, Dur = 480, Pitch = 67, Lyric = "so" });
        tune.Notes.Add(new NoteInfo { Pos = 1440, Dur = 480, Pitch = 62, Lyric = "re" });

        var track = new NativeTrackInfo { Name = "vocal" };
        track.Parts.Add(empty);
        track.Parts.Add(tune);

        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo
        {
            Tempos = { new TempoInfo { Pos = 0, Bpm = 120 } },
            TimeSignatures = { new TimeSignatureInfo { BarIndex = 0, Numerator = 4, Denominator = 4 } },
            Tracks = { track, new TrackInfo { Name = "empty" } },
        }));
        return document.Project!;
    }

    sealed class StubView : IEditorViewAccess
    {
        public IPart? EditingPart { get; set; }
        public (double Start, double End)? Ticks;
        public int? Track;
        public (double Low, double High)? Pitches;
        public IPart? Opened;
        // 挪完之后视野里剩下什么由真实的轴算（AnimateReveal），这里让替身直接给出答案。
        public (double Start, double End) VisibleTicks { get; set; } = (0, 3840);

        public (double Start, double End) RevealTicks(double startTick, double endTick)
        {
            Ticks = (startTick, endTick);
            return VisibleTicks;
        }
        public void RevealTrack(int trackNumber) => Track = trackNumber;
        public void RevealPitches(double minPitch, double maxPitch) => Pitches = (minPitch, maxPitch);
        public void OpenPart(IPart part) { Opened = part; EditingPart = part; }
    }

    static (CommandResult Result, string Text) Run(string json, IProject? project, IEditorViewAccess? view)
    {
        var result = Command.ExecuteAsync(CommandArgs.Parse(json), new CommandContext { Project = project, EditorView = view }, CancellationToken.None)
            .GetAwaiter().GetResult();
        return (result, result.IsError ? string.Empty : Command.Render(result.Data, CommandArgs.Empty));
    }

    static IPart PartOf(IProject project, int track, int part)
    {
        var parts = new List<IPart>(project.Tracks[track - 1].Parts);
        return parts[part - 1];
    }

    // ── 没有视野可挪 / 说不清要看哪里

    [Fact]
    public void WithoutAnEditorThereIsNoViewToMove()
    {
        var (result, _) = Run("{}", SampleProject(), null);
        Assert.Equal("no_editor", result.Error!.Value.Code);
        Assert.Contains("no view to move", result.Error!.Value.Message);
    }

    [Fact]
    public void SayingNothingAtAllIsRefusedInsteadOfGuessing()
    {
        var (result, _) = Run("{}", SampleProject(), new StubView());
        Assert.Equal("nothing_named", result.Error!.Value.Code);
    }

    [Fact]
    public void APartNumberWithoutATrackIsAmbiguousAndSaysSo()
    {
        var (result, _) = Run("""{"part": 2}""", SampleProject(), new StubView());
        Assert.Equal("part_needs_track", result.Error!.Value.Code);
    }

    [Fact]
    public void OutOfRangeLocatorsSayHowManyThereAre()
    {
        var project = SampleProject();
        Assert.Equal("no_such_track", Run("""{"track": 9}""", project, new StubView()).Result.Error!.Value.Code);

        var (part, _) = Run("""{"track": 1, "part": 7}""", project, new StubView());
        Assert.Equal("no_such_part", part.Error!.Value.Code);
        Assert.Contains("it has 2", part.Error!.Value.Message);
    }

    // ── 定位：part 给出范围，显式 tick 更具体故盖过它

    [Fact]
    public void APartRevealsItsOwnSpan()
    {
        var project = SampleProject();
        var view = new StubView();
        Run("""{"track": 1, "part": 2}""", project, view);

        Assert.Equal((1920d, 3840d), view.Ticks);
        Assert.Equal(1, view.Track);
    }

    [Fact]
    public void ExplicitTicksWinBecauseTheyAreMoreSpecific()
    {
        var view = new StubView();
        Run("""{"track": 1, "part": 2, "startTick": 2400, "endTick": 2600}""", SampleProject(), view);

        Assert.Equal((2400d, 2600d), view.Ticks);
    }

    // 只给一个 tick 就是一个点——不该被读成"从这里到无穷"。
    [Fact]
    public void ASingleTickIsAPointNotAnOpenRange()
    {
        var view = new StubView();
        var (_, text) = Run("""{"startTick": 960}""", SampleProject(), view);

        Assert.Equal((960d, 960d), view.Ticks);
        Assert.Contains("at tick 960", text);
        Assert.Null(view.Track);   // 没说轨道就不动纵向
    }

    // ── 音高轴只在钢琴窗真开着目标 part 时才动

    [Fact]
    public void ThePitchAxisMovesOnlyWhenThePianoRollIsOnThatPart()
    {
        var project = SampleProject();
        var target = PartOf(project, 1, 2);

        var closed = new StubView();
        Run("""{"track": 1, "part": 2}""", project, closed);
        Assert.Null(closed.Pitches);

        var opened = new StubView { EditingPart = target };
        Run("""{"track": 1, "part": 2}""", project, opened);
        Assert.Equal((60d, 67d), opened.Pitches);
    }

    // 指定的范围里恰好没有音符时退回整个 part 的音域：用户要看的是"这一带"，
    // 而不是"这一带恰好有没有音符"。
    [Fact]
    public void AnEmptyRangeFallsBackToThePartsOwnPitchSpan()
    {
        var project = SampleProject();
        var view = new StubView { EditingPart = PartOf(project, 1, 2) };
        Run("""{"track": 1, "part": 2, "startTick": 3000, "endTick": 3100}""", project, view);

        Assert.Equal((60d, 67d), view.Pitches);
    }

    // ── open：默认不切，但**不切的时候要说出来**

    [Fact]
    public void ByDefaultItDoesNotTakeOverThePianoRollButSaysWhatIsThere()
    {
        var project = SampleProject();
        var view = new StubView { EditingPart = PartOf(project, 1, 1) };   // 钢琴窗开着别的 part
        var (result, text) = Run("""{"track": 1, "part": 2}""", project, view);

        Assert.Null(view.Opened);
        Assert.True(result.Data!["pianoShowsAnotherPart"]!.GetValue<bool>());
        Assert.Contains("still showing \"intro\"", text);
        Assert.Contains("open: true", text);   // 一个来回就能自纠
    }

    [Fact]
    public void OpenTrueSwitchesThePianoRollAndSaysThatChangesWhatIsBeingEdited()
    {
        var project = SampleProject();
        var target = PartOf(project, 1, 2);
        var view = new StubView { EditingPart = PartOf(project, 1, 1) };
        var (result, text) = Run("""{"track": 1, "part": 2, "open": true}""", project, view);

        Assert.Same(target, view.Opened);
        Assert.True(result.Data!["openedPart"]!.GetValue<bool>());
        Assert.Contains("what the user is editing now", text);
        // 切过去之后音高轴才是这个 part 的——顺序错了就会把上一个 part 的音域挪进视野。
        Assert.Equal((60d, 67d), view.Pitches);
    }

    // ── 回报：说的是"实际看得见什么"，故整段没装下时读不成"都给你看到了"

    [Fact]
    public void TheReplySaysWhatIsActuallyVisibleNotWhatWasAskedFor()
    {
        var project = SampleProject();
        var view = new StubView { VisibleTicks = (1000, 2000) };   // 目标比视野宽，只装下一部分
        var (result, text) = Run("""{"startTick": 0, "endTick": 3840}""", project, view);

        Assert.Equal(1000d, result.Data!["visibleStartTick"]!.GetValue<double>());
        Assert.Equal(2000d, result.Data!["visibleEndTick"]!.GetValue<double>());
        Assert.Contains("ticks 0–3840", text);              // 要求的
        Assert.Contains("now shows ticks 1000–2000", text); // 实际的
    }
}
