using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.Data.Synthesis;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 声明失效（IVoiceSynthesisContext.InvalidatePart / InvalidateNotes）的宿主侧封条：
// 身份跨会话稳定且与声明视图同号、合拍（任意线程连调只 flush 一次）、级联范围、会话销毁后静默、不碰延音缓存。
// 面板按选区交集刷新属 UI，不在此测。
[Collection("DataThreadProject")]   // 见 FinalPronunciationTests 的 CollectionDefinition
public class DeclarationInvalidationTests
{
    static DeclarationInvalidationTests()
    {
        TestVoices.EnsureBuiltIn();
    }

    // 延迟型数据线程：Post 只入队、Pump 时在测试线程（= 数据线程）执行——合拍要能被观察到，
    // 就不能像 FinalPronunciationTests 那样 Post 即同步执行。
    sealed class QueuedDataThread : SynchronizationContext
    {
        readonly ConcurrentQueue<(SendOrPostCallback, object?)> mQueue = new();
        public int Pending => mQueue.Count;
        public override void Post(SendOrPostCallback d, object? state) => mQueue.Enqueue((d, state));
        public override void Send(SendOrPostCallback d, object? state) => d(state);
        public void Pump()
        {
            while (mQueue.TryDequeue(out var item))
                item.Item1(item.Item2);
        }
    }

    static void OnDataThread(Action<QueuedDataThread> action)
    {
        var previous = SynchronizationContext.Current;
        var context = new QueuedDataThread();
        SynchronizationContext.SetSynchronizationContext(context);
        try { action(context); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static MidiPart SamplePart()
    {
        var part = new MidiPartInfo { Name = "p", Pos = 0, StartOffset = 0, EndOffset = 1920 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "la" });
        part.Notes.Add(new NoteInfo { Pos = 480, Dur = 480, Pitch = 62, Lyric = "la" });
        var track = new NativeTrackInfo { Name = "t1" };
        track.Parts.Add(part);
        var document = new ProjectDocument();
        document.SetProject(new Project(new ProjectInfo { Tracks = { track } }));
        return (MidiPart)document.Project!.Tracks.First().Parts.First();
    }

    static VoiceSynthesisContext ContextOf(MidiPart part)
        => Assert.IsType<VoiceSynthesisPipeline>(part.SynthesisPipeline).Context;

    // 让 part 整体重建会话（与 FinalPronunciationTests 同一个触发：G2P 开关进了喂引擎的输入）。
    static void RebuildSession(QueuedDataThread thread)
    {
        var previous = Settings.AutoGeneratePronunciation.Value;
        Settings.AutoGeneratePronunciation.Value = !previous;
        Settings.AutoGeneratePronunciation.Value = previous;
        thread.Pump();
    }

    static List<DeclarationInvalidation> Record(MidiPart part)
    {
        var events = new List<DeclarationInvalidation>();
        part.DeclarationsInvalidated.Subscribe(events.Add);
        return events;
    }

    [Fact]
    public void Ids_AreStableAcrossSessionRebuild_AndMatchDeclarationViews()
    {
        OnDataThread(thread =>
        {
            var part = SamplePart();
            thread.Pump();
            var before = ContextOf(part);
            var sessionIds = before.Notes.Select(n => n.Id).ToList();

            // 声明视图与会话活视图同号：插件在声明求值时才查得回会话里算的派生状态。
            IVoiceSynthesisPartView view = new PartContext(part);
            Assert.Equal(sessionIds, view.Notes.Select(n => n.Id).ToList());
            Assert.Equal(before.PartId, view.PartId);
            Assert.Equal(sessionIds.Count, sessionIds.Distinct().Count());
            Assert.NotEqual(before.PartId, sessionIds[0]);   // part 与 note 共用计数器、号码不撞

            RebuildSession(thread);
            var after = ContextOf(part);
            Assert.NotSame(before, after);                   // 对照：确实换了会话，下面的 Equal 才有意义
            Assert.Equal(sessionIds, after.Notes.Select(n => n.Id).ToList());
            Assert.Equal(before.PartId, after.PartId);
        });
    }

    [Fact]
    public void ManyCallsFromAnyThread_FlushOnce_PartWins()
    {
        OnDataThread(thread =>
        {
            var part = SamplePart();
            thread.Pump();
            var context = ContextOf(part);
            var events = Record(part);
            var noteId = part.Notes.First().RuntimeId;

            // 后台线程高频调用：只入队一次 flush，数据线程 pump 前不发事件。
            Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                    context.InvalidateNotes([noteId]);
                context.InvalidatePart();
            }).Wait();
            Assert.Empty(events);
            Assert.Equal(1, thread.Pending);

            thread.Pump();
            var e = Assert.Single(events);
            Assert.True(e.Part);                             // part 级涵盖全部 note，不再携带 id 集合
            Assert.Empty(e.NoteIds);
            Assert.All(part.Notes, n => Assert.True(e.Affects(n)));
        });
    }

    [Fact]
    public void NoteInvalidation_CarriesOnlyThoseNotes_AndIgnoresUnknownIds()
    {
        OnDataThread(thread =>
        {
            var part = SamplePart();
            thread.Pump();
            var context = ContextOf(part);
            var events = Record(part);
            var notes = part.Notes.ToList();

            context.InvalidateNotes([notes[0].RuntimeId, "no-such-note"]);
            context.InvalidateNotes([notes[0].RuntimeId]);
            thread.Pump();

            var e = Assert.Single(events);
            Assert.False(e.Part);
            Assert.True(e.Affects(notes[0]));
            Assert.False(e.Affects(notes[1]));
        });
    }

    [Fact]
    public void CallsAfterSessionDisposed_AreIgnored()
    {
        OnDataThread(thread =>
        {
            var part = SamplePart();
            thread.Pump();
            var stale = ContextOf(part);
            var events = Record(part);

            stale.InvalidatePart();                          // 已入队、尚未 flush 时会话就被换掉
            RebuildSession(thread);
            stale.InvalidateNotes([part.Notes.First().RuntimeId]);
            thread.Pump();

            Assert.Empty(events);
        });
    }

    // 契约：本信号不使延音缓存失效（延音判定必须由当前数据同步决定）。缓存的脏位是私有实现，这里用反射看——
    // 它是这条契约在宿主侧唯一的落点，值得一根直接的探针。
    [Fact]
    public void Invalidation_DoesNotDirtyContinuationCache()
    {
        OnDataThread(thread =>
        {
            var part = SamplePart();
            thread.Pump();
            var dirty = typeof(MidiPart).GetField("mContinuationDirty", BindingFlags.Instance | BindingFlags.NonPublic)!;
            part.IsPluginContinuation(part.Notes.First());   // 读一次让缓存落定
            Assert.False((bool)dirty.GetValue(part)!);

            var context = ContextOf(part);
            context.InvalidatePart();
            context.InvalidateNotes(part.Notes.Select(n => n.RuntimeId));
            thread.Pump();

            Assert.False((bool)dirty.GetValue(part)!);
        });
    }

    // ── 宿主侧：声明 context 带着 part 的全部数据（音高 / 歌词、note 集合、自动化曲线……），这些数据【提交】后宿主
    //    重新询问 part 级声明，插件不必自己订阅再 Invalidate。只跟提交、不跟每次改动（拖动的每一步都是 settled 改动）。

    static (ProjectDocument Document, MidiPart Part) SampleDocument()
    {
        var part = SamplePart();
        var document = (ProjectDocument)((Project)part.Track.Project).Document!;
        document.Commit();   // 装载期的改动先落定，测试从干净状态开始
        return (document, part);
    }

    [Fact]
    public void CommittingAPartDataChange_AsksForDeclarationsOnce()
    {
        OnDataThread(thread =>
        {
            var (document, part) = SampleDocument();
            thread.Pump();
            var events = Record(part);

            part.Notes.First().Pitch.Set(64);
            thread.Pump();
            Assert.Empty(events);                  // 改了但没提交：不问

            document.Commit();
            thread.Pump();
            Assert.Single(events);
            Assert.True(events[0].Part);           // part 级，note 声明随之级联
        });
    }

    [Fact]
    public void ManyChangesBeforeOneCommit_AskOnce()
    {
        OnDataThread(thread =>
        {
            var (document, part) = SampleDocument();
            thread.Pump();
            var events = Record(part);

            var note = part.Notes.First();
            for (int i = 0; i < 20; i++)           // 像拖动：每一步都是 settled 改动
                note.Pitch.Set(60 + i % 5);
            document.Commit();
            thread.Pump();
            Assert.Single(events);
        });
    }

    [Fact]
    public void CommitWithoutAPartChange_AsksNothing_UndoDoes()
    {
        OnDataThread(thread =>
        {
            var (document, part) = SampleDocument();
            thread.Pump();
            part.Notes.First().Lyric.Set("li");
            document.Commit();
            thread.Pump();
            var events = Record(part);

            document.Commit();                     // 这次提交与本 part 无关
            thread.Pump();
            Assert.Empty(events);

            document.Undo();                       // 撤销改回了本 part 的数据
            thread.Pump();
            Assert.Single(events);
        });
    }
}
