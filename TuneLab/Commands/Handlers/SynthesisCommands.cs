using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Data;
using TuneLab.Data.Synthesis;

namespace TuneLab.Commands.Handlers;

// 合成这一族两条：把合成驱动到落定（`project synthesize`）、以及看此刻是什么状态（`project synthesis-status`）。
//
// 【为什么需要「只驱动、不写文件」这一条】脚本面能把合成产物固化成用户数据（part.lockPitch /
// lockAutomation / note.lockPhonemes），却没有任何办法让合成**发生**——那三处的文档都在说
//「false means there was no synthesis output — usually: not synthesized yet」，而在 headless 里
// 那恒为真：那边没有编辑器那个 50ms 定时器，不自己派活就永远没有产物。在这条命令之前，跑批脚本
// 唯一能顶起合成的办法是导一个根本不要的音频文件（export-audio 顺带把合成驱动到落定），荒谬且昂贵。
//
// 【为什么 synthesize 只在 headless 跑】它没有产物。export-audio 在有窗口的进程里之所以成立，是因为
// 它前面挡着模态框（挡住用户中途改工程，否则文件前后不同源），而锁住界面几分钟的正当性来自「换来一个
// 文件」；这一条换不来任何东西，那张卡片没法写得诚实。不挡框则更糟：用户边编辑边合成，派活反复作废，
// 很可能永远不 Done，最后回报 timeout——而真实原因是用户在动工程，这个结果无从解释。何况有编辑器的
// 进程里合成本来就有主人（那个定时器），外部再插一手，「谁负责把它跑完」就没人说得清了。
// 状态查询没有这个问题（纯读、零代价），故那一条两边都给。

internal sealed class ProjectSynthesizeCommand : ICommand
{
    public string Path => "project synthesize";

    // Read：不改工程数据、不写盘、不进撤销历史。它让引擎跑起来、于是内存里多出合成产物——那与
    // 「按一下播放」同级，不是一次编辑。代价（占住这台机器）写在文档第一句，不靠 kind 来表达。
    public CommandKind Kind => CommandKind.Read;

    public string AgentToolName => "run_synthesis";

    public string Brief => "Drive synthesis to completion and wait";

    public string Documentation =>
        "Run the project's synthesis until it settles, and wait for it. This writes NO file and changes nothing in the project — "
        + "it just makes the synthesized output exist, so that a script can read or lock it afterwards (note.lockPhonemes, part.lockPitch, part.lockAutomation "
        + "all no-op on a part that has not been synthesized yet). It is the expensive part of any batch run: it executes every voice and effect in range, which can take minutes. "
        + "\nThis only works in a headless process. In a running TuneLab the editor already drives synthesis itself, and a command that occupies the machine for minutes "
        + "without producing anything has no honest way to ask for that time — so it refuses there and tells you to use --headless. "
        + "\nBy default it covers the whole project. Narrow it with track/part (1-based, as reported by get_project_overview) and/or a startSeconds/endSeconds window; "
        + "a narrower scope is judged complete on its own, regardless of what is still rendering elsewhere. "
        + "\nIf it does not settle in time you are told how much was left, and whatever did finish is still there — call again to keep going. "
        + "\nWhen it does settle, ranges that FAILED (they would be silent in a render) and ranges running DEGRADED (an effect failed, so the unprocessed audio stands in) are reported. "
        + "\nTo render audio to a file use export_audio instead: it drives synthesis exactly like this and then writes the mix.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "track": { "type": "integer", "description": "1-based track number, as reported by get_project_overview. Omit to cover every track." },
            "part": { "type": "integer", "description": "1-based part number within that track. Requires \"track\". Omit to cover every part of it." },
            "startSeconds": { "type": "number", "description": "Start of the time window, in seconds on the project timeline. Comes in a pair with endSeconds; omit both for the whole timeline." },
            "endSeconds": { "type": "number", "description": "End of the time window, in seconds. Must be after startSeconds." },
            "timeoutSeconds": { "type": "integer", "description": "How long to wait before giving up and reporting what is left. Default 300 (5 minutes), allowed 10-3600." }
          },
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        // 判据取「这个进程里有没有编辑器」（EditorStatus 只有有窗口的宿主给得出），而不是某个
        // "我是不是 headless" 的布尔：命令面一贯按能力有无作答，与 project save / open 那两条同一套，
        // 只是方向相反——它们缺了编辑器做不到，这一条有编辑器才不该做。
        if (ctx.EditorStatus is not null)
            return CommandResult.Fail("has_editor",
                "TuneLab is open here, and synthesis in a running editor is that window's own job — it drives it as you work. "
                + "This command exists for unattended runs: start a headless process with --headless --project <file> and call it there.");

        if (ctx.Project is not { } project)
            return CommandResult.Fail("no_project", "no project is open, so there is nothing to synthesize.");

        var (scope, scopeError) = SynthesisScopeSupport.Resolve(project, args);
        if (scopeError is { } bad)
            return CommandResult.Fail(bad.Code, bad.Message);

        int timeoutSeconds = SynthesisWaitSupport.ClampTimeout(args.Json.GetIntOrNull("timeoutSeconds"));

        SynthesisTick tick;
        double waitedSeconds;
        try
        {
            (tick, waitedSeconds) = await SynthesisWaitSupport.DriveAsync(ctx, project, scope, timeoutSeconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return CommandResult.Fail("cancelled",
                "cancelled while waiting for synthesis. Whatever finished before that is still there, and nothing was written to disk either way.");
        }

        var result = new JsonObject
        {
            ["outcome"] = tick.Done ? "done" : "timeout",
            ["scope"] = SynthesisScopeSupport.Describe(args),
            ["seconds"] = timeoutSeconds,
            ["waitedSeconds"] = Math.Round(waitedSeconds, 1),
            ["busy"] = tick.Busy,
            ["pending"] = tick.Pending,
        };

        // 落定之后才问链尾是什么。超时时【不能】问：Inspect 直接扫 Failed 段而不做区间代数，凭的正是
        // 「落定后不会再有 Synthesizing 与之重叠」（见 SynthesisCompletion.Inspect）——半途去问会拿到
        // 说不清的答案。此时能诚实说的只有"还剩多少"。
        if (tick.Done)
        {
            var (facts, parts) = await ctx.OnMainThread(()
                => (SynthesisCompletion.Inspect(project, scope), SynthesisCompletion.Snapshot(project, scope)));

            result["silent"] = facts.Silent.Count > 0 ? SynthesisWaitSupport.Flaws(facts.Silent) : null;
            result["degraded"] = facts.Degraded.Count > 0 ? SynthesisWaitSupport.Flaws(facts.Degraded) : null;

            // 「落定」只说没有在等的活儿了，不说【做成了什么】：范围内一个 part 都没有产出时它同样为真。
            // 那是跑批里最容易被当成成功的一种失败（音源在这个进程里不可用、或那些 part 根本没有音符），
            // 故把"多少个 part 现在有产出"一并报出来——这条命令的全部目的就是让产出存在，那它就该
            // 回答产出在不在，而不只是"我等完了"。
            result["parts"] = parts.Count;
            result["partsWithOutput"] = CountWithOutput(parts);
        }

        return CommandResult.Ok(result);
    }

    // 有产出 = 状态带上报了至少一段。空 part、以及音源不可用的 part，都报不出任何段。
    static int CountWithOutput(IReadOnlyList<SynthesisPartStatus> parts)
    {
        int count = 0;
        foreach (var part in parts)
            if (part.Segments.Count > 0)
                count++;
        return count;
    }

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var scope = obj["scope"]!.GetValue<string>();
        if (obj["outcome"]!.GetValue<string>() == "timeout")
            return string.Format(
                "Synthesis did not settle within {0}s — {1} part(s) still rendering, {2} still waiting ({3}). "
                + "Everything that did finish is still there, so calling this again picks up where it stopped; give it a longer timeoutSeconds if the project is a big one.",
                obj["seconds"]!.GetValue<int>(), obj["busy"]!.GetValue<int>(), obj["pending"]!.GetValue<int>(), scope);

        int parts = obj["parts"]!.GetValue<int>();
        int withOutput = obj["partsWithOutput"]!.GetValue<int>();

        if (parts == 0)
            return string.Format("There is no part with notes in {0}, so there was nothing to synthesize.", scope);

        // 落定但一个 part 都没有产出：说成"合成完了"就是把一次失败报成成功。最常见的原因是音源在这个
        // 进程里不可用（扩展没装、或路由没指过去），所以顺带指路，而不是只丢下一个漂亮的结果。
        if (withOutput == 0)
            return string.Format(
                "Synthesis settled for {0}, but NOT ONE of its {1} part(s) produced anything. That usually means their sound source is unavailable "
                + "in this process (the extension is not installed, or nothing is routed to it — check with list_extensions), or those parts have no notes. "
                + "Nothing is there for a script to read or lock.", scope, parts);

        var text = new StringBuilder();
        text.AppendFormat("Synthesis settled for {0} in {1:0.#}s — {2} of {3} part(s) have output.",
            scope, obj["waitedSeconds"]!.GetValue<double>(), withOutput, parts);

        if (obj["silent"] is { } silent)
            text.AppendFormat("\nWARNING — these ranges failed to synthesize and would be SILENT in a render:{0}", SynthesisWaitSupport.DescribeFlaws(silent));
        if (obj["degraded"] is { } degraded)
            text.AppendFormat("\nNote — an effect failed on these ranges, so the unprocessed audio stands in there rather than the intended sound:{0}", SynthesisWaitSupport.DescribeFlaws(degraded));

        return text.ToString();
    }
}

internal sealed class ProjectSynthesisStatusCommand : ICommand
{
    public string Path => "project synthesis-status";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "get_synthesis_status";

    public string Brief => "Report what is synthesized and what is not";

    public string Documentation =>
        "Report the synthesis state of the project right now: how many parts are rendering or still waiting, and the status bar's own segments per part "
        + "(pending / synthesizing / claimed / interim / final / degraded / failed, with progress and any message). This is the same data the status strip draws, so it matches what the user sees. "
        + "\nIt only LOOKS — it never starts any work. In a headless process nothing drives synthesis on its own, so everything reads as pending until you call run_synthesis; that is the truth there, not a fault. "
        + "\nNarrow it the same way as run_synthesis: track/part (1-based, as reported by get_project_overview) and/or a startSeconds/endSeconds window. Segments are reported as the engine stated them, never clipped to your window.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "track": { "type": "integer", "description": "1-based track number, as reported by get_project_overview. Omit to cover every track." },
            "part": { "type": "integer", "description": "1-based part number within that track. Requires \"track\". Omit to cover every part of it." },
            "startSeconds": { "type": "number", "description": "Start of the time window, in seconds on the project timeline. Comes in a pair with endSeconds; omit both for the whole timeline." },
            "endSeconds": { "type": "number", "description": "End of the time window, in seconds. Must be after startSeconds." }
          },
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        if (ctx.Project is not { } project)
            return CommandResult.Fail("no_project", "no project is open, so there is no synthesis to report on.");

        var (scope, scopeError) = SynthesisScopeSupport.Resolve(project, args);
        if (scopeError is { } bad)
            return CommandResult.Fail(bad.Code, bad.Message);

        var (tick, parts) = await ctx.OnMainThread(() =>
            (SynthesisCompletion.Survey(project, scope), SynthesisCompletion.Snapshot(project, scope)));

        var partsJson = new JsonArray();
        foreach (var part in parts)
        {
            var segments = new JsonArray();
            foreach (var segment in part.Segments)
                segments.Add(new JsonObject
                {
                    ["state"] = segment.State.ToString().ToLowerInvariant(),
                    ["startSeconds"] = Math.Round(segment.StartTime, 3),
                    ["endSeconds"] = Math.Round(segment.EndTime, 3),
                    ["progress"] = segment.Progress > 0 ? Math.Round(segment.Progress, 3) : null,
                    ["message"] = string.IsNullOrEmpty(segment.Message) ? null : segment.Message,
                });

            partsJson.Add(new JsonObject
            {
                ["where"] = part.Where,
                ["synthesizable"] = part.HasPipeline,
                ["segments"] = segments,
            });
        }

        return CommandResult.Ok(new JsonObject
        {
            ["scope"] = SynthesisScopeSupport.Describe(args),
            ["done"] = tick.Done,
            ["busy"] = tick.Busy,
            ["pending"] = tick.Pending,
            ["parts"] = partsJson,
        });
    }

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var text = new StringBuilder();
        var scope = obj["scope"]!.GetValue<string>();
        if (obj["done"]!.GetValue<bool>())
            text.AppendFormat("Synthesis is settled for {0}.", scope);
        else
            text.AppendFormat("Synthesis for {0}: {1} part(s) rendering, {2} still waiting.",
                scope, obj["busy"]!.GetValue<int>(), obj["pending"]!.GetValue<int>());

        if (obj["parts"] is not JsonArray parts || parts.Count == 0)
        {
            text.Append("\nThere is no part with a sound source in range, so there is nothing to synthesize.");
            return text.ToString();
        }

        foreach (var item in parts)
        {
            if (item is not JsonObject part)
                continue;

            text.AppendFormat("\n{0}:", part["where"]!.GetValue<string>());
            if (!part["synthesizable"]!.GetValue<bool>())
            {
                text.Append(" no working sound source — nothing here will be synthesized");
                continue;
            }

            if (part["segments"] is not JsonArray segments || segments.Count == 0)
            {
                text.Append(" nothing reported yet");
                continue;
            }

            foreach (var entry in segments)
            {
                if (entry is not JsonObject segment)
                    continue;

                text.AppendFormat("\n  · {0:0.###}s–{1:0.###}s {2}",
                    segment["startSeconds"]!.GetValue<double>(), segment["endSeconds"]!.GetValue<double>(), segment["state"]!.GetValue<string>());
                if (segment["progress"] is { } progress)
                    text.AppendFormat(" {0:0}%", progress.GetValue<double>() * 100);
                if (segment["message"]?.GetValue<string>() is { Length: > 0 } message)
                    text.Append(" — ").Append(message.Replace("\n", " "));
            }
        }

        return text.ToString();
    }
}

// 两条命令共用的范围参数：解析成 SynthesisScope，以及把它说成一句人话。
internal static class SynthesisScopeSupport
{
    public static (SynthesisScope Scope, CommandError? Error) Resolve(IProject project, CommandArgs args)
    {
        int? trackNumber = args.Json.GetIntOrNull("track");
        int? partNumber = args.Json.GetIntOrNull("part");
        double? start = args.Json.GetDoubleOrNull("startSeconds");
        double? end = args.Json.GetDoubleOrNull("endSeconds");

        if (partNumber is not null && trackNumber is null)
            return (default, new CommandError("part_needs_track",
                "part numbers are per track, so \"part\" needs \"track\" as well — part 1 of which track?"));

        // 成对或都不给，与脚本面的范围参数（part.lockPitch(start, end) 等）同一条纪律：单给一头要么
        // 意思含混，要么得替调用方猜另一头。
        if (start is null != end is null)
            return (default, new CommandError("half_a_window",
                "\"startSeconds\" and \"endSeconds\" come in pairs: pass both to narrow the time window, or neither for the whole timeline."));

        if (start is { } s && end is { } e && e <= s)
            return (default, new CommandError("empty_window",
                string.Format("that time window is empty: endSeconds ({0:0.###}) is not after startSeconds ({1:0.###}).", e, s)));

        ITrack? track = null;
        IPart? part = null;

        if (trackNumber is { } t)
        {
            var tracks = project.Tracks;
            if (t < 1 || t > tracks.Count)
                return (default, new CommandError("no_such_track",
                    string.Format("there is no track {0} — the project has {1}. Call get_project_overview to see them.", t, tracks.Count)));

            track = tracks[t - 1];

            if (partNumber is { } p)
            {
                var parts = new List<IPart>(track.Parts);
                if (p < 1 || p > parts.Count)
                    return (default, new CommandError("no_such_part",
                        string.Format("track {0} has no part {1} — it has {2}. Call get_project_overview to see them.", t, p, parts.Count)));

                part = parts[p - 1];
                if (part is not IMidiPart)
                    return (default, new CommandError("not_a_midi_part", string.Format(
                        "part {0} of track {1} is an audio part: it holds recorded audio rather than notes, so there is nothing to synthesize there.", p, t)));
            }
        }

        return (new SynthesisScope(track, part, start ?? double.MinValue, end ?? double.MaxValue), null);
    }

    // 回报里那句范围描述。从【参数】生成而非从 scope 反推：用户说的是"track 2 part 1"，回报里就该是
    // 那几个字，而不是对象身份重新编出来的号。
    public static string Describe(CommandArgs args)
    {
        int? track = args.Json.GetIntOrNull("track");
        int? part = args.Json.GetIntOrNull("part");
        double? start = args.Json.GetDoubleOrNull("startSeconds");
        double? end = args.Json.GetDoubleOrNull("endSeconds");

        var text = new StringBuilder();
        if (track is { } t && part is { } p)
            text.AppendFormat("track {0} part {1}", t, p);
        else if (track is { } only)
            text.AppendFormat("track {0}", only);
        else
            text.Append("the whole project");

        if (start is { } s && end is { } e)
            text.AppendFormat(", {0:0.###}s–{1:0.###}s", s, e);

        return text.ToString();
    }
}
