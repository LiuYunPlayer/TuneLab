using System;
using System.Linq;
using System.Text;
using TuneLab.Data;

namespace TuneLab.Agent;

// agent 工具与工程数据之间的薄门面：把"模型可下达的查询/编辑意图"翻译成数据层命令，
// 并把对 TuneLab.Data 具体 API 的依赖收口在这一层——工具与 agent 循环只依赖本接口，
// 数据层重构时只需改实现。所有写操作都走命令系统、作为一个可撤销单位提交。
//
// 第一版只放两个代表性方法（一只读一写入），用来跑通"模型→工具→改数据→回灌"链路；
// 工具集的细化（音符增删、区间选择、自动化、effect 等）后续单独设计。
internal interface IAgentProjectEditor
{
    // 只读：工程结构摘要文本，作为上下文喂给模型。
    string GetProjectSummary();

    // 写入：把指定轨内所有音符整体升降若干半音（钳到 MIDI 0..127），作为一个可撤销单位提交。
    // 返回实际改动的音符数。trackIndex 越界抛 ArgumentOutOfRangeException（由调用方转成给模型的错误文本）。
    int ShiftTrackPitch(int trackIndex, int semitones);
}

// 绑定到当前工程的 Facade 实现。构造时注入 IProject（由 UI 在为当前文档创建 agent 时传入）。
internal sealed class ProjectAgentEditor(IProject project) : IAgentProjectEditor
{
    public string GetProjectSummary()
    {
        var sb = new StringBuilder();
        var tracks = project.Tracks;
        sb.AppendLine(string.Format("Project has {0} track(s):", tracks.Count));
        for (int i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            var parts = track.Parts.ToList();
            int noteCount = parts.OfType<IMidiPart>().Sum(p => p.Notes.Count());
            sb.AppendLine(string.Format(
                "  [{0}] name=\"{1}\", parts={2}, notes={3}",
                i, track.Name.Value, parts.Count, noteCount));
        }
        return sb.ToString();
    }

    public int ShiftTrackPitch(int trackIndex, int semitones)
    {
        var tracks = project.Tracks;
        if (trackIndex < 0 || trackIndex >= tracks.Count)
            throw new ArgumentOutOfRangeException(nameof(trackIndex), string.Format("track index {0} out of range [0,{1})", trackIndex, tracks.Count));

        int changed = 0;
        var midiParts = tracks[trackIndex].Parts.OfType<IMidiPart>().ToList();
        foreach (var part in midiParts)
        {
            part.BeginMergeDirty();
            part.Notes.BeginMergeNotify();
            foreach (var note in part.Notes)
            {
                int target = Math.Clamp(note.Pitch.Value + semitones, 0, 127);
                if (target != note.Pitch.Value)
                {
                    note.Pitch.Set(target);
                    changed++;
                }
            }
            part.Notes.EndMergeNotify();
            part.EndMergeDirty();
        }

        if (changed > 0)
            project.Commit();

        return changed;
    }
}
