using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Agent;

// Layer 1 只读工具（除 get_project_overview 外）。读工具不改数据、不进命令系统，
// 仅把工程明细格式化回灌给模型作为上下文。寻址全用 1-based，位置用 tick。

// 当前在钢琴窗打开编辑的 part：返回其 1-based 轨/part 序号 + 概要。用户说"当前/这个 part"时先调它解析序号。
internal sealed class GetCurrentPartTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "get_current_part";
    public string Description => "Get the part currently open in the piano editor, with its 1-based track and part numbers. Call this when the user refers to \"the current part\", \"this part\" or \"the part I'm editing\" without giving numbers.";
    public string ParametersJsonSchema => """{ "type": "object", "properties": {}, "additionalProperties": false }""";

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
        => Task.FromResult(editor.GetCurrentPart());
}

// 播放线位置：tick + 秒 + 小节:拍 + 是否播放中。用户说"播放线/这里/当前位置"时调它取 tick。
internal sealed class GetPlayheadTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "get_playhead";
    public string Description => "Get the current playhead position as a tick (and seconds, bar:beat) plus whether playback is running. Call this when the user refers to \"the playhead\", \"here\" or \"the current position\" instead of giving a tick.";
    public string ParametersJsonSchema => """{ "type": "object", "properties": {}, "additionalProperties": false }""";

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
        => Task.FromResult(editor.GetPlayhead());
}

// 把一个绝对 tick 吸附到当前量化网格。播放线本身不吸附，写旋律要对齐网格时用它把目标 tick 规整。
internal sealed class SnapTickTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "snap_tick";
    public string Description => "Snap an absolute tick to the current quantization grid (the piano editor's snap setting). The playhead is not grid-aligned, so when placing notes on the grid (e.g. writing a melody), snap the target tick first. Returns the snapped absolute tick and the grid cell size.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "tick": { "type": ["number", "string"], "description": "Absolute tick to snap to the grid." }
          },
          "required": ["tick"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            return Task.FromResult(editor.SnapTick(doc.RootElement.GetDouble("tick")));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}

// 某轨明细：轨属性 + 各 part（1-based 编号、tick 区间、voice、音符数）。
internal sealed class GetTrackDetailTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "get_track_detail";
    public string Description => "Get details of one track: its properties and the list of its parts (1-based part numbers, tick range, voice, note count).";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackNumber": { "type": ["integer", "string"], "description": "1-based track number." }
          },
          "required": ["trackNumber"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            return Task.FromResult(editor.GetTrackDetail(doc.RootElement.GetInt("trackNumber")));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}

// 列出某 midi part 的音符（可选 tick 区间过滤）。NoteNumber 为 part 内 1-based 编号，apply_edits 用它寻址。
internal sealed class GetPartNotesTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "get_part_notes";
    public string Description => "List the notes of a midi part: 1-based NoteNumber, position and duration in ticks, pitch (MIDI number + note name), and lyric. Optionally filter by a tick range. Use the returned NoteNumber to target notes in apply_edits.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackNumber": { "type": ["integer", "string"], "description": "1-based track number." },
            "partNumber": { "type": ["integer", "string"], "description": "1-based part number within the track." },
            "startTick": { "type": ["number", "string", "null"], "description": "Optional: only list notes whose position >= this tick." },
            "endTick": { "type": ["number", "string", "null"], "description": "Optional: only list notes whose position < this tick." }
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
            return Task.FromResult(editor.GetPartNotes(
                root.GetInt("trackNumber"), root.GetInt("partNumber"),
                root.GetDoubleOrNull("startTick"), root.GetDoubleOrNull("endTick")));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}

// 列出某 midi part 可编辑的参数（pitch + 全部 voice 级自动化轨，含宿主自带 Volume/VibratoEnvelope 与引擎声明的）。
// 是 get_parameter / apply_edits 自动化类 op 的发现入口——先列再按返回的 id 操作。
internal sealed class GetPartParametersTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "get_part_parameters";
    public string Description => "List the editable parameters of a midi part: \"pitch\" plus every available automation id (including built-in ones like Volume and VibratoEnvelope, and any the voice declares), each with display name, value range and default. Call this to discover automation ids before reading or editing them.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackNumber": { "type": ["integer", "string"], "description": "1-based track number." },
            "partNumber": { "type": ["integer", "string"], "description": "1-based part number within the track." }
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
            return Task.FromResult(editor.GetPartParameters(root.GetInt("trackNumber"), root.GetInt("partNumber")));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}

// 采样某 part 的参数曲线（"pitch" 或某自动化轨 id）在 tick 区间上的取值。
internal sealed class GetParameterTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "get_parameter";
    public string Description => "Sample a parameter curve of a midi part over a tick range. Use \"pitch\" for the final pitch curve (MIDI scale), or an automation id declared by the part's voice/effect. Returns evenly spaced samples.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackNumber": { "type": ["integer", "string"], "description": "1-based track number." },
            "partNumber": { "type": ["integer", "string"], "description": "1-based part number within the track." },
            "parameterId": { "type": "string", "description": "\"pitch\" or an automation id." },
            "startTick": { "type": ["number", "string"], "description": "Range start in ticks." },
            "endTick": { "type": ["number", "string"], "description": "Range end in ticks (must be > startTick)." },
            "samples": { "type": ["integer", "string", "null"], "description": "Number of evenly spaced samples (2..200, default 16)." }
          },
          "required": ["trackNumber", "partNumber", "parameterId", "startTick", "endTick"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            return Task.FromResult(editor.GetParameterValues(
                root.GetInt("trackNumber"), root.GetInt("partNumber"), root.GetString("parameterId"),
                root.GetDouble("startTick"), root.GetDouble("endTick"), root.GetIntOrNull("samples") ?? 16));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}
