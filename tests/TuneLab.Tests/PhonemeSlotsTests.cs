using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// SDK 冻结面回归保护：音素「核相对 slot」口径纯函数 PhonemeSlots（PhonemeAt / UnionSlots）+ 其依赖的声明面
// 视图成员（IVoiceSynthesisNoteView.LeadingPhonemes/BodyPhonemes、IVoiceSynthesisPhonemeView.Properties）。
// 这些是引擎 GetPhonemePropertyConfigs 的键控口径与多选合并范式的基座，此前无任何单测覆盖——冻结后改实现
// 无护栏。用轻量 fake 视图直接行使 SDK 契约（不牵扯宿主数据层）。
public class PhonemeSlotsTests
{
    sealed class FakePhonemeView(string symbol, PropertyObject? properties = null) : IVoiceSynthesisPhonemeView
    {
        public string Symbol { get; } = symbol;
        public double Duration => 0;
        public double StretchWeight => 0;
        public PropertyObject Properties { get; } = properties ?? PropertyObject.Empty;
    }

    sealed class FakeNoteView : IVoiceSynthesisNoteView
    {
        public double StartTime { get; init; }
        public double EndTime { get; init; }
        public int Pitch { get; init; }
        public string Lyric { get; init; } = "";
        public PropertyObject Properties { get; init; } = PropertyObject.Empty;
        public double BodyOffset { get; init; }
        public IReadOnlyList<IVoiceSynthesisPhonemeView> LeadingPhonemes { get; init; } = [];
        public IReadOnlyList<IVoiceSynthesisPhonemeView> BodyPhonemes { get; init; } = [];
    }

    static IVoiceSynthesisPhonemeView Ph(string symbol, PropertyObject? props = null) => new FakePhonemeView(symbol, props);

    static FakeNoteView Note(string[] leading, string[] body) => new()
    {
        LeadingPhonemes = leading.Select(s => Ph(s)).ToList(),
        BodyPhonemes = body.Select(s => Ph(s)).ToList(),
    };

    static PropertyObject Obj(params (string key, PropertyValue value)[] entries)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var (key, value) in entries)
            map.Add(key, value);
        return new PropertyObject(map);
    }

    // ===== PhonemeAt：slot = 全序列下标 − LeadingPhonemes.Count；核（slot 0）= 主体首音素 =====

    [Fact]
    public void PhonemeAt_NucleusAndNeighbors()
    {
        // 引导 [c1,c2]，主体 [v,c3]：全序列 0=c1 1=c2 2=v 3=c3，leadCount=2。
        var note = Note(["c1", "c2"], ["v", "c3"]);

        Assert.Equal("v", note.PhonemeAt(0)?.Symbol);    // slot 0 = 核 = 主体首
        Assert.Equal("c2", note.PhonemeAt(-1)?.Symbol);  // slot -1 = 核前最近的引导（引导末）
        Assert.Equal("c1", note.PhonemeAt(-2)?.Symbol);  // slot -2 = 引导首
        Assert.Equal("c3", note.PhonemeAt(1)?.Symbol);   // slot 1 = 核后
    }

    [Fact]
    public void PhonemeAt_OutOfRange_ReturnsNull()
    {
        var note = Note(["c1", "c2"], ["v", "c3"]);
        Assert.Null(note.PhonemeAt(-3));  // 越过引导首
        Assert.Null(note.PhonemeAt(2));   // 越过主体末
    }

    [Fact]
    public void PhonemeAt_EmptyNote_ReturnsNull()
    {
        var note = Note([], []);
        Assert.Null(note.PhonemeAt(0));
        Assert.Null(note.PhonemeAt(-1));
    }

    [Fact]
    public void PhonemeAt_LeadingOnly_And_BodyOnly()
    {
        var leadingOnly = Note(["c1", "c2"], []);
        Assert.Equal("c2", leadingOnly.PhonemeAt(-1)?.Symbol);
        Assert.Null(leadingOnly.PhonemeAt(0));   // 无主体 → 核位空

        var bodyOnly = Note([], ["v", "c"]);
        Assert.Equal("v", bodyOnly.PhonemeAt(0)?.Symbol);
        Assert.Equal("c", bodyOnly.PhonemeAt(1)?.Symbol);
        Assert.Null(bodyOnly.PhonemeAt(-1));     // 无引导 → 核前空
    }

    // ===== UnionSlots：选区 slot 全集 = 升序连续区间 [−maxLead, maxPostNucleus] =====

    [Fact]
    public void UnionSlots_SingleNote_IsContiguousRange()
    {
        var notes = new List<IVoiceSynthesisNoteView> { Note(["c1", "c2"], ["v", "c3"]) };  // 域 [-2, 1]
        Assert.Equal(new List<int> { -2, -1, 0, 1 }, notes.UnionSlots().ToList());
    }

    [Fact]
    public void UnionSlots_MultiNote_IsUnionOfRanges()
    {
        // A 域 [-2,1]（lead 2、body 2）、B 域 [-1,0]（lead 1、body 1）→ 并集 [-2,1] 连续。
        var notes = new List<IVoiceSynthesisNoteView>
        {
            Note(["c1", "c2"], ["v", "c3"]),
            Note(["c"], ["v"]),
        };
        Assert.Equal(new List<int> { -2, -1, 0, 1 }, notes.UnionSlots().ToList());
    }

    [Fact]
    public void UnionSlots_SkipsEmptyNotes()
    {
        var notes = new List<IVoiceSynthesisNoteView>
        {
            Note([], []),               // 空 note 不参与
            Note(["c"], ["v", "c2"]),   // 域 [-1, 1]
        };
        Assert.Equal(new List<int> { -1, 0, 1 }, notes.UnionSlots().ToList());
    }

    [Fact]
    public void UnionSlots_AllEmpty_IsEmpty()
    {
        var notes = new List<IVoiceSynthesisNoteView> { Note([], []), Note([], []) };
        Assert.Empty(notes.UnionSlots());
    }

    // ===== 文档化的多选声明合并范式：per slot 取各 note 音素属性、三态合并（宿主只并值） =====

    [Fact]
    public void MultiSelectMerge_PerSlot_PhonemeProperties()
    {
        // 两 note 的核（slot 0）音素各带 stress：同值 → 保留，不等 → Multiple。
        var noteA = new FakeNoteView { BodyPhonemes = [Ph("v", Obj(("stress", 0.5)))] };
        var noteB = new FakeNoteView { BodyPhonemes = [Ph("v", Obj(("stress", 0.5)))] };
        var noteC = new FakeNoteView { BodyPhonemes = [Ph("v", Obj(("stress", 0.9)))] };

        var same = new List<IVoiceSynthesisNoteView> { noteA, noteB };
        var mergedSame = same.Select(n => n.PhonemeAt(0)?.Properties).OfType<PropertyObject>().Merge();
        Assert.True(mergedSame.Map.TryGetValue("stress", out var s) && s.ToDouble(out var sv) && sv == 0.5);

        var diff = new List<IVoiceSynthesisNoteView> { noteA, noteC };
        var mergedDiff = diff.Select(n => n.PhonemeAt(0)?.Properties).OfType<PropertyObject>().Merge();
        Assert.True(mergedDiff.Map.TryGetValue("stress", out var d) && d.IsMultiple());
    }
}
