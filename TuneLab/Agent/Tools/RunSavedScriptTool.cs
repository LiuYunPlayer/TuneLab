using System;
using System.Globalization;
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

namespace TuneLab.Agent;

// E1「全能 agent 闭环」的两件工具：读某命名脚本的入参 schema/上次值（只读）+ 按名跑它（写，可省入参）。
// 与 save/list/read/delete_script 一起构成脚本库闭环——agent 帮用户写好工具脚本存库后，日后能自己读参数、代跑一次，
// 无需重写代码。写路径与 run_script 共用 ScriptWriteExecutor（同一授权闸门 / 收口）。

// get_script_inputs：返回某脚本的入参 schema（名/类型/默认/范围·选项）+ 用户上次输入值。只读，不跑脚本动作
// （只 eval 顶层调 getInputConfig，约定无副作用、误改原子回退）。让 agent 在 run_saved_script 前知道要填哪些参数。
internal sealed class GetScriptInputsTool(IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language, Func<ScriptSelection?>? selection, Func<ScriptPianoSelection?>? pianoSelection) : IAgentTool
{
    public string Name => "get_script_inputs";

    public string Description =>
        "Return the input schema of a saved script (each field's name, type, default, range/options) plus the user's LAST entered values. " +
        "Call this before run_saved_script when list_scripts marks a script as taking inputs, so you know what to pass. " +
        "A script that takes no inputs reports so — just run it with run_saved_script and no inputs.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": { "name": { "type": "string", "description": "Library name of the script (without .js)." } },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string name;
        try { using var doc = JsonDocument.Parse(argumentsJson); name = doc.RootElement.GetString("name"); }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        name = ScriptLibrary.SanitizeName((name ?? "").Trim());
        if (string.IsNullOrWhiteSpace(name) || !ScriptLibrary.Exists(name))
            return "Error: no script named \"" + name + "\". Call list_scripts to see available names.";

        string code;
        try { code = ScriptLibrary.Read(name); }
        catch (Exception ex) { return "Error: " + ex.Message; }

        var (scriptId, hasInputs) = SavedScriptSupport.Inspect(name, code, project, currentPart, quantization, language);
        if (!hasInputs)
            return string.Format("Script \"{0}\" takes no inputs. Run it with run_saved_script and no `inputs`.", name);

        var lastValues = ScriptInputMemory.Load(scriptId);

        // getInputConfig 读工程上下文（选中音符等），在 UI 线程求值；误改在 GetInputConfig 内原子回退。
        // DescribeSchema 也在 UI 线程内完成——自定义 scale/format 的 config 会回调 Jint 引擎（.Scale.ToValue / .Format），
        // 而 Jint 引擎非线程安全、须在其创建线程（UI）调用；built-in config 纯 C# 无此约束，一并放里无碍。
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (schema, error) = ScriptRunner.GetInputConfig(project, currentPart, quantization, language, selection, pianoSelection, code, lastValues, cancellationToken);
            if (error != null)
                return string.Format("Error: getInputConfig failed to evaluate for \"{0}\" — {1}", name, error);
            if (schema == null)
                return string.Format("Script \"{0}\" takes no inputs. Run it with run_saved_script and no `inputs`.", name);
            return SavedScriptSupport.DescribeSchema(name, schema, lastValues);
        });
    }
}

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
            return "Error: no script named \"" + name + "\". Call list_scripts to see available names.";

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

// run_saved_script / get_script_inputs 共用的小助手：稳定 id 解析 + 入参 schema 文本化。
internal static class SavedScriptSupport
{
    // 一次 eval 取回脚本身份：稳定 id（= 入参记忆键，与快捷键锚点同一套；声明 id 合法用之否则文件名）+ 是否带入参
    // （定义了 getInputConfig）。非工具脚本（无 getScriptInfo）→ id=文件名、hasInputs=false（其入参运行期被忽略）。
    public static (string ScriptId, bool HasInputs) Inspect(string name, string code, IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language)
    {
        var (info, _) = ScriptTools.InspectSource(name, code, project, currentPart, quantization, language);
        return info != null ? (ScriptTools.StableId(info), info.HasInputs) : (name, false);
    }

    // 入参 schema + 上次值 → 给模型的可读文本。逐字段列：名(+标签)、类型/范围/选项、默认、上次用值。
    public static string DescribeSchema(string name, ObjectConfig schema, PropertyObject lastValues)
    {
        var sb = new StringBuilder();
        int count = schema.Properties.Count;
        sb.Append(string.Format("Inputs for script \"{0}\" ({1} field(s)). Pass any subset as `inputs` to run_saved_script; ", name, count));
        sb.Append("omitted fields fall back to the last value shown (else the default). Values you pass are not saved as the user's last values.");
        foreach (var kvp in schema.Properties)
        {
            var key = kvp.Key;
            sb.Append("\n- ").Append(key.Id);
            if (!string.IsNullOrEmpty(key.DisplayText) && key.DisplayText != key.Id)
                sb.Append(" (\"").Append(key.DisplayText).Append("\")");
            sb.Append(": ").Append(DescribeConfig(kvp.Value));

            if (kvp.Value is IValueConfig leaf)
                sb.Append(". default ").Append(FormatValue(leaf.DefaultValue));
            if (lastValues.Map.TryGetValue(key.Id, out var last) && !last.IsNull())
                sb.Append(". last used: ").Append(FormatValue(last));
        }
        return sb.ToString();
    }

    static string DescribeConfig(IControllerConfig config) => config switch
    {
        SliderConfig s => string.Format("number in [{0}, {1}]", FormatNum(s.Scale.ToValue(0)), FormatNum(s.Scale.ToValue(1))),
        DraggableNumberBoxConfig d => "number" + RangeHint(d),
        ComboBoxConfig c => "one of " + Options(c),
        CheckBoxConfig => "boolean (true/false)",
        TextBoxConfig t => t.IsPassword ? "text (masked)" : "text",
        ObjectConfig => "object (grouped fields)",
        _ => "value",
    };

    static string RangeHint(DraggableNumberBoxConfig d)
    {
        var parts = new StringBuilder();
        if (d.Min is { } min) parts.Append(", min ").Append(FormatNum(min));
        if (d.Max is { } max) parts.Append(", max ").Append(FormatNum(max));
        if (d.Step is { } step) parts.Append(", step ").Append(FormatNum(step));
        return parts.ToString();
    }

    static string Options(ComboBoxConfig c)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        foreach (var item in c.Items)
        {
            if (item.SubItems != null || item.Value.IsNull())
                continue;   // 跳过分组标题 / 分隔线（值为空）
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(FormatValue(item.Value));
            if (!string.IsNullOrEmpty(item.DisplayText) && item.DisplayText != FormatValue(item.Value))
                sb.Append(" (\"").Append(item.DisplayText).Append("\")");
        }
        return sb.Append(']').ToString();
    }

    static string FormatValue(PropertyValue v)
    {
        if (v.IsNull()) return "(none)";
        if (v.ToBoolean(out var b)) return b ? "true" : "false";
        if (v.ToDouble(out var d)) return FormatNum(d);
        if (v.ToString(out var s)) return "\"" + s + "\"";
        return "(none)";
    }

    static string FormatNum(double d)
        => d == Math.Floor(d) && !double.IsInfinity(d)
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString(CultureInfo.InvariantCulture);
}
