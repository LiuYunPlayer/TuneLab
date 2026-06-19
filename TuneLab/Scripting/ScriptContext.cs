using System;
using System.Collections.Generic;
using TuneLab.Data;
using TuneLab.Foundation;

namespace TuneLab.Scripting;

// 脚本里抛出的"用法/参数错误"——宿主据其 Message 把清晰错误回报给脚本作者（含 agent 模型），不带 C# 栈噪声。
internal sealed class ScriptApiException(string message) : Exception(message);

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

    // 句柄缓存（按底层对象引用）：同一对象多次读取返回同一句柄，使脚本里 === 成立、并支持删除后置失效标记。
    readonly Dictionary<INote, ScriptNote> mNotes = new();
    readonly Dictionary<IPart, ScriptPart> mParts = new();
    readonly Dictionary<ITrack, ScriptTrack> mTracks = new();
    readonly Dictionary<Vibrato, ScriptVibrato> mVibratos = new();

    // 已开 merge 括号的 midi part；Finish() 时统一收口。
    readonly HashSet<IMidiPart> mBracketed = new();
    int mChanges;   // 发生的改动计数（>0 才 Commit）

    public ScriptContext(IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization)
    {
        mProject = project;
        mCurrentPart = currentPart;
        mQuantization = quantization;
    }

    // ── 给根对象（ScriptApp / ScriptProject）读的底层入口 ──
    internal IProject Project => mProject;
    internal IMidiPart? CurrentMidiPart => mCurrentPart?.Invoke();
    internal IQuantization? Quantization => mQuantization?.Invoke();

    // ── 写收口 ──
    internal void Bump() => mChanges++;

    internal void EnsureBracket(IMidiPart midi)
    {
        if (mBracketed.Add(midi))
        {
            midi.BeginMergeDirty();
            midi.Notes.BeginMergeNotify();
        }
    }

    // 关闭所有 merge 括号；有改动则提交成一次可撤销单位。返回是否提交。宿主在 finally 调用（含脚本抛错路径，
    // 此时把"出错前已发生的改动"也作为一个可撤销单位落地——与 apply_edits 的"部分成功也落地"一致）。
    internal bool Finish()
    {
        foreach (var part in mBracketed)
        {
            part.Notes.EndMergeNotify();
            part.EndMergeDirty();
        }
        mBracketed.Clear();
        if (mChanges > 0)
        {
            mProject.Commit();
            return true;
        }
        return false;
    }

    internal int ChangeCount => mChanges;

    // ── 句柄工厂（按引用缓存以保持句柄身份） ──
    internal ScriptNote WrapNote(INote note) => mNotes.TryGetValue(note, out var h) ? h : mNotes[note] = new ScriptNote(this, note);
    internal ScriptPart WrapPart(IPart part) => mParts.TryGetValue(part, out var h) ? h : mParts[part] = new ScriptPart(this, part);
    internal ScriptTrack WrapTrack(ITrack track) => mTracks.TryGetValue(track, out var h) ? h : mTracks[track] = new ScriptTrack(this, track);
    internal ScriptVibrato WrapVibrato(Vibrato vibrato) => mVibratos.TryGetValue(vibrato, out var h) ? h : mVibratos[vibrato] = new ScriptVibrato(this, vibrato);
}
