using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.Scripting;
using TuneLab.SDK;
using TuneLab.Utils;
using TuneLab.Commands;

namespace TuneLab.Agent;

// E1「全能 agent 闭环」的写那一件：按名跑库里的脚本（可省入参）。读那半边（入参 schema/上次值）已搬进
// 命令面（`script inputs`），两边共用 SavedScriptSupport 的字段口径。
// 与 save/delete_script 及命令面的 script 读命令一起构成脚本库闭环——agent 帮用户写好工具脚本存库后，
// 日后能自己读参数、代跑一次，无需重写代码。写路径与 run_script 共用 ScriptWriteExecutor（同一授权闸门 / 收口）。

// run_saved_script：按库名读源码运行；inputs 可省——agent 给了就覆盖在用户上次值之上再补默认，没给则用上次/默认。
// 走与 run_script 相同的授权闸门（ScriptWriteExecutor）。政策：agent 跑【不回写】用户的 ScriptInputMemory 上次值
// （agent 的选择留在其对话历史，不污染用户手动运行的记忆）。见 docs §2.5 + project_agent_feature_progress。
internal sealed class RunSavedScriptTool(ScriptWriteExecutor executor, IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language, Func<ScriptSelection?>? selection, Func<ScriptPianoSelection?>? pianoSelection) : IAgentTool
{
    public string Name => "run_saved_script";

    public string Description =>
        "Run a script already saved in the user's library, by name — like pressing its menu item for the user. " +
        "Use this to reuse a tool the user (or you) saved earlier instead of rewriting it with run_script. " +
        "`inputs` is optional: pass a map of input-name -> value for the fields you want to set (call get_script_inputs first to see them); " +
        "any field you omit falls back to the user's last value, else the config default. Omit `inputs` entirely to run with last/default values. " +
        "Runs as ONE undoable change through the SAME authorization gate as run_script (may be applied only after the user confirms, or not at all in read-only) — relay the result, don't assume it landed. " +
        "Your inputs are NOT saved as the user's last values.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Library name of the script to run (without .js)." },
            "inputs": { "type": "object", "description": "Optional map of input-name -> value overriding the user's last values. Omit to use last/default values." }
          },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string name;
        PropertyObject? agentInputs = null;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            name = doc.RootElement.GetString("name");
            if (doc.RootElement.TryGetProperty("inputs", out var inp) && inp.ValueKind == JsonValueKind.Object)
                agentInputs = PropertyJsonUtils.ToPropertyObject(JObject.Parse(inp.GetRawText()));
        }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        name = ScriptLibrary.SanitizeName((name ?? "").Trim());
        if (string.IsNullOrWhiteSpace(name) || !ScriptLibrary.Exists(name))
            return "Error: " + SavedScriptSupport.NotFound(name);

        string code;
        try { code = ScriptLibrary.Read(name); }
        catch (Exception ex) { return "Error: " + ex.Message; }

        var (scriptId, hasInputs) = SavedScriptSupport.Inspect(name, code, project, currentPart, quantization, language);

        PropertyObject? inputs = null;
        // 非入参脚本（无 getInputConfig）：脚本忽略入参，直接跑，不去 eval getInputConfig（普通脚本那样做会跑其脚本体）。
        // agent 误传的 inputs 无害地不生效。
        if (hasInputs)
        {
            var lastValues = ScriptInputMemory.Load(scriptId);

            // 入参 = 用户上次值 ← agent 给的覆盖（稀疏叠加）。schema 依合并后的值现算（条件字段随之定），再补默认成全量喂 main。
            var merged = new Map<string, PropertyValue>();
            foreach (var kv in lastValues.Map)
                merged[kv.Key] = kv.Value;
            if (agentInputs != null)
                foreach (var kv in agentInputs.Map)
                    merged[kv.Key] = kv.Value;
            var mergedValues = new PropertyObject(merged);

            var (schema, error) = await Dispatcher.UIThread.InvokeAsync(() =>
                ScriptRunner.GetInputConfig(project, currentPart, quantization, language, selection, pianoSelection, code, mergedValues, cancellationToken));
            if (error != null)
                return string.Format("Error: getInputConfig failed to evaluate for \"{0}\" — {1}", name, error);
            if (schema != null)
                inputs = ScriptConfigs.FillDefaults(schema, mergedValues);
        }

        // 政策：agent 代跑不回写 ScriptInputMemory（用户上次值是用户意图，agent 选择留在其对话历史）。
        return await executor.RunWithAuthorizationAsync(code, inputs, cancellationToken);
    }
}
