using TuneLab.Foundation;
using TuneLab.Hosting.Compat.Legacy.Voice;
using TuneLab.SDK;
using Xunit;
using LProp = TuneLab.Base.Properties;
using LStruct = TuneLab.Base.Structures;

namespace TuneLab.Hosting.Compat.Legacy.Tests;

// compat 后盖前单声部兜底：V1 面直传可重叠 note（和弦），但老引擎硬假定单声部、首尾相接。
// 这两个喂老引擎的视图（LiveNoteView=分片输入、SnapshotNoteView=合成数据）的 EndTime 一律
// 钳到下一 note 起点（EndTime = Min(自身End, Next.StartTime)）。本组钉死该钳位与同起点退化，
// 不碰宿主基线（AutomationSnapshotTests，独立工程）。
public class NoteOverlapClampTests
{
    // 普通重叠：后起的 note 截断前一个的尾巴；末尾 note 无 Next 整段保留。
    [Fact]
    public void LiveView_TailOverlap_ClampsEndToNextStart()
    {
        var a = new FakeNote(0, 2);
        var b = new FakeNote(1, 3);
        a.Next = b;

        Assert.Equal(1, Live(a).EndTime); // Min(2, 1)
        Assert.Equal(3, Live(b).EndTime); // Min(3, +inf)
    }

    // 不重叠时钳位是恒等：与 chord 支持引入前老 Note.EndTime 逐字一致。
    [Fact]
    public void LiveView_NoOverlap_KeepsOriginalEnd()
    {
        var a = new FakeNote(0, 1);
        var b = new FakeNote(2, 3);
        a.Next = b;

        Assert.Equal(1, Live(a).EndTime);
        Assert.Equal(3, Live(b).EndTime);
    }

    // 同起点真和弦退化：数据层序为 EndPos 降（长者在前），长者的 Next 即同起点的短者，
    // 钳后归零；最终只剩排在最后、EndTime 最靠前（最短）的那个存活。
    [Fact]
    public void LiveView_SameStartChord_OnlyShortestSurvives()
    {
        var long3 = new FakeNote(0, 3);
        var mid2 = new FakeNote(0, 2);
        var short1 = new FakeNote(0, 1);
        var trailing = new FakeNote(5, 6);
        long3.Next = mid2;
        mid2.Next = short1;
        short1.Next = trailing;

        Assert.Equal(0, Live(long3).EndTime);    // Min(3, 0) 归零
        Assert.Equal(0, Live(mid2).EndTime);     // Min(2, 0) 归零
        Assert.Equal(1, Live(short1).EndTime);   // Min(1, 5) 存活
        Assert.Equal(6, Live(trailing).EndTime); // Min(6, +inf)
    }

    // 快照视图同口径：用全局 origin.Next（可越过 piece 边界）冻结成边界。
    [Fact]
    public void SnapshotView_ClampsSameAsLiveView()
    {
        var oa = new FakeNote(0, 2);
        var ob = new FakeNote(1, 3);
        oa.Next = ob;

        var views = SnapshotNoteView.CreateChain(
            [Snap(0, 2), Snap(1, 3)],
            [oa, ob]);

        Assert.Equal(1, views[0].EndTime); // Min(2, 1)
        Assert.Equal(3, views[1].EndTime); // Min(3, +inf)
    }

    // 跨 piece 重叠：piece 内末 note 的 Next 是下一 piece 的 note（不在快照列表里），
    // 仍按全局 Next 钳位——与 LiveView 一致，避免边界处漏钳。
    [Fact]
    public void SnapshotView_ClampsAgainstNextOutsidePiece()
    {
        var oa = new FakeNote(0, 2);
        var ob = new FakeNote(2, 5);
        var outside = new FakeNote(3, 6); // 下一 piece、与 ob 重叠
        oa.Next = ob;
        ob.Next = outside;

        var views = SnapshotNoteView.CreateChain(
            [Snap(0, 2), Snap(2, 5)], // 仅 oa/ob 在本 piece 快照里
            [oa, ob]);

        Assert.Equal(2, views[0].EndTime); // Min(2, 2)
        Assert.Equal(3, views[1].EndTime); // Min(5, outside.start=3)
    }

    // —— 脚手架 ——

    static LiveNoteView Live(IVoiceSynthesisNote origin)
    {
        // propertiesReader 仅在访问 .Properties 时调用；钳位路径不触及，给个合法空对象即可。
        var cache = new LiveNoteViewCache(_ => new LProp.PropertyObject(new LStruct.Map<string, LProp.PropertyValue>()));
        return cache.Wrap(origin);
    }

    static VoiceSynthesisNoteSnapshot Snap(double start, double end) => new()
    {
        StartTime = start,
        EndTime = end,
        Pitch = 60,
        Lyric = "a",
        Phonemes = [],
        Properties = PropertyObject.Empty,
    };

    sealed class FakeNote(double start, double end) : IVoiceSynthesisNote
    {
        public IReadOnlyNotifiableProperty<double> StartTime { get; } = new Const<double>(start);
        public IReadOnlyNotifiableProperty<double> EndTime { get; } = new Const<double>(end);
        public IReadOnlyNotifiableProperty<int> Pitch { get; } = new Const<int>(60);
        public IReadOnlyNotifiableProperty<string> Lyric { get; } = new Const<string>("a");
        public IReadOnlyNotifiableProperty<IReadOnlyList<SynthesizedPhoneme>> Phonemes { get; } = new Const<IReadOnlyList<SynthesizedPhoneme>>([]);
        public bool IsContinuation => false;
        public IReadOnlyNotifiablePropertyObject Properties => null!; // 钳位路径不触及
        public IVoiceSynthesisNote? Next { get; set; }
        public IVoiceSynthesisNote? Last { get; set; }
    }

    sealed class Const<T>(T value) : IReadOnlyNotifiableProperty<T>
    {
        public T Value => value;
        public event Action? WillModify { add { } remove { } }
        public event Action? Modified { add { } remove { } }
    }
}
