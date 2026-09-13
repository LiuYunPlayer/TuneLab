using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 空发音（note.Pronunciation == ""）时喂给引擎的是什么 —— INote.FinalPronunciation 的口径。
//
// 用户报的现场：一个从别处导入的中文工程打开后音素整片是引擎的默认音素。追下去是这条链——
// 导入落地走 SetInfo（不经 DataLyric.Set 的录入期挂闸），故 NoteInfo.Pronunciation 保持默认空串；
// 空发音若一律当"无覆盖、原文直达引擎"，只认拼音的中文引擎收到汉字就退回自己的默认音素。
// 空发音在这类 note 上只意味着"没人填过"，不代表用户要引擎自己 G2P——后者要靠开关表达。
// 故兜底判据挂 AutoGeneratePronunciation：开=编辑器负责 G2P（录入期填 + 读取期兜底），关=原文直达。
// 这批用例与其它「建 Project」的测试类串行：切换 AutoGeneratePronunciation 是进程级事件，会通知
// **进程内所有活着的 part** 去重建合成会话，而每个 part 的 live context 把创建它的那条线程钉成了
// 自己的数据线程（VoiceSynthesisContext.AssertDataThread 比对 ManagedThreadId）。测试进程里同时
// 活着多个 Project、各绑一条线程，并行时重建就会跨线程发生、让别的类的用例炸在纪律断言上。
// 真实应用没有这个形态：只有一个 Project，且改设置的路径（设置窗 / agent 的 set_setting）都在主线程
// （见 SettingsCommands 的 ctx.OnMainThread）。故这是测试隔离，不是产品缺陷。
[CollectionDefinition("DataThreadProject")]
public class DataThreadProjectCollection { }

[Collection("DataThreadProject")]
public class FinalPronunciationTests
{
    static FinalPronunciationTests()
    {
        TestVoices.EnsureBuiltIn();
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

    // 开关是进程级全局：改完必须还原，否则污染同进程里别的用例。
    static void WithAutoG2P(bool value, Action action)
    {
        var previous = Settings.AutoGeneratePronunciation.Value;
        Settings.AutoGeneratePronunciation.Value = value;
        try { action(); }
        finally { Settings.AutoGeneratePronunciation.Value = previous; }
    }

    // 导入形态：汉字歌词 + 发音空串，经 SetInfo 直接落地（不走 Lyric.Set）。
    static IMidiPart ImportedPart()
    {
        var part = new MidiPartInfo { Name = "p", Pos = 0, StartOffset = 0, EndOffset = 1920 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "大", Pronunciation = "" });
        part.Notes.Add(new NoteInfo { Pos = 480, Dur = 480, Pitch = 62, Lyric = "晴", Pronunciation = "" });
        var track = new NativeTrackInfo { Name = "t1" };
        track.Parts.Add(part);
        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo { Tracks = { track } }));
        return (IMidiPart)document.Project!.Tracks.First().Parts.First();
    }

    [Fact]
    public void ImportedHanziWithEmptyPronunciation_FallsBackToPinyin_WhenAutoG2PIsOn()
    {
        OnDataThread(() => WithAutoG2P(true, () =>
        {
            var notes = ImportedPart().Notes.ToList();
            Assert.All(notes, n => Assert.Equal(string.Empty, n.Pronunciation.Value));   // 前提：确实是空发音
            Assert.Equal("da", notes[0].FinalPronunciation());
            Assert.Equal("qing", notes[1].FinalPronunciation());
        }));
    }

    [Fact]
    public void ImportedHanziWithEmptyPronunciation_SendsLyricItself_WhenAutoG2PIsOff()
    {
        OnDataThread(() => WithAutoG2P(false, () =>
        {
            var notes = ImportedPart().Notes.ToList();
            // 关掉 = 用户明说"让引擎按自己的音系 G2P"，宿主不猜：喂引擎的 lyric 回落到歌词原文。
            Assert.Null(notes[0].FinalPronunciation());
            Assert.Null(notes[1].FinalPronunciation());
        }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitPronunciation_AlwaysWins(bool autoG2P)
    {
        OnDataThread(() => WithAutoG2P(autoG2P, () =>
        {
            var part = ImportedPart();
            var note = part.Notes.First();
            note.Pronunciation.Set("dai");   // 用户显式覆盖：与拼音候选首项不同，才能分辨用的是哪个
            Assert.Equal("dai", note.FinalPronunciation());
        }));
    }

    // 开关进了喂引擎的输入，故改它等于所有 note 的歌词同时变了 —— part 必须整体重建合成会话，
    // 否则引擎那边还挂着按旧口径分好的块，画面与音频都停在改之前。
    [Fact]
    public void TogglingAutoG2P_RebuildsSynthesisPipeline()
    {
        OnDataThread(() =>
        {
            var part = ImportedPart();
            var before = part.SynthesisPipeline;
            Assert.NotNull(before);
            Assert.Same(before, part.SynthesisPipeline);   // 对照：不动开关时它是同一个，下面的 NotSame 才有意义

            var previous = Settings.AutoGeneratePronunciation.Value;
            try
            {
                Settings.AutoGeneratePronunciation.Value = !previous;
                Assert.NotSame(before, part.SynthesisPipeline);
            }
            finally
            {
                Settings.AutoGeneratePronunciation.Value = previous;
            }
        });
    }
}
