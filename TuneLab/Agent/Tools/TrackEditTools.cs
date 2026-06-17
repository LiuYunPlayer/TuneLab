using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Agent;

// Layer 2 业务级写工具（轨）。每个工具一次调用 = 一个可撤销单位。寻址 1-based。

// 改轨属性：名/静音/独奏/增益(dB)/声像。只传想改的字段。
internal sealed class SetTrackPropertiesTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "set_track_properties";
    public string Description => "Set properties of a track. Only the fields you provide are changed.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackNumber": { "type": ["integer", "string"], "description": "1-based track number." },
            "name": { "type": ["string", "null"], "description": "New track name." },
            "mute": { "type": ["boolean", "string", "null"] },
            "solo": { "type": ["boolean", "string", "null"] },
            "gainDb": { "type": ["number", "string", "null"], "description": "Track gain in decibels (0 = unity)." },
            "pan": { "type": ["number", "string", "null"], "description": "Pan in [-1,1]; -1 left, 0 center, 1 right." }
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
            var root = doc.RootElement;
            return Task.FromResult(editor.SetTrackProperties(
                root.GetInt("trackNumber"), root.GetStringOrNull("name"), root.GetBoolOrNull("mute"),
                root.GetBoolOrNull("solo"), root.GetDoubleOrNull("gainDb"), root.GetDoubleOrNull("pan")));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}

// 新增一条空轨，返回其 1-based 编号。
internal sealed class AddTrackTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "add_track";
    public string Description => "Add a new empty track at the end of the project. Returns the new track's 1-based number.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "name": { "type": ["string", "null"], "description": "Optional name for the new track." }
          },
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            int number = editor.AddTrack(doc.RootElement.GetStringOrNull("name"));
            return Task.FromResult(string.Format("OK: added track {0}.", number));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}

// 删除指定 1-based 轨。
internal sealed class RemoveTrackTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "remove_track";
    public string Description => "Remove a track by its 1-based number. This is undoable.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackNumber": { "type": ["integer", "string"], "description": "1-based track number to remove." }
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
            int number = doc.RootElement.GetInt("trackNumber");
            editor.RemoveTrack(number);
            return Task.FromResult(string.Format("OK: removed track {0}.", number));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}
