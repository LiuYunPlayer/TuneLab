using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.Hosting.Compat.Legacy.Voice;
using TuneLab.SDK;
using Xunit;
using LProp = TuneLab.Base.Properties;
using LStruct = TuneLab.Base.Structures;

namespace TuneLab.Hosting.Compat.Legacy.Tests;

// 钉死 note 的前置辅音「自然长超过前一个未钉死 note」时喂 legacy 引擎的几何。引入**基线回显缓存**后：
//   · 未钉死邻居**有缓存**（真实 pass 常态）→ 前置辅音被借入邻居空间并二级压缩、钳在邻居头（与宿主显示同函数同输入 →
//     音频==显示）；喂引擎侧也喂了邻居缓存 → 全显式时间线、无接缝 Sil。
//   · 未钉死邻居**无缓存**（基线尚未预测出该 note 的退化回退）→ 前置辅音按自然长往左堆、不压缩，伸到邻居头之前
//     （下例 start=−0.2s，越过 A 头）。这只是回退态，非常态。
// 本组把这两态量成断言：有缓存钳在 0.0、无缓存越到 −0.2、两者分歧 0.2s；并验证「两个都钉死」与「未钉死+缓存」
// 得到同一压缩结果（即缓存让未钉死邻居等价于一个有几何的内容邻居）。
public class LeadingIntoUnpinnedNeighborTests
{
    // 场景常量：A = 未钉死短 note [0, 0.3]；B = 钉死 note [0.3, 1.3]，前置辅音自然长 0.5（比整个 A 还长）+ 元音。
    const double AStart = 0.0, AEnd = 0.3;
    const double BStart = 0.3, BEnd = 1.3;
    const double LeadNatural = 0.5;   // 前置辅音自然时长 > (BStart − AStart) = 0.3 ⇒ 越过 A 头

    // —— 喂引擎·未钉死邻居【无缓存】退化态 ⇒ 前置辅音不压缩、伸到 A 头之前 ——
    [Fact]
    public void EngineFeed_LongLeading_UnpinnedNeighbor_NoCache_ExtendsUncompressedBeforePrevHead()
    {
        var oa = new FakeNote(AStart, AEnd);
        var ob = new FakeNote(BStart, BEnd);
        oa.Next = ob;

        var views = SnapshotNoteView.CreateChain(
            [Unpinned(AStart, AEnd), Pinned(BStart, BEnd)],
            [oa, ob],
            NoCache());   // 真实 pass、但 A 无基线缓存 → A 喂空，B 前置辅音无压缩上下文

        var lead = views[1].Phonemes[0];   // B 全序列首个 = 前置辅音
        // 刚性辅音末恒钉在 B 头（junction），起 = 头 − 自然长；未压缩。
        Assert.Equal(BStart - LeadNatural, lead.StartTime, 6);   // = −0.2
        Assert.Equal(BStart, lead.EndTime, 6);                   // = 0.3
        // 起点落在前一个 note 头（0.0）之前 ⇒ 无缓存退化态才会越界。
        Assert.True(lead.StartTime < AStart);
    }

    // —— 喂引擎·未钉死邻居【有缓存】常态 ⇒ 前置辅音借入邻居空间并二级压缩、钳在 A 头（== 显示） ——
    [Fact]
    public void EngineFeed_LongLeading_UnpinnedNeighbor_WithCache_ClampsLikeDisplay()
    {
        var oa = new FakeNote(AStart, AEnd);
        var ob = new FakeNote(BStart, BEnd);
        oa.Next = ob;

        var views = SnapshotNoteView.CreateChain(
            [Unpinned(AStart, AEnd), Pinned(BStart, BEnd)],
            [oa, ob],
            Echo(EchoVowel(0.2), null));   // A 的基线自然预测（一个元音）作压缩上下文；B 无缓存（用其钉死值）

        var lead = views[1].Phonemes[0];
        // 与显示同函数同输入：借入 A 空间 [0,0.3]、元音让尽、辅音等比压到 0.3、钳在 A 头，不越界。
        Assert.Equal(AStart, lead.StartTime, 6);   // = 0.0
        Assert.Equal(AEnd, lead.EndTime, 6);       // = 0.3
        // 且 A 的元音也被喂引擎（全显式时间线）：views[0] 有输出、被压到 [0,0]。
        Assert.Single(views[0].Phonemes);
    }

    // —— 显示路径：前 note 作有内容邻居 ⇒ 前置辅音被借入并二级压缩、钳在 A 头 ——
    [Fact]
    public void DisplayPath_LongLeading_ContentNeighbor_ClampsLeadingToPrevHead()
    {
        // 直接调 SDK 布局，前 note 给一个元音（模拟其已回显/已钉死的内容），与 B 相接。
        var timings = PhonemeLayout.Resolve(new[]
        {
            new PhonemeLayoutNote { FillStart = AStart, FillEnd = AEnd, LeadingPhonemes = [], BodyPhonemes = [Vowel(0.2)], BodyOffset = 0 },
            LayoutB(),
        });

        var lead = timings[1][0];   // B 前置辅音
        // 借入 A 空间 [0, 0.3]，自然长 0.5 > 0.3 ⇒ 元音让尽、辅音等比压到 0.3，起点钳在 A 头。
        Assert.Equal(AStart, lead.Start, 6);   // = 0.0，不越 A 头
        Assert.Equal(AEnd, lead.End, 6);       // = 0.3
    }

    // —— 分歧量（仅无缓存退化态存在）：喂引擎比显示更靠左，差 = (BStart − LeadNatural) 到 AStart 的距离 ——
    [Fact]
    public void Divergence_NoCacheEngineFeedIsEarlierThanDisplay_ByOverflowAmount()
    {
        var oa = new FakeNote(AStart, AEnd);
        var ob = new FakeNote(BStart, BEnd);
        oa.Next = ob;
        var engineLeadStart = SnapshotNoteView.CreateChain(
            [Unpinned(AStart, AEnd), Pinned(BStart, BEnd)], [oa, ob], NoCache())[1].Phonemes[0].StartTime;

        var displayLeadStart = PhonemeLayout.Resolve(new[]
        {
            new PhonemeLayoutNote { FillStart = AStart, FillEnd = AEnd, LeadingPhonemes = [], BodyPhonemes = [Vowel(0.2)], BodyOffset = 0 },
            LayoutB(),
        })[1][0].Start;

        Assert.Equal(AStart - (BStart - LeadNatural), displayLeadStart - engineLeadStart, 6);   // 0.0 − (−0.2) = 0.2
        Assert.True(engineLeadStart < displayLeadStart);
    }

    // —— 对照：前 note 也钉死 ⇒ CreateChain 走钉死间联合压缩，喂引擎几何回落到与显示一致（分歧是「钉死 vs 未钉死」专属）——
    [Fact]
    public void EngineFeed_LongLeading_PinnedNeighbor_CompressesLikeDisplay()
    {
        var oa = new FakeNote(AStart, AEnd);
        var ob = new FakeNote(BStart, BEnd);
        oa.Next = ob;

        var aPinned = Pinned(AStart, AEnd, leading: [], body: [P(0.2, 1)]);   // A 也钉死（一个元音）
        var views = SnapshotNoteView.CreateChain([aPinned, Pinned(BStart, BEnd)], [oa, ob], NoCache());

        var lead = views[1].Phonemes[0];
        Assert.Equal(AStart, lead.StartTime, 6);   // 与显示一致：钳在 A 头，不再越界
        Assert.Equal(AEnd, lead.EndTime, 6);
    }

    // —— 脚手架 ——

    static SynthesizedPhoneme Ph(double dur, double weight) => new() { Symbol = "x", Duration = dur, StretchWeight = weight };
    static SynthesizedPhoneme Vowel(double dur) => new() { Symbol = "a", Duration = dur, StretchWeight = 1 };

    // 真实 pass 的基线回显数组（2 note）：全 null = 无缓存退化态；带 syllable = 该 note 有基线自然预测。
    static SynthesizedSyllable?[] NoCache() => new SynthesizedSyllable?[] { null, null };
    static SynthesizedSyllable?[] Echo(SynthesizedSyllable? a, SynthesizedSyllable? b) => new[] { a, b };
    static SynthesizedSyllable EchoVowel(double dur) => new([], [Vowel(dur)], 0);

    static PhonemeLayoutNote LayoutB() => new()
    {
        FillStart = BStart, FillEnd = BEnd,
        LeadingPhonemes = [Ph(LeadNatural, 0)],   // 刚性前置辅音
        BodyPhonemes = [Vowel(0.5)],
        BodyOffset = 0,
    };

    static VoiceSynthesisNoteSnapshot Unpinned(double start, double end) => Snap(start, end, [], []);

    static VoiceSynthesisNoteSnapshot Pinned(double start, double end,
        IReadOnlyList<VoiceSynthesisPhonemeSnapshot>? leading = null, IReadOnlyList<VoiceSynthesisPhonemeSnapshot>? body = null)
        => Snap(start, end, leading ?? [P(LeadNatural, 0)], body ?? [P(0.5, 1)]);

    static VoiceSynthesisPhonemeSnapshot P(double dur, double weight) => new("x", dur, weight, PropertyObject.Empty);

    static VoiceSynthesisNoteSnapshot Snap(double start, double end,
        IReadOnlyList<VoiceSynthesisPhonemeSnapshot> leading, IReadOnlyList<VoiceSynthesisPhonemeSnapshot> body) => new()
    {
        StartTime = start,
        EndTime = end,
        Pitch = 60,
        Lyric = "a",
        LeadingPhonemes = leading,
        BodyPhonemes = body,
        BodyOffset = 0,
        Properties = PropertyObject.Empty,
    };

    sealed class FakeNote(double start, double end) : IVoiceSynthesisNote
    {
        public IReadOnlyNotifiableProperty<double> StartTime { get; } = new Const<double>(start);
        public IReadOnlyNotifiableProperty<double> EndTime { get; } = new Const<double>(end);
        public IReadOnlyNotifiableProperty<int> Pitch { get; } = new Const<int>(60);
        public IReadOnlyNotifiableProperty<string> Lyric { get; } = new Const<string>("a");
        public IReadOnlyNotifiableProperty<IReadOnlyList<SynthesizedPhoneme>> LeadingPhonemes { get; } = new Const<IReadOnlyList<SynthesizedPhoneme>>([]);
        public IReadOnlyNotifiableProperty<IReadOnlyList<SynthesizedPhoneme>> BodyPhonemes { get; } = new Const<IReadOnlyList<SynthesizedPhoneme>>([]);
        public IReadOnlyNotifiableProperty<double> BodyOffset { get; } = new Const<double>(0);
        public IReadOnlyNotifiablePropertyObject Properties => null!;
        public IVoiceSynthesisNote? Next { get; set; }
        public IVoiceSynthesisNote? Previous { get; set; }
    }

    sealed class Const<T>(T value) : IReadOnlyNotifiableProperty<T>
    {
        public T Value => value;
        public IActionEvent WillModify => ActionEvent.Empty;
        public IActionEvent Modified => ActionEvent.Empty;
    }
}
