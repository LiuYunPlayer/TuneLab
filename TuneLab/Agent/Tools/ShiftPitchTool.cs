using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Agent;

// 业务级写工具：把某 part 的音符整体升降若干半音（可选 tick 区间），作为一个可撤销单位。
// 这是"移动音符音高"的正确工具（区别于画音高曲线的 set_pitch_line）。
internal sealed class TransposeNotesTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "transpose_notes";
    public string Description => "Transpose (move) the pitch of notes in a part up or down by a number of semitones (e.g. +12 = up one octave, negative = down). Optionally limit to a tick range. This changes note pitches; it is the correct tool for transposing, unlike set_pitch_line which only draws a pitch curve.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackNumber": { "type": ["integer", "string"], "description": "1-based track number." },
            "partNumber": { "type": ["integer", "string"], "description": "1-based part number within the track." },
            "semitones": { "type": ["integer", "string"], "description": "Semitones to shift; negative shifts down. +12 = up one octave." },
            "startTick": { "type": ["number", "string", "null"], "description": "Optional: only transpose notes whose position >= this tick." },
            "endTick": { "type": ["number", "string", "null"], "description": "Optional: only transpose notes whose position < this tick." }
          },
          "required": ["trackNumber", "partNumber", "semitones"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            int changed = editor.TransposeNotes(
                root.GetInt("trackNumber"), root.GetInt("partNumber"), root.GetInt("semitones"),
                root.GetDoubleOrNull("startTick"), root.GetDoubleOrNull("endTick"));
            return Task.FromResult(string.Format("OK: transposed {0} note(s).", changed));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}

// 业务级写工具：在某 part 的一段 tick 区间上添加一个颤音（Vibrato 对象，让音高真正抖动）。
// 区别于 VibratoEnvelope 自动化——那只缩放已有颤音的深度，单独写它不产生任何颤音。
internal sealed class AddVibratoTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "add_vibrato";
    public string Description => "Add a vibrato (pitch oscillation) over a tick range of a part — this creates the real Vibrato that wiggles the pitch. Use this when the user asks to add vibrato/颤音. Do NOT use the VibratoEnvelope automation for this; that only scales the depth of an existing vibrato and adds nothing on its own.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackNumber": { "type": ["integer", "string"], "description": "1-based track number." },
            "partNumber": { "type": ["integer", "string"], "description": "1-based part number within the track." },
            "startTick": { "type": ["number", "string"], "description": "Absolute start tick of the vibrato." },
            "endTick": { "type": ["number", "string"], "description": "Absolute end tick (must be > startTick)." },
            "frequency": { "type": ["number", "string", "null"], "description": "Vibrato rate in Hz (default 6)." },
            "amplitude": { "type": ["number", "string", "null"], "description": "Vibrato depth in semitones (default 1)." }
          },
          "required": ["trackNumber", "partNumber", "startTick", "endTick"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            return Task.FromResult("OK: " + editor.AddVibrato(
                root.GetInt("trackNumber"), root.GetInt("partNumber"),
                root.GetDouble("startTick"), root.GetDouble("endTick"),
                root.GetDoubleOrNull("frequency"), root.GetDoubleOrNull("amplitude")));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}

// 业务级写工具：把某轨所有音符整体升降若干半音，作为一个可撤销单位。
internal sealed class ShiftPitchTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "shift_track_pitch";
    public string Description => "Transpose all notes in a track up or down by a number of semitones. Use a negative value to shift down.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackNumber": { "type": ["integer", "string"], "description": "1-based track number (track 1 is the first track)." },
            "semitones": { "type": ["integer", "string"], "description": "Semitones to shift; negative shifts down." }
          },
          "required": ["trackNumber", "semitones"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        int trackNumber, semitones;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            trackNumber = root.GetInt("trackNumber");
            semitones = root.GetInt("semitones");
        }
        catch (Exception ex)
        {
            return Task.FromResult("Error: invalid arguments — " + ex.Message);
        }

        try
        {
            int changed = editor.ShiftTrackPitch(trackNumber, semitones);
            return Task.FromResult(string.Format("OK: shifted {0} note(s) in track {1} by {2} semitone(s).", changed, trackNumber, semitones));
        }
        catch (Exception ex)
        {
            return Task.FromResult("Error: " + ex.Message);
        }
    }
}
