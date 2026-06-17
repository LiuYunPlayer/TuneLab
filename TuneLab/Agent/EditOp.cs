using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.Agent;

// apply_edits 批量工具的操作词汇（op-DSL）。每个 op 是一条"逐字段/原子级"编辑意图；
// 一整批 op 由 IAgentProjectEditor.ApplyEdits 在一对 BeginMergeDirty/EndMergeDirty 内执行、
// 末尾单次 Commit——整批是一个可撤销单位。
//
// 寻址约定（与所有 agent 工具一致）：TrackNumber/PartNumber/NoteNumber 均为 1-based（贴合
// 用户认知，"第 1 轨"即首轨）；editor 在边界处一次性转 0-based 内部寻址。位置/时长单位为 tick
// （PPQ=480），且**位置均为绝对（全局）tick**——与 playhead/overview/小节同坐标系；editor 在落数据时
// 减去 part 起点转成数据层的 part 相对坐标，模型侧无需做任何换算。批内 NoteNumber 对"批开始时"的音符
// 顺序快照解析、按对象引用施改，故批内增删不影响同批后续 op 的编号；本批新增的音符在同批内不可按编号再寻址。
internal abstract record AgentEditOp
{
    public required int TrackNumber { get; init; }
    public required int PartNumber { get; init; }
}

// 在 part 内新增一个音符。
internal sealed record AddNoteOp : AgentEditOp
{
    public required double Pos { get; init; }   // tick，相对 part
    public required double Dur { get; init; }   // tick
    public required int Pitch { get; init; }    // MIDI 0..127
    public string Lyric { get; init; } = string.Empty;
}

// 修改已存在音符的若干字段（null 字段不动）。改 Pos/Dur 会触发摘除-重插以维持有序。
internal sealed record SetNoteOp : AgentEditOp
{
    public required int NoteNumber { get; init; }
    public int? Pitch { get; init; }
    public double? Pos { get; init; }
    public double? Dur { get; init; }
    public string? Lyric { get; init; }
}

// 删除指定编号音符。
internal sealed record DeleteNoteOp : AgentEditOp
{
    public required int NoteNumber { get; init; }
}

// 删除 [Start,End) tick 范围内（按起点判定）的全部音符。
internal sealed record DeleteNotesInRangeOp : AgentEditOp
{
    public required double Start { get; init; }
    public required double End { get; init; }
}

// 覆盖写一段音高曲线：先清空 [Start,End)，再按 Points 落一条线。
// Point.X = tick（相对 part），Point.Y = 音高偏移（半音），叠加在音符基准音高之上。
internal sealed record SetPitchLineOp : AgentEditOp
{
    public required double Start { get; init; }
    public required double End { get; init; }
    public required IReadOnlyList<Point> Points { get; init; }
}

// 清空 [Start,End) 的音高曲线。
internal sealed record ClearPitchOp : AgentEditOp
{
    public required double Start { get; init; }
    public required double End { get; init; }
}

// 覆盖写一段自动化参数曲线：先清空 [Start,End)，再按 Points 落一条线。
// AutomationId 为参数轨 id（取自 voice/effect 声明的 AutomationConfigs）；不存在时按需创建。
// Point.X = tick，Point.Y = 参数绝对值（含 DefaultValue）。DefaultValue 非空时一并设默认值。
internal sealed record SetAutomationLineOp : AgentEditOp
{
    public required string AutomationId { get; init; }
    public required double Start { get; init; }
    public required double End { get; init; }
    public required IReadOnlyList<Point> Points { get; init; }
    public double? DefaultValue { get; init; }
}

// 清空某参数轨 [Start,End) 的曲线。
internal sealed record ClearAutomationOp : AgentEditOp
{
    public required string AutomationId { get; init; }
    public required double Start { get; init; }
    public required double End { get; init; }
}
