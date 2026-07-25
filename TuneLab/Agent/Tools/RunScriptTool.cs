using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Agent;

// 逃生口工具：让模型写一段 JavaScript 表达复杂/批量/带循环条件的工程编辑（音乐编辑高度契合，
// 如"5-8 小节每音符升八度再加三度和声"=一个循环，一轮搞定、省下几十次 tool 往返）。
//
// 工具本身很薄：解析 code 字符串，交给共享的写执行器（ScriptWriteExecutor）过授权闸门后运行。
// 脚本引擎、动作面 API、沙箱、整段=一次 Commit 的收口都在脚本模块里（TuneLab.Scripting）；
// 分级授权 + 预览 + 写守卫 wait-retry 在 ScriptWriteExecutor 里——与 run_saved_script 共用同一写路径（单一动作面 SSOT）。
internal sealed class RunScriptTool(ScriptWriteExecutor executor) : IAgentTool
{
    public string Name => "run_script";

    public string Description =>
        "Run a short JavaScript program to edit the project via the global `tl` object. Use this for complex, bulk, computed, or conditional edits " +
        "that would otherwise take many tool calls — e.g. \"for every note in bars 5-8, raise it an octave and add a harmony a third above\" is one loop. " +
        "The whole script runs as ONE undoable change. " +
        "BEFORE writing your first script in a conversation, call get_script_api once to load the full API, the handle/tick rules, and examples — do not guess method names. " +
        "Key rules: object-style — `tl` is the project, while tracks/parts/notes are handles with read/write fields (n.pitch += 1) and methods (part.notes(), note.remove()); " +
        "collection methods return plain arrays (for-of/index, not a linked list); positions are absolute ticks; pitch is MIDI; print(x) emits debug output. " +
        "NOTE: depending on the user's authorization setting your edits may be applied only after the user confirms, or not applied at all (read-only) — the result message tells you what happened; relay it, don't assume the edit landed.";

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

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string code;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            code = doc.RootElement.GetString("code");
        }
        catch (Exception ex)
        {
            return "Error: invalid arguments — " + ex.Message;
        }

        if (string.IsNullOrWhiteSpace(code))
            return "Error: \"code\" is empty.";

        // 内联脚本无入参（inputs=null）；命名脚本的入参路径在 run_saved_script。共用同一授权闸门与收口。
        return await executor.RunWithAuthorizationAsync(code, inputs: null, cancellationToken);
    }
}
