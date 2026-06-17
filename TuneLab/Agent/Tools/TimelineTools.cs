using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Agent;

// Layer 2 业务级写工具（时间线：tempo/拍号）。每次调用 = 一个可撤销单位。

// 设速度。不给 atTick 则改基础速度（tick 0）；给了则在该 tick 处设/加 tempo 标记。
internal sealed class SetTempoTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "set_tempo";
    public string Description => "Set the tempo (BPM). Without atTick, sets the base tempo at tick 0; with atTick, sets or adds a tempo marker at that tick.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "bpm": { "type": ["number", "string"], "description": "Beats per minute (> 0)." },
            "atTick": { "type": ["number", "string", "null"], "description": "Optional tick position for the tempo marker." }
          },
          "required": ["bpm"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            return Task.FromResult("OK: " + editor.SetTempo(root.GetDouble("bpm"), root.GetDoubleOrNull("atTick")));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}

// 设拍号。atBarNumber 为 1-based 小节号（默认第 1 小节）。
internal sealed class SetTimeSignatureTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "set_time_signature";
    public string Description => "Set the time signature. atBarNumber is a 1-based bar number (defaults to bar 1).";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "numerator": { "type": ["integer", "string"], "description": "Beats per bar (e.g. 3 in 3/4)." },
            "denominator": { "type": ["integer", "string"], "description": "Beat unit (e.g. 4 in 3/4)." },
            "atBarNumber": { "type": ["integer", "string", "null"], "description": "Optional 1-based bar number where the signature starts." }
          },
          "required": ["numerator", "denominator"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            return Task.FromResult("OK: " + editor.SetTimeSignature(
                root.GetInt("numerator"), root.GetInt("denominator"), root.GetIntOrNull("atBarNumber")));
        }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}
