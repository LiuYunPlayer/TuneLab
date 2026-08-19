using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.Hosting.Compat.Legacy.Voice;
using TuneLab.SDK;
using Xunit;
using LVoice = TuneLab.Extensions.Voices;

namespace TuneLab.Hosting.Compat.Legacy.Tests;

// 老引擎回传字典的**三态**必须原样转达进 V1 会话产物——这是 compat 对宿主显示门控的承诺：
//   · 键不在                  → 引擎没提这个 note（脏 / 合成中）→ echo 里也不该有键，宿主留白且邻居一并留白；
//   · 键在、音素数组为空      → 引擎点了名、答案是"这个 note 没有音素"（老引擎对延音符 note 即此形态）
//                               → echo 里键要在、值是零音素音节，宿主本 note 留白但**邻居照常显示**；
//   · 键在、有音素            → 常态。
// 把中间那态丢掉（早期实现的 `count == 0 → continue`）会让它塌进第一态：一个明确空的延音符 note
// 被宿主当成待合成，把相接的前后邻居一起拖进显示门控留白——句中有延音符即大片音素消失。
public class EmptyPhonemeEchoTests
{
    const double AStart = 0.0, AEnd = 0.5;    // 内容 note
    const double BStart = 0.5, BEnd = 1.0;    // 延音符 note（老引擎回空数组）
    const double CStart = 1.0, CEnd = 1.5;    // 引擎未提及的 note

    [Fact]
    public void EmptyPhonemeArray_KeptAsEmptySyllable()
    {
        var (views, origins) = Chain();
        var echo = LegacySessionAdapter.BuildEcho(Result(views, empty: 1, missing: 2), views);

        Assert.True(echo.TryGetValue(origins[1].Id, out var syllable));   // 键在 = 引擎表了态
        Assert.Empty(syllable!.LeadingPhonemes);                         // 答案是"没有音素"
        Assert.Empty(syllable.BodyPhonemes);
    }

    [Fact]
    public void MissingKey_StaysAbsent()
    {
        var (views, origins) = Chain();
        var echo = LegacySessionAdapter.BuildEcho(Result(views, empty: 1, missing: 2), views);

        Assert.False(echo.TryGetValue(origins[2].Id, out _));   // 引擎没提 → 不伪造条目
    }

    [Fact]
    public void PhonemesPresent_StillMapped()
    {
        var (views, origins) = Chain();
        var echo = LegacySessionAdapter.BuildEcho(Result(views, empty: 1, missing: 2), views);

        Assert.True(echo.TryGetValue(origins[0].Id, out var syllable));
        Assert.Equal(1, syllable!.LeadingPhonemes.Count + syllable.BodyPhonemes.Count);
    }

    // —— 脚手架 ——

    // A [0,0.5] / B [0.5,1.0] / C [1.0,1.5]，首尾相接（延音符典型形态）。
    static (IReadOnlyList<SnapshotNoteView> Views, IReadOnlyList<FakeNote> Origins) Chain()
    {
        var oa = new FakeNote(AStart, AEnd);
        var ob = new FakeNote(BStart, BEnd);
        var oc = new FakeNote(CStart, CEnd);
        oa.Next = ob;
        ob.Next = oc;

        var views = SnapshotNoteView.CreateChain(
            [Snap(AStart, AEnd), Snap(BStart, BEnd), Snap(CStart, CEnd)],
            [oa, ob, oc]);   // 基线 pass（全喂空）
        return (views, [oa, ob, oc]);
    }

    // 老引擎结果：除 empty（键在、空数组）与 missing（不写键）外，其余 note 各回一个音素。
    static LVoice.SynthesisResult Result(IReadOnlyList<SnapshotNoteView> views, int empty, int missing)
    {
        var map = new Dictionary<LVoice.ISynthesisNote, LVoice.SynthesizedPhoneme[]>();
        for (int i = 0; i < views.Count; i++)
        {
            if (i == missing)
                continue;

            map[views[i]] = i == empty
                ? []
                : [new LVoice.SynthesizedPhoneme { Symbol = "a", StartTime = views[i].StartTime, EndTime = views[i].EndTime }];
        }
        return new LVoice.SynthesisResult(0, 44100, [], null, map);
    }

    static VoiceSynthesisNoteSnapshot Snap(double start, double end) => new()
    {
        StartTime = start,
        EndTime = end,
        Pitch = 60,
        Lyric = "a",
        LeadingPhonemes = [],
        BodyPhonemes = [],
        BodyOffset = 0,
        Properties = PropertyObject.Empty,
    };

    sealed class FakeNote(double start, double end) : IVoiceSynthesisNote
    {
        public string Id { get; } = System.Threading.Interlocked.Increment(ref sNextId).ToString();
        static int sNextId;
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
