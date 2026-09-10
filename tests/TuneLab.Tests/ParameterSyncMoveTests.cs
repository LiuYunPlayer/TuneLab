using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 参数同步模式（Settings.ParameterSyncMode）下移动音符时的参数搬运（ParameterSyncMove）。
// 判据是【来处清空 + 落点得到平移后的曲线】两条同时成立：曾只清落点、来处的音高线原地留着不动，
// 观感就是这个模式"没生效"（用户报的就是这个）——单看落点有曲线是查不出来的，故两条都断言。
public class ParameterSyncMoveTests
{
    static ParameterSyncMoveTests()
    {
        TestVoices.EnsureBuiltIn();
    }

    // MidiPart 一建就起合成管线，而管线要求建在"数据线程"（有 SynchronizationContext，且此后改参数
    // 只许在同一线程），否则活视图的纪律断言会炸。测试里就地当那条线程：同步执行的上下文 + 全程不 await。
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

    // 两个音符各占一拍，音高线只画在第一个音符上（0~479，60 → 61）。
    static IMidiPart SamplePart()
    {
        var part = new MidiPartInfo { Name = "p", Pos = 480, StartOffset = 0, EndOffset = 1920 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "do" });
        part.Notes.Add(new NoteInfo { Pos = 480, Dur = 480, Pitch = 62, Lyric = "re" });
        part.Pitch.Segments.Add(new List<Point> { new(0, 60), new(240, 60.5), new(479, 61) });
        var track = new NativeTrackInfo { Name = "t1" };
        track.Parts.Add(part);
        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo { Tracks = { track } }));
        return (IMidiPart)document.Project!.Tracks.First().Parts.First();
    }

    // 手抄不了的部分只有 Settings 读取：搬运器本身是 UI 调的那一份（UI 只负责夹在 MoveNotes 两侧）。
    static void MoveNotes(IMidiPart part, IReadOnlyCollection<INote> notes, double posOffset, int pitchOffset, double extension = 5)
    {
        var sync = ParameterSyncMove.Capture(part, notes, posOffset, pitchOffset, extension);
        part.MoveNotes(notes, () =>
        {
            foreach (var note in notes)
            {
                note.Pos.Set(note.Pos.Value + posOffset);
                note.Pitch.Set(note.Pitch.Value + pitchOffset);
            }
            sync.ClearSources();
        });
        sync.Apply();
    }

    static List<List<Point>> Curve(IMidiPart part) => part.Pitch.GetInfo();

    // 曲线在这些 tick 上的取值（NaN = 那里没有音高线）。锚点落在哪是搬运的实现细节，值才是判据。
    static double[] ValuesAt(IMidiPart part, params double[] ticks) => part.Pitch.GetValues(ticks);

    [Fact]
    public void HorizontalMove_CarriesCurveAndLeavesNothingBehind()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var moved = part.Notes.First();
            MoveNotes(part, new List<INote> { moved }, 960, 0);

            Assert.Equal(960, moved.Pos.Value);
            // 来处一点不留（NaN = 没有音高线），落点拿到平移后的曲线。
            foreach (var v in ValuesAt(part, 0, 120, 240, 360))
                Assert.True(double.IsNaN(v), "来处还留着音高线: " + v);
            var landed = ValuesAt(part, 960, 1200);
            Assert.Equal(60, landed[0], 3);
            Assert.Equal(60.5, landed[1], 3);
        });
    }

    [Fact]
    public void VerticalMove_ShiftsCurveValuesInPlace()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var moved = part.Notes.First();
            MoveNotes(part, new List<INote> { moved }, 0, 3);

            Assert.Equal(63, moved.Pitch.Value);
            var values = ValuesAt(part, 0, 240, 470);
            Assert.Equal(63, values[0], 3);
            Assert.Equal(63.5, values[1], 3);
            Assert.Equal(64, values[2], 1);   // 曲线末端（原 479 处 61）平移后落在 475 附近
        });
    }

    // 多选整体平移：第一个音符的落点正是第二个音符的来处。全部来处清完才写落点，故搬过去的曲线
    // 不会被另一个音符的清除吃掉；同批的邻居之间也不必让出交界点（位移相同、写出的值一致）。
    [Fact]
    public void MultiSelectMove_DoesNotClearAlreadyMovedCurve()
    {
        OnDataThread(() =>
        {
            var part = SamplePart();
            var notes = part.Notes.ToList();
            MoveNotes(part, notes, 480, 0);

            Assert.Equal(new List<double> { 480, 960 }, part.Notes.Select(n => n.Pos.Value).ToList());
            var values = ValuesAt(part, 480, 720, 959);
            Assert.Equal(60, values[0], 3);
            Assert.Equal(60.5, values[1], 3);
            Assert.Equal(61, values[2], 1);
            foreach (var v in ValuesAt(part, 0, 240))
                Assert.True(double.IsNaN(v), "来处还留着音高线: " + v);
        });
    }

    // 相邻两个音符【先后】各自上移相同音高：交界那一个 tick 上的锚点为两个音符共有，谁搬都会把它搬走，
    // 于是它被平移了两次——原本连续的音高在交界处冒出一根 1 tick 宽的尖刺（用户报的就是这个）。
    [Fact]
    public void TwoAdjacentNotesMovedOneAfterAnother_LeaveNoSpikeAtTheJunction()
    {
        OnDataThread(() =>
        {
            var part = SamplePartWithContinuousCurve();
            var notes = part.Notes.ToList();

            MoveNotes(part, new List<INote> { notes[0] }, 0, 3);
            part.Commit();
            // 只移了左边那个：交界点仍是右邻自己的值，没被搬走。
            Assert.Equal(60, ValuesAt(part, 480)[0], 3);

            MoveNotes(part, new List<INote> { notes[1] }, 0, 3);
            part.Commit();
            // 两个都移过了：整条曲线齐平，交界处不许有尖刺（曾是 66，两侧 63）。
            foreach (var v in ValuesAt(part, 0, 240, 470, 475, 480, 485, 720, 960))
                Assert.Equal(63, v, 3);
        });
    }

    // 一条【连续】的音高线横跨两个相邻音符（交界 tick 480 上有个共享锚点）。
    static IMidiPart SamplePartWithContinuousCurve()
    {
        var part = new MidiPartInfo { Name = "p", Pos = 0, StartOffset = 0, EndOffset = 1920 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "do" });
        part.Notes.Add(new NoteInfo { Pos = 480, Dur = 480, Pitch = 60, Lyric = "re" });
        part.Pitch.Segments.Add(new List<Point> { new(0, 60), new(240, 60), new(480, 60), new(720, 60), new(960, 60) });
        var track = new NativeTrackInfo { Name = "t1" };
        track.Parts.Add(part);
        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo { Tracks = { track } }));
        return (IMidiPart)document.Project!.Tracks.First().Parts.First();
    }

    // 水平移开一个音符时**不该**给落点的曲线焊一个收尾点：落点那一侧的 tick 上摆着的是别人的曲线，
    // 硬接上去就是在音符尾部竖起一根 gap 宽（近乎垂直）的翘尾——实测撞到的正是这个。收尾点只在纯移调
    // （来处 = 落点）时才有意义。
    [Fact]
    public void HorizontalMoveDoesNotWeldTheCurveToWhateverSitsAtTheLandingBoundary()
    {
        OnDataThread(() =>
        {
            var part = SamplePartWithContinuousCurve();
            var notes = part.Notes.ToList();
            // 第一个音符往右半拍、同时抬 6 个半音：落点右界 480+240=720 恰好落在第二个音符的曲线上
            // （那里是 60），接错值一眼看得出来。
            MoveNotes(part, new List<INote> { notes[0] }, 240, 6);

            // 落点整段齐平在 66，末尾 5 tick 内不许朝邻居的 60 跳。
            foreach (var v in ValuesAt(part, 240, 480, 700, 715, 719))
                Assert.Equal(66, v, 1);
            // 来处清空。
            foreach (var v in ValuesAt(part, 0, 120))
                Assert.True(double.IsNaN(v), "来处还留着音高线: " + v);
        });
    }
}
