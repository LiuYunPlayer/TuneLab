using System;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.Scripting;

namespace TuneLab.Commands;

// 写脚本的【共享执行器】：把「分级授权 + 预览 + 写守卫 wait-retry + 结果事实」收成一处，让 `script run`
// （内联代码）与 `script run-saved`（库里命名脚本 + 入参）走同一条写路径（单一动作面 SSOT）。
// 二者只在「代码/入参从哪来」不同；到了这里就是同一件事：以给定 code + inputs 过闸门后落地或呈现。
//
// 【预览-裁决，而非一问一答】工程编辑事先不知道会改多少，故先跑一遍预览（改完原子回退）拿到改动数，
// 再据档位决定落地/只读呈现/问用户。这是 IAuthorizationPolicy 只给三个原语（档位/能不能问/问一次）
// 而不是只给 AuthorizeAsync 的原因——一问一答那类写用默认实现，这一类在这里自己编排。
//  · Auto           直接落地（不必预览）；
//  · ReadOnlyAdvice 跑一遍预览、一律回退、只回报"会改什么"，从不落地；
//  · Confirm        预览 → 问用户 → 应用本次/始终允许 才重跑落地、拒绝则不动。
internal static class ScriptWriteExecutor
{
    // 写守卫被拦时（用户正操作）的最长等待与轮询间隔：脚本会原子回退、整段安全重跑，故等用户松手后自动落地。
    const int MaxWaitMs = 3000;
    const int PollMs = 120;

    // 以给定源码 + 入参过授权闸门执行。inputs=null 表示无入参（等同空对象）。
    public static async Task<CommandResult> RunAsync(CommandContext ctx, IProject project, string code, PropertyObject? inputs, CancellationToken cancellationToken)
    {
        // 没有授权策略的入口（headless/CI 忘了配）：连预览都不跑——预览虽会回退，跑的仍是会改工程的用户代码。
        if (ctx.Authorization is not { } policy)
            return CommandResult.Ok(new JsonObject { ["outcome"] = "no_policy" });

        // 在主线程跑（数据层改动要求如此）。若写被拦（用户正操作），脚本已原子回退、工程未动，
        // 故等用户松手（Pushable 恢复）后整段重跑——对调用方透明；超时才回报。preview=true 时只跑不落地。
        async Task<ScriptRunResult> Run(bool preview)
        {
            var r = await ctx.OnMainThread(() => ScriptRunner.Run(project, ctx.CurrentPart(), ctx.Quantization(), ctx.Language,
                ctx.Selection(), ctx.PianoSelection(), ScriptLimits.Agent, code, cancellationToken, inputs, preview, ctx.SelectionWriter()));
            int waited = 0;
            while (r.Blocked && waited < MaxWaitMs && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(PollMs, cancellationToken);
                waited += PollMs;
                r = await ctx.OnMainThread(() => ScriptRunner.Run(project, ctx.CurrentPart(), ctx.Quantization(), ctx.Language,
                    ctx.Selection(), ctx.PianoSelection(), ScriptLimits.Agent, code, cancellationToken, inputs, preview, ctx.SelectionWriter()));
            }
            return r;
        }

        try
        {
            if (policy.Mode == AuthorizationMode.Auto)
                return CommandResult.Ok(Outcome(await Run(preview: false), null));

            // 只读建议 / 需确认：先预览（跑一遍、干净回退、报会改动数）。
            var preview = await Run(preview: true);
            if (!preview.Ok)
                return CommandResult.Ok(Outcome(preview, null));   // 出错 / 仍被拦：如实回报（已回退）
            if (preview.Changes == 0)
                return CommandResult.Ok(new JsonObject
                {
                    ["outcome"] = "no_changes",
                    ["previewed"] = true,
                    ["changes"] = 0,
                    ["output"] = Text(preview.Output),
                });

            if (policy.Mode == AuthorizationMode.ReadOnlyAdvice)
                return CommandResult.Ok(new JsonObject
                {
                    ["outcome"] = "advice",
                    ["changes"] = preview.Changes,
                    ["output"] = Text(preview.Output),
                });

            if (!policy.CanAsk)
                return CommandResult.Ok(new JsonObject { ["outcome"] = "cannot_ask", ["changes"] = preview.Changes });

            var decision = await policy.AskAsync(new AuthorizationRequest(WriteKind.ProjectEdit, preview.Changes, null), cancellationToken);
            if (decision == AuthorizationDecision.Reject)
                return CommandResult.Ok(new JsonObject { ["outcome"] = "rejected", ["changes"] = preview.Changes });

            // 两种同意都要如实点明"用户被问过并同意了"——否则 ApplyOnce 的回报与 Auto 档一字不差，调用方无从
            // 知道确认发生过、也判不出当前档位（实测表现为模型反复追问"是不是弹了卡片"）。
            return CommandResult.Ok(Outcome(await Run(preview: false), decision == AuthorizationDecision.ApplyAlways
                ? "(The user was asked to confirm, approved it, and switched authorization to auto-apply; your later edits will apply without asking.)\n"
                : "(The user was asked to confirm and approved these edits; authorization stays at Confirm, so your next edit will ask again.)\n"));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CommandResult.Fail("run_failed", ex.Message); }
    }

    // 一次真跑（或失败的预览）的事实。note = 确认前缀（没有则 null）。
    static JsonNode Outcome(ScriptRunResult result, string? note)
    {
        if (result.Blocked)
            return new JsonObject { ["outcome"] = "blocked" };

        var data = new JsonObject
        {
            ["outcome"] = !result.Ok ? "error" : result.Committed ? "applied" : "no_changes",
            ["previewed"] = false,
            ["changes"] = result.Changes,
            ["error"] = result.Ok ? null : result.Error,
            ["output"] = Text(result.Output),
            // 脚本的返回值只在成功时有意义（出错那次的返回值是半截状态的产物）。
            ["resultText"] = result.Ok ? Text(result.ResultText) : null,
            ["note"] = note,
        };
        return data;
    }

    static JsonNode? Text(string? s) => string.IsNullOrEmpty(s) ? null : s.TrimEnd('\n');

    // 两条 run 命令共用的渲染（措辞与搬家前逐字一致）。
    public static string Render(JsonNode? data)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        int changes = obj["changes"]?.GetValue<int>() ?? 0;
        var sb = new StringBuilder(obj["note"]?.GetValue<string>() ?? string.Empty);
        switch (obj["outcome"]!.GetValue<string>())
        {
            case "no_policy":
                return "This entry point has no authorization policy configured, so nothing that writes is allowed here and the script was NOT run. "
                     + "Whoever runs it must grant write authorization explicitly (there is no silent default).";

            case "blocked":
                return "The user is editing the project right now, so the script did not run and nothing was changed. "
                     + "Wait a moment and try again, or ask the user to finish their current edit.";

            case "cannot_ask":
                return string.Format("Confirmation is required (Confirm mode) but no UI is available to ask, so the {0} edit(s) were NOT applied. "
                    + "Ask the user to apply manually or switch authorization to Auto.", changes);

            case "rejected":
                return string.Format("The user reviewed the {0} proposed edit(s) and chose NOT to apply them. Nothing was changed.", changes);

            case "advice":
                sb.Append(string.Format(
                    "Authorization is READ-ONLY (advice mode): the script ran and WOULD apply {0} edit(s), but NOTHING was changed. " +
                    "Explain the plan to the user; to actually apply it, ask them to set agent authorization to Confirm or Auto, or run the script manually.", changes));
                break;

            case "applied":
                sb.Append(string.Format("Script ran OK. Applied {0} edit(s) as one undoable change.", changes));
                break;

            case "error":
                sb.Append("Script error: ").Append(obj["error"]?.GetValue<string>())
                  .Append("\n(All changes were rolled back; the project is unchanged. Fix the script and re-run — do not patch from current state.)");
                break;

            default:   // no_changes——预览与真跑的措辞不同（一个是"不会产生"，一个是"没有做"）。
                sb.Append(obj["previewed"]!.GetValue<bool>()
                    ? "Script ran OK. No changes were produced."
                    : "Script ran OK. No changes were made.");
                break;
        }

        if (obj["output"]?.GetValue<string>() is { } output)
            sb.Append("\n--- output ---\n").Append(output);
        if (obj["resultText"]?.GetValue<string>() is { } resultText)
            sb.Append("\n--- result ---\n").Append(resultText);
        return sb.ToString();
    }
}
