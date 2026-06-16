using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Agent;

// 代表性写入工具：把某轨所有音符整体升降若干半音。用来验证"模型下达编辑 → 走命令系统 → 可撤销"这条链路。
internal sealed class ShiftPitchTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "shift_track_pitch";
    public string Description => "Transpose all notes in a track up or down by a number of semitones. Use a negative value to shift down.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "trackIndex": { "type": "integer", "description": "Zero-based index of the track to transpose." },
            "semitones": { "type": "integer", "description": "Semitones to shift; negative shifts down." }
          },
          "required": ["trackIndex", "semitones"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        int trackIndex, semitones;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            trackIndex = root.GetProperty("trackIndex").GetInt32();
            semitones = root.GetProperty("semitones").GetInt32();
        }
        catch (Exception ex)
        {
            return Task.FromResult("Error: invalid arguments — " + ex.Message);
        }

        try
        {
            int changed = editor.ShiftTrackPitch(trackIndex, semitones);
            return Task.FromResult(string.Format("OK: shifted {0} note(s) in track {1} by {2} semitone(s).", changed, trackIndex, semitones));
        }
        catch (Exception ex)
        {
            return Task.FromResult("Error: " + ex.Message);
        }
    }
}
