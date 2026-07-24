using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.I18N;
using TuneLab.Scripting;
using TuneLab.Utils;

namespace TuneLab.Agent;

// 逃生口工具：让模型写一段 JavaScript 表达复杂/批量/带循环条件的工程编辑（音乐编辑高度契合，
// 如"5-8 小节每音符升八度再加三度和声"=一个循环，一轮搞定、省下几十次 tool 往返）。
//
// 工具本身很薄：解析 code 字符串，交给【独立的脚本模块】(TuneLab.Scripting) 执行，把日志/结果/错误回灌。
// 脚本引擎、动作面 API、沙箱、整段=一次 Commit 的收口都在脚本模块里，不在 agent 层——agent 只是它的一个消费者。
//
// 分级授权（Settings.AgentAuthorization，见 docs §3）：本工具是 agent 的写口，故授权闸门作用于此——
//  · Auto         直接提交（原行为）；
//  · ReadOnlyAdvice 跑一遍预览、一律回退、只回报"会改什么"，从不落地；
//  · Confirm      预览 → 宿主模态让用户确认 → 确认才重跑落地、取消则不动。
// owner 提供确认模态的宿主窗（宿主注入 () => 侧栏根）；无窗时 Confirm 保守地不落地。
internal sealed class RunScriptTool(IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language, Func<ScriptSelection?>? selection, Func<ScriptPianoSelection?>? pianoSelection, Func<Avalonia.Visual?>? owner = null) : IAgentTool
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

    // 写守卫被拦时（用户正操作）的最长等待与轮询间隔：脚本会原子回退、整段安全重跑，故等用户松手后自动落地。
    const int MaxWaitMs = 3000;
    const int PollMs = 120;

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

        // 在 UI 线程跑（数据层改动要求如此）。若写被拦（用户正操作），脚本已原子回退、工程未动，
        // 故等用户松手（Pushable 恢复）后整段重跑——对模型透明；超时才回报。preview=true 时只跑不落地。
        async Task<ScriptRunResult> RunOnUi(bool preview)
        {
            var r = await Dispatcher.UIThread.InvokeAsync(() => ScriptRunner.Run(project, currentPart, quantization, language, selection, pianoSelection, ScriptLimits.Agent, code, cancellationToken, inputs: null, preview: preview));
            int waited = 0;
            while (r.Blocked && waited < MaxWaitMs && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(PollMs, cancellationToken);
                waited += PollMs;
                r = await Dispatcher.UIThread.InvokeAsync(() => ScriptRunner.Run(project, currentPart, quantization, language, selection, pianoSelection, ScriptLimits.Agent, code, cancellationToken, inputs: null, preview: preview));
            }
            return r;
        }

        var level = AgentAuthorizationExtensions.ParseOrDefault(Settings.AgentAuthorization.Value);

        try
        {
            if (level == AgentAuthorization.Auto)
                return Describe(await RunOnUi(preview: false));

            // ReadOnlyAdvice / Confirm：先预览（跑一遍、干净回退、报会改动数）。
            var pv = await RunOnUi(preview: true);
            if (!pv.Ok)
                return Describe(pv);   // 出错 / 仍被拦：如实回报（已回退）
            if (pv.Changes == 0)
                return WithOutput("Script ran OK. No changes were produced.", pv);

            if (level == AgentAuthorization.ReadOnlyAdvice)
                return WithOutput(string.Format(
                    "Authorization is READ-ONLY (advice mode): the script ran and WOULD apply {0} edit(s), but NOTHING was changed. " +
                    "Explain the plan to the user; to actually apply it, ask them to set agent authorization to Confirm or Auto, or run the script manually.", pv.Changes), pv);

            // Confirm：宿主模态确认。
            var visual = owner?.Invoke();
            if (visual == null)
                return string.Format("Confirmation is required (Confirm mode) but no window is available to show it, so the {0} edit(s) were NOT applied. Ask the user to apply manually or switch authorization to Auto.", pv.Changes);

            bool confirmed = await Dispatcher.UIThread.InvokeAsync(() =>
                visual.ShowConfirm(
                    "Agent".Tr(TC.Dialog),
                    string.Format("Apply the agent's {0} change(s) to the project?".Tr(TC.Dialog), pv.Changes),
                    "Apply".Tr(TC.Dialog),
                    "Cancel".Tr(TC.Dialog)));
            if (!confirmed)
                return string.Format("The user reviewed the {0} proposed edit(s) and chose NOT to apply them. Nothing was changed.", pv.Changes);

            return Describe(await RunOnUi(preview: false));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return "Error: " + ex.Message; }
    }

    // 终态结果 → 回报模型的文本（Auto 与确认后落地共用）。
    static string Describe(ScriptRunResult result)
    {
        if (result.Blocked)
            return "The user is editing the project right now, so the script did not run and nothing was changed. Wait a moment and try again, or ask the user to finish their current edit.";

        var sb = new StringBuilder();
        if (result.Ok)
            sb.Append(result.Committed
                ? string.Format("Script ran OK. Applied {0} edit(s) as one undoable change.", result.Changes)
                : "Script ran OK. No changes were made.");
        else
            sb.Append("Script error: ").Append(result.Error)
              .Append("\n(All changes were rolled back; the project is unchanged. Fix the script and re-run — do not patch from current state.)");
        if (!string.IsNullOrEmpty(result.Output))
            sb.Append("\n--- output ---\n").Append(result.Output.TrimEnd('\n'));
        if (result.Ok && !string.IsNullOrEmpty(result.ResultText))
            sb.Append("\n--- result ---\n").Append(result.ResultText);
        return sb.ToString();
    }

    static string WithOutput(string message, ScriptRunResult result)
        => string.IsNullOrEmpty(result.Output) ? message : message + "\n--- output ---\n" + result.Output.TrimEnd('\n');
}
