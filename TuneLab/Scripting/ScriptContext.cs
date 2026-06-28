using System;
using System.Collections.Generic;
using TuneLab.Data;
using TuneLab.Foundation;

namespace TuneLab.Scripting;

// 脚本里抛出的"用法/参数错误"——宿主据其 Message 把清晰错误回报给脚本作者（含 agent 模型），不带 C# 栈噪声。
internal sealed class ScriptApiException(string message) : Exception(message);

// 脚本试图在"别处 UI 操作进行中"（文档有未提交命令）改工程时抛出——只读脚本永不触发。
// 宿主据此特判：用户前台运行→回报；agent→等 Pushable 恢复后自动整段重跑（原子回退保证安全）。
internal sealed class ScriptBlockedException() : Exception("a user editing operation is in progress; the project can't be modified right now.");

// 一次脚本运行的【收口内核 / 共享上下文】，脚本语言面【完全不可见】（不注入 JS，全是 internal 成员）。
// 所有对象——编辑器根 `tl`(ScriptApp)、工程 `tl.currentProject()`(ScriptProject)、各句柄——都持有同一个 ScriptContext，
// 写操作经它统一收口。这是一个【独立模块】：只依赖数据层（TuneLab.Data/Foundation），不依赖 agent。
//
// 写操作【立即改数据层但都不 Commit】，整段脚本跑完由宿主（ScriptRunner）统一收口成一次 Commit（=一个可撤销单位）。
// merge / autoprepare / commit 这些"危险包裹"由本类托管：
//  · Commit 由宿主在最外层 finally 调一次（Finish）；
//  · part 的 BeginMergeDirty/EndMergeDirty（合并通知 + 把合成重活延到括号收口，即 autoprepare 抑制）
//    与 Notes.BeginMergeNotify/EndMergeNotify 由本类【惰性按 part 开】（EnsureBracket）、宿主 Finish() 收口。
//
// 坐标铁律：对外位置/时长一律【绝对（全局）tick】；音高 = MIDI。落库的相对换算在各句柄内完成。
internal sealed class ScriptContext
{
    readonly IProject mProject;
    readonly Func<IMidiPart?>? mCurrentPart;
    readonly Func<IQuantization?>? mQuantization;
    readonly Func<string?>? mLanguage;
    readonly Func<ScriptSelection?>? mSelection;
    readonly Head mStartHead;   // 运行前的撤销锚点（构造时即捕获，早于任何 merge 括号/改动）；出错回退至此
    // 本次运行能否写：构造时取一次 Pushable()。脚本同步跑、运行期用户无法插入新操作，故此值全程不变——
    // 守卫只在"首次写入"时检查它（EnsureWritable），从而只读脚本即便在用户操作中途也畅通，只拦写。
    readonly bool mWritable;

    // 句柄缓存（按底层对象引用）：同一对象多次读取返回同一句柄，使脚本里 === 成立、并支持删除后置失效标记。
    readonly Dictionary<INote, ScriptNote> mNotes = new();
    readonly Dictionary<IPart, ScriptPart> mParts = new();
    readonly Dictionary<ITrack, ScriptTrack> mTracks = new();
    readonly Dictionary<Vibrato, ScriptVibrato> mVibratos = new();

    // 已开 merge 括号的 midi part；Finish() 时统一收口。
    readonly HashSet<IMidiPart> mBracketed = new();
    int mChanges;   // 发生的改动计数（>0 才 Commit）

    public ScriptContext(IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language, Func<ScriptSelection?>? selection)
    {
        mProject = project;
        mCurrentPart = currentPart;
        mQuantization = quantization;
        mLanguage = language;
        mSelection = selection;
        mStartHead = project.Head;
        mWritable = project.Pushable();
    }

    // 首次写入守卫：别处 UI 操作进行中（!Pushable）则拒绝写——只读脚本不调用任何写入口，故永不触发。
    internal void EnsureWritable()
    {
        if (!mWritable)
            throw new ScriptBlockedException();
    }

    // ── 给根对象（ScriptApp / ScriptProject）读的底层入口 ──
    internal IProject Project => mProject;
    internal IMidiPart? CurrentMidiPart => mCurrentPart?.Invoke();
    internal IQuantization? Quantization => mQuantization?.Invoke();
    // 编排区范围选区快照（编辑器态）；无源或无选区时 null。
    internal ScriptSelection? Selection => mSelection?.Invoke();
    // 当前界面语言文化码（如 "zh-CN"）；脚本经 tl.language 读，用于本地化显示名/对话框文案。无源时空串。
    internal string Language => mLanguage?.Invoke() ?? "";

    // ── 写收口 ──
    internal void Bump() => mChanges++;

    internal void EnsureBracket(IMidiPart midi)
    {
        EnsureWritable();   // 所有音符/曲线/颤音写入都经此，是它们的统一写守卫点
        if (mBracketed.Add(midi))
        {
            midi.BeginMergeDirty();
            midi.Notes.BeginMergeNotify();
        }
    }

    // 关闭所有 merge 括号后收口。宿主在最外层 finally 调用：
    //  · rollback=false 且有改动 → Commit 成一个可撤销单位，返回 true；
    //  · rollback=true（脚本抛错/超时/取消）或无改动 → DiscardTo(startHead) 撤掉本次运行的全部未提交命令
    //    （含 merge 括号），干净回退到跑脚本前状态，返回 false。
    // 必须先关括号再收口：先 EndMerge 让 Begin/End 在未提交栈里成对平衡，DiscardTo 的逆序 Undo 才与一次正常 Undo 等价。
    internal bool Finish(bool rollback)
    {
        foreach (var part in mBracketed)
        {
            part.Notes.EndMergeNotify();
            part.EndMergeDirty();
        }
        mBracketed.Clear();
        if (!rollback && mChanges > 0)
        {
            mProject.Commit();
            return true;
        }
        mProject.DiscardTo(mStartHead);
        return false;
    }

    internal int ChangeCount => mChanges;

    // ── 句柄工厂（按引用缓存以保持句柄身份） ──
    internal ScriptNote WrapNote(INote note) => mNotes.TryGetValue(note, out var h) ? h : mNotes[note] = new ScriptNote(this, note);
    internal ScriptPart WrapPart(IPart part) => mParts.TryGetValue(part, out var h) ? h : mParts[part] = new ScriptPart(this, part);
    internal ScriptTrack WrapTrack(ITrack track) => mTracks.TryGetValue(track, out var h) ? h : mTracks[track] = new ScriptTrack(this, track);
    internal ScriptVibrato WrapVibrato(Vibrato vibrato) => mVibratos.TryGetValue(vibrato, out var h) ? h : mVibratos[vibrato] = new ScriptVibrato(this, vibrato);
}
