using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Agent;

// Layer 2 业务级写工具（part）。每个工具一次调用 = 一个可撤销单位。寻址 1-based，位置/时长用绝对 tick。
// part 是音符/曲线的容器：apply_edits 的 note/pitch/automation op 都作用于已存在的 part，故当目标轨没有 midi part
// （例如要从零写旋律）时，必须先 add_part 建一个、再用它的编号往里写。

// 在某轨新建一个空 midi part，返回其插入后的 1-based 编号（part 按起点排序，新编号取决于 pos）。
internal sealed class AddPartTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "add_part";
    public string Description =>
        "Create a new empty MIDI part on a track, so you can write notes into it. Use this when a track has no part to put notes in " +
        "(e.g. writing a melody from scratch). pos/dur are in ABSOLUTE ticks (PPQ from get_project_overview); size dur to cover the notes " +
        "you plan to write (you can resize later with set_part_properties). Returns the new part's 1-based number — use that number with " +
        "apply_edits to add notes. trackNumber is 1-based.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackNumber": { "type": ["integer", "string"], "description": "1-based track number to add the part to." },
            "pos": { "type": ["number", "string"], "description": "Part start position, absolute tick (>= 0)." },
            "dur": { "type": ["number", "string"], "description": "Part length in ticks (> 0). Size it to cover the notes you will write." },
            "name": { "type": ["string", "null"], "description": "Optional name for the new part." }
          },
          "required": ["trackNumber", "pos", "dur"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            int number = editor.AddPart(root.GetInt("trackNumber"), root.GetDouble("pos"), root.GetDouble("dur"), root.GetStringOrNull("name"));
            return Task.FromResult(string.Format(
                "OK: added part {0} on track {1}. Address notes with trackNumber={1}, partNumber={0}.", number, root.GetInt("trackNumber")));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}

// 删除某轨的某 part（midi 或 audio 均可）。
internal sealed class RemovePartTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "remove_part";
    public string Description => "Remove a part from a track by its 1-based number. This is undoable. trackNumber/partNumber are 1-based.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackNumber": { "type": ["integer", "string"], "description": "1-based track number." },
            "partNumber": { "type": ["integer", "string"], "description": "1-based part number on that track to remove." }
          },
          "required": ["trackNumber", "partNumber"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            int track = root.GetInt("trackNumber");
            int part = root.GetInt("partNumber");
            editor.RemovePart(track, part);
            return Task.FromResult(string.Format("OK: removed part {0} from track {1}.", part, track));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}

// 改 part 名/起点(移动)/时长(缩放)。只改所给字段；改 pos/dur 可能改变其编号（part 按起点排序），返回变更后的编号。
internal sealed class SetPartPropertiesTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "set_part_properties";
    public string Description =>
        "Set properties of a part: name, pos (move), dur (resize). Only the fields you provide are changed. pos/dur are in ABSOLUTE ticks. " +
        "Moving or resizing can change the part's 1-based number (parts are ordered by start tick) — the result reports the new number. " +
        "trackNumber/partNumber are 1-based.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackNumber": { "type": ["integer", "string"], "description": "1-based track number." },
            "partNumber": { "type": ["integer", "string"], "description": "1-based part number on that track." },
            "name": { "type": ["string", "null"], "description": "New part name." },
            "pos": { "type": ["number", "string", "null"], "description": "New start position, absolute tick (>= 0)." },
            "dur": { "type": ["number", "string", "null"], "description": "New length in ticks (> 0)." }
          },
          "required": ["trackNumber", "partNumber"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            return Task.FromResult(editor.SetPartProperties(
                root.GetInt("trackNumber"), root.GetInt("partNumber"),
                root.GetStringOrNull("name"), root.GetDoubleOrNull("pos"), root.GetDoubleOrNull("dur")));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}
