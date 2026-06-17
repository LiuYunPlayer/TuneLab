using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Agent;

// Layer 3 批量工具：把一串逐字段编辑（op-DSL）作为一个可撤销单位施加。
// 适合需要多步、细粒度改动的场景（写旋律、批量改音符、画参数曲线）——比逐个业务工具往返省 token，
// 且整批要么一起撤销。解析（JSON → 类型化 op）在本工具，落数据/命令系统在 IAgentProjectEditor.ApplyEdits。
internal sealed class ApplyEditsTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "apply_edits";

    public string Description =>
        "Apply a batch of fine-grained edits as ONE undoable change. Use this for melody writing, multi-note edits, " +
        "or drawing parameter curves. All tick positions are ABSOLUTE (global) ticks — same space as the playhead and bars (PPQ from get_project_overview); track/part/note numbers are 1-based. " +
        "Within one batch, note numbers refer to the part's note order BEFORE the batch " +
        "(by snapshot), so deletes/inserts in the same batch don't shift the numbering of later ops; notes you add in this batch " +
        "cannot be addressed by number within the same batch. Each edit is an object with an \"op\" field. Supported ops:\n" +
        "  add_note: {trackNumber, partNumber, pos, dur, pitch, lyric?}\n" +
        "  set_note: {trackNumber, partNumber, noteNumber, pitch?, pos?, dur?, lyric?}\n" +
        "  delete_note: {trackNumber, partNumber, noteNumber}\n" +
        "  delete_notes_in_range: {trackNumber, partNumber, start, end}\n" +
        "  set_pitch_line: {trackNumber, partNumber, start, end, points} — clears [start,end) then draws a line; points=[{tick,value}], value=MIDI pitch (e.g. 60=C4, fractional allowed)\n" +
        "  clear_pitch: {trackNumber, partNumber, start, end}\n" +
        "  set_automation_line: {trackNumber, partNumber, automationId, start, end, points, defaultValue?} — points=[{tick,value}], value=absolute parameter value; call get_part_parameters to discover automation ids (e.g. Volume, VibratoEnvelope)\n" +
        "  clear_automation: {trackNumber, partNumber, automationId, start, end}";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "edits": {
              "type": ["array", "string"],
              "description": "Ordered list of edit ops. Each item is an object with an \"op\" field plus that op's fields (see tool description). Must be a JSON array, not a stringified array.",
              "items": { "type": "object", "additionalProperties": true }
            }
          },
          "required": ["edits"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        List<AgentEditOp> ops;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var edits = doc.RootElement.Require("edits");
            // 容错：部分模型会把数组整体序列化成 JSON 字符串，这里再解析一层。
            JsonDocument? inner = null;
            try
            {
                if (edits.ValueKind == JsonValueKind.String)
                {
                    inner = JsonDocument.Parse(edits.GetString() ?? "");
                    edits = inner.RootElement;
                }
                if (edits.ValueKind != JsonValueKind.Array)
                    return Task.FromResult("Error: \"edits\" must be a JSON array of edit ops.");

                ops = new List<AgentEditOp>(edits.GetArrayLength());
                int index = 0;
                foreach (var e in edits.EnumerateArray())
                {
                    index++;
                    try { ops.Add(ParseOp(e)); }
                    catch (Exception ex) { return Task.FromResult(string.Format("Error: edit #{0} is invalid — {1}", index, ex.Message)); }
                }
            }
            finally { inner?.Dispose(); }
        }
        catch (Exception ex)
        {
            return Task.FromResult("Error: invalid arguments — " + ex.Message);
        }

        try { return Task.FromResult(editor.ApplyEdits(ops)); }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }

    static AgentEditOp ParseOp(JsonElement e)
    {
        string kind = e.GetString("op");
        int track = e.GetInt("trackNumber");
        int part = e.GetInt("partNumber");
        switch (kind)
        {
            case "add_note":
                return new AddNoteOp { TrackNumber = track, PartNumber = part, Pos = e.GetDouble("pos"), Dur = e.GetDouble("dur"), Pitch = e.GetInt("pitch"), Lyric = e.GetStringOrNull("lyric") ?? string.Empty };
            case "set_note":
                return new SetNoteOp { TrackNumber = track, PartNumber = part, NoteNumber = e.GetInt("noteNumber"), Pitch = e.GetIntOrNull("pitch"), Pos = e.GetDoubleOrNull("pos"), Dur = e.GetDoubleOrNull("dur"), Lyric = e.GetStringOrNull("lyric") };
            case "delete_note":
                return new DeleteNoteOp { TrackNumber = track, PartNumber = part, NoteNumber = e.GetInt("noteNumber") };
            case "delete_notes_in_range":
                return new DeleteNotesInRangeOp { TrackNumber = track, PartNumber = part, Start = e.GetDouble("start"), End = e.GetDouble("end") };
            case "set_pitch_line":
                return new SetPitchLineOp { TrackNumber = track, PartNumber = part, Start = e.GetDouble("start"), End = e.GetDouble("end"), Points = ParsePoints(e.Require("points")) };
            case "clear_pitch":
                return new ClearPitchOp { TrackNumber = track, PartNumber = part, Start = e.GetDouble("start"), End = e.GetDouble("end") };
            case "set_automation_line":
                return new SetAutomationLineOp { TrackNumber = track, PartNumber = part, AutomationId = e.GetString("automationId"), Start = e.GetDouble("start"), End = e.GetDouble("end"), Points = ParsePoints(e.Require("points")), DefaultValue = e.GetDoubleOrNull("defaultValue") };
            case "clear_automation":
                return new ClearAutomationOp { TrackNumber = track, PartNumber = part, AutomationId = e.GetString("automationId"), Start = e.GetDouble("start"), End = e.GetDouble("end") };
            default:
                throw new ArgumentException("unknown op \"" + kind + "\".");
        }
    }

    static IReadOnlyList<Point> ParsePoints(JsonElement arr)
    {
        if (arr.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("\"points\" must be an array of {tick,value}.");
        var list = new List<Point>(arr.GetArrayLength());
        foreach (var p in arr.EnumerateArray())
            list.Add(new Point(p.GetDouble("tick"), p.GetDouble("value")));
        return list;
    }
}
