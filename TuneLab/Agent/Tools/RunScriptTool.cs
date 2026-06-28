using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.Scripting;

namespace TuneLab.Agent;

// 逃生口工具：让模型写一段 JavaScript 表达复杂/批量/带循环条件的工程编辑（音乐编辑高度契合，
// 如"5-8 小节每音符升八度再加三度和声"=一个循环，一轮搞定、省下几十次 tool 往返）。
//
// 工具本身很薄：解析 code 字符串，交给【独立的脚本模块】(TuneLab.Scripting) 执行，把日志/结果/错误回灌。
// 脚本引擎、动作面 API、沙箱、整段=一次 Commit 的收口都在脚本模块里，不在 agent 层——agent 只是它的一个消费者。
internal sealed class RunScriptTool(IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language) : IAgentTool
{
    public string Name => "run_script";

    public string Description =>
        "Run a short JavaScript program to edit the project via the global `tl` object. Use this for complex, bulk, computed, or conditional edits " +
        "that would otherwise take many tool calls — e.g. \"for every note in bars 5-8, raise it an octave and add a harmony a third above\" is one loop. " +
        "The whole script runs as ONE undoable change. " +
        "BEFORE writing your first script in a conversation, call get_script_api once to load the full API, the handle/tick rules, and examples — do not guess method names. " +
        "Key rules: object-style — `tl` is the project, while tracks/parts/notes are handles with read/write fields (n.pitch += 1) and methods (part.notes(), note.remove()); " +
        "collection methods return plain arrays (for-of/index, not a linked list); positions are absolute ticks; pitch is MIDI; print(x) emits debug output.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "code": { "type": "string", "description": "JavaScript source to run. Use the `tl` global to read/edit the project and print(...) for debugging output." }
          },
          "required": ["code"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string code;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            code = doc.RootElement.GetString("code");
        }
        catch (Exception ex)
        {
            return Task.FromResult("Error: invalid arguments — " + ex.Message);
        }

        if (string.IsNullOrWhiteSpace(code))
            return Task.FromResult("Error: \"code\" is empty.");

        ScriptRunResult result;
        try { result = ScriptRunner.Run(project, currentPart, quantization, language, ScriptLimits.Agent, code, cancellationToken); }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }

        var sb = new StringBuilder();
        if (result.Ok)
        {
            sb.Append(result.Committed
                ? string.Format("Script ran OK. Applied {0} edit(s) as one undoable change.", result.Changes)
                : "Script ran OK. No changes were made.");
        }
        else
        {
            sb.Append("Script error: ").Append(result.Error);
            // 原子回退：出错时跑脚本前的全部改动已撤销，工程未变。修脚本本身后重跑，不要基于当前状态打补丁。
            sb.Append("\n(All changes were rolled back; the project is unchanged. Fix the script and re-run — do not patch from current state.)");
        }
        if (!string.IsNullOrEmpty(result.Output))
            sb.Append("\n--- output ---\n").Append(result.Output.TrimEnd('\n'));
        if (result.Ok && !string.IsNullOrEmpty(result.ResultText))
            sb.Append("\n--- result ---\n").Append(result.ResultText);
        return Task.FromResult(sb.ToString());
    }
}
