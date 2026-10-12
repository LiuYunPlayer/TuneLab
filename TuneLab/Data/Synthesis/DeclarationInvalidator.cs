using System;
using System.Collections.Generic;
using System.Threading;

namespace TuneLab.Data.Synthesis;

// 声明失效的收口（voice / instrument context 共用）：插件可在任意线程、高频地调 InvalidatePart / InvalidateNotes
//（后台解析逐 note 回来时一次几十次），本类在锁内攒脏位（part 标志 + note id 集合），只 Post 一次 flush 回数据线程，
// flush 时交给 MidiPart 统一重算——lane 重算、轨集合签名比对、面板刷新各只做一次。
// 随 context 生灭：会话销毁后攒下的与再来的调用一律丢弃（新会话会自行重算并重新宣告）。
internal sealed class DeclarationInvalidator(MidiPart part) : IDisposable
{
    public void InvalidatePart()
    {
        lock (mLock)
        {
            if (mDisposed)
                return;
            mPartPending = true;
        }
        Schedule();
    }

    public void InvalidateNotes(IEnumerable<string> noteIds)
    {
        ArgumentNullException.ThrowIfNull(noteIds);
        lock (mLock)
        {
            if (mDisposed)
                return;
            foreach (var id in noteIds)
            {
                if (id != null)
                    mPendingNotes.Add(id);
            }
        }
        Schedule();
    }

    // 仅数据线程（随 context.Dispose）。
    public void Dispose()
    {
        lock (mLock)
        {
            mDisposed = true;
            mPartPending = false;
            mPendingNotes.Clear();
        }
    }

    void Schedule()
    {
        lock (mLock)
        {
            if (mScheduled || mDisposed)
                return;
            mScheduled = true;
        }

        // 无调度上下文（测试等环境）：退化为就地 flush（同 MidiPart.OnTimebaseModified 的退化口径）。
        if (mSyncContext == null)
        {
            Flush();
            return;
        }
        mSyncContext.Post(_ => Flush(), null);
    }

    void Flush()
    {
        bool partPending;
        HashSet<string> notes;
        lock (mLock)
        {
            mScheduled = false;
            if (mDisposed)
                return;
            partPending = mPartPending;
            notes = mPendingNotes;
            mPartPending = false;
            mPendingNotes = new();
        }

        if (!partPending && notes.Count == 0)
            return;
        // part 级已涵盖全部 note，不再携带 id 集合。
        part.InvalidateDeclarations(new DeclarationInvalidation(partPending, partPending ? sEmpty : notes));
    }

    static readonly HashSet<string> sEmpty = new();

    // context 在数据线程上构建（管线构造断言过），故此处捕获的即数据线程调度上下文。
    readonly SynchronizationContext? mSyncContext = SynchronizationContext.Current;
    readonly object mLock = new();
    bool mPartPending;
    HashSet<string> mPendingNotes = new();
    bool mScheduled;
    bool mDisposed;
}
