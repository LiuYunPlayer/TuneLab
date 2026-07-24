using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.Scripting;

namespace TuneLab.Agent;

// agent 写脚本的【共享执行器】：把「分级授权闸门 + 预览 + 写守卫 wait-retry + 结果回报」收成一处，
// 让 run_script（内联代码）与 run_saved_script（库里命名脚本 + 入参）走同一条写路径（单一动作面 SSOT）。
// 二者只在「代码/入参从哪来」不同；到了这里就是同一件事：以给定 code + inputs 过闸门后落地或呈现。
//
// 分级授权（Settings.AgentAuthorization，见 docs/script-inputs-and-action-surface.md §3）：
//  · Auto           直接提交（原行为）；
//  · ReadOnlyAdvice 跑一遍预览、一律回退、只回报"会改什么"，从不落地；
//  · Confirm        预览 → 宿主内联升级卡片让用户裁决 → 应用本次/始终允许(切自动) 才重跑落地、拒绝则不动。
// confirm 是宿主注入的升级卡片回调（changeCount → 裁决）：宿主把卡片渲进触发这一轮的对话视图。
// 无回调时 Confirm 保守地不落地。"始终允许"的档位切换由宿主在回调内部完成（本执行器只据裁决决定是否落地）。
internal sealed class ScriptWriteExecutor(IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language, Func<ScriptSelection?>? selection, Func<ScriptPianoSelection?>? pianoSelection, Func<AgentAuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm = null)
{
    // 写守卫被拦时（用户正操作）的最长等待与轮询间隔：脚本会原子回退、整段安全重跑，故等用户松手后自动落地。
    const int MaxWaitMs = 3000;
    const int PollMs = 120;

    // 以给定源码 + 入参过授权闸门执行，返回给模型的回报文本。inputs=null 表示无入参（等同空对象）。
    public async Task<string> RunWithAuthorizationAsync(string code, PropertyObject? inputs, CancellationToken cancellationToken)
    {
        // 在 UI 线程跑（数据层改动要求如此）。若写被拦（用户正操作），脚本已原子回退、工程未动，
        // 故等用户松手（Pushable 恢复）后整段重跑——对模型透明；超时才回报。preview=true 时只跑不落地。
        async Task<ScriptRunResult> RunOnUi(bool preview)
        {
            var r = await Dispatcher.UIThread.InvokeAsync(() => ScriptRunner.Run(project, currentPart, quantization, language, selection, pianoSelection, ScriptLimits.Agent, code, cancellationToken, inputs, preview));
            int waited = 0;
            while (r.Blocked && waited < MaxWaitMs && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(PollMs, cancellationToken);
                waited += PollMs;
                r = await Dispatcher.UIThread.InvokeAsync(() => ScriptRunner.Run(project, currentPart, quantization, language, selection, pianoSelection, ScriptLimits.Agent, code, cancellationToken, inputs, preview));
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

            // Confirm：宿主内联升级卡片裁决。
            if (confirm == null)
                return string.Format("Confirmation is required (Confirm mode) but no UI is available to ask, so the {0} edit(s) were NOT applied. Ask the user to apply manually or switch authorization to Auto.", pv.Changes);

            var decision = await confirm(new AgentAuthorizationRequest(AgentWriteKind.ProjectEdit, pv.Changes, null), cancellationToken);
            if (decision == ScriptAuthDecision.Reject)
                return string.Format("The user reviewed the {0} proposed edit(s) and chose NOT to apply them. Nothing was changed.", pv.Changes);

            var applied = Describe(await RunOnUi(preview: false));
            // 始终允许：宿主已把授权切到 Auto——告知模型，后续写将不再逐次询问。
            return decision == ScriptAuthDecision.ApplyAlways
                ? "(The user switched authorization to auto-apply; your later edits will apply without asking.)\n" + applied
                : applied;
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
