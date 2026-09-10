using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using TuneLab.Data;
using TuneLab.Input;

namespace TuneLab.Commands;

// 编辑器此刻状态的【取事实 + 渲染】。`editor status` 与 `action run` 的回报共用这一份：触发完一条即发即忘
// 的动作（播放、切工具）之后调用方要的答案，与它单独问一次状态时该得到的答案是同一个，故字段与措辞只在
// 这里定义一次（同 KeybindingText 之于两条 keybinding 命令）。
internal static class EditorStatusText
{
    // 没有编辑器时的那句话。三条命令（action list / action run / editor status）共用，故只有一处定义。
    // 【要说清是"这里永远没有"而不是"暂时没有"】动作由编辑器窗口在建界面时注册，headless 里等多久都不会有；
    // 把它说成"暂时"会让调用方一直重试。
    public const string NoEditor =
        "No editor is present in this process. Actions are the things a person can do in TuneLab's editor window — they are declared by that window as it builds, so a process without one has none of them and no editor state to report. "
        + "Run this against a running TuneLab (attach, i.e. without --headless). Project data itself does not need an editor: the project and script commands work either way.";

    // hasEditor=false（headless / 桥未连）时只回那一个字段：其余全是"有编辑器才有"的事实，编出来就是撒谎。
    public static JsonObject Build(CommandContext ctx)
    {
        if (ctx.EditorStatus is not { } status)
            return new JsonObject { ["hasEditor"] = false };

        return new JsonObject
        {
            ["hasEditor"] = true,
            ["playing"] = status.IsPlaying,
            ["playheadTime"] = status.PlayheadTime,
            ["playheadTick"] = status.PlayheadTick,
            ["endTime"] = status.EndTime,
            ["tool"] = new JsonObject
            {
                ["id"] = status.CurrentToolActionId,
                ["label"] = ActionRegistry.LabelOf(status.CurrentToolActionId),
            },
            ["parameterPanelVisible"] = status.IsParameterPanelVisible,
            ["waveformVisible"] = status.IsWaveformVisible,
            // 侧栏：null = 没开；开着时同时给 id 与显示名（同 tool）——渲染可能发生在命令桥另一端，
            // 那里没有注册表可查。
            ["sidebar"] = status.SidebarPanelActionId is not { } sidebar ? null : new JsonObject
            {
                ["id"] = sidebar,
                ["label"] = ActionRegistry.LabelOf(sidebar),
            },
            ["focusedSurface"] = status.FocusedSurface,
            // 挡在界面前面的东西（模态框 / 系统文件选择器）。空数组 = 没有——不省成 null：
            // "问过了，没有"与"这版本不报这一条"对调用方是两件事。
            ["blockingDialogs"] = new JsonArray(status.BlockingDialogs.Select(d => (JsonNode)d!).ToArray()),
            // 当前 part 与量化取自 EditorState（脚本面读的也是这两个访问器），故 status、脚本、agent 说的
            // 是同一个"当前"。
            ["currentPart"] = CurrentPart(ctx),
            // 量化同时报【动作 id】（同当前工具与侧栏）：调用方因此能把它与 `action list` 里那 18 档直接对上。
            // 分母 = 基数×细分（3×4 = 1/12），与工具栏下拉上写的完全一致——报成 "1/4 三连" 会让外部对不上号。
            ["quantization"] = ctx.EditorState?.Quantization is not { } q ? null : new JsonObject
            {
                ["division"] = (int)q.Division,
                ["base"] = (int)q.Base,
                ["label"] = "1/" + (int)q.Base * (int)q.Division,
                ["actionId"] = "quantization.1_" + (int)q.Base * (int)q.Division,
            },
        };
    }

    // 当前 part 的可定位身份：轨号 / part 号都是 1-based，与 `project status` 的 "number" 同口径
    // （换口径会让两条命令报的"第 2 轨"指不同的轨）。
    static JsonNode? CurrentPart(CommandContext ctx)
    {
        if (ctx.EditorState?.CurrentPart is not { } part)
            return null;

        var node = new JsonObject { ["name"] = part.Name.Value };
        if (ctx.Project is { } project)
        {
            var tracks = project.Tracks;
            for (int t = 0; t < tracks.Count; t++)
            {
                var parts = tracks[t].Parts.ToList();
                int p = parts.IndexOf(part);
                if (p < 0)
                    continue;
                node["trackNumber"] = t + 1;
                node["trackName"] = tracks[t].Name.Value;
                node["partNumber"] = p + 1;
                break;
            }
        }
        return node;
    }

    // 状态那几行。前缀让调用方分得清这是"触发之后的状态"还是"单独问的状态"——两处的正文一模一样。
    public static void Append(StringBuilder sb, JsonObject data)
    {
        if (data["hasEditor"]?.GetValue<bool>() != true)
        {
            sb.Append(NoEditor);
            return;
        }

        // 【放在最前面】它决定"此刻做的任何事用户看不看得见"。排在工具、面板那些后面就等于藏起来，
        // 而这一条恰恰是读的人最该先知道的。
        if (data["blockingDialogs"] is JsonArray blocking && blocking.Count > 0)
        {
            sb.Append("BLOCKED: the user's screen has ")
              .Append(string.Join(" and ", blocking.Select(d => "\"" + d!.GetValue<string>() + "\"")))
              .Append(" in front of the editor, waiting to be answered. Commands still run and still report success, but the user cannot see or touch the editor until they answer it, ")
              .Append("so anything you do now — including moving their view — happens behind that. Only a person can dismiss it.\n");
        }

        bool playing = data["playing"]!.GetValue<bool>();
        sb.Append(playing ? "Playing" : "Stopped").Append(", playhead at ").Append(Time(Number(data["playheadTime"]) ?? 0));
        if (Number(data["playheadTick"]) is { } tick)
            sb.Append(string.Format(CultureInfo.InvariantCulture, " (tick {0:0})", tick));
        sb.Append(" of ").Append(Time(Number(data["endTime"]) ?? 0)).Append('.');

        var tool = data["tool"]!.AsObject();
        sb.Append("\nTool: \"").Append(tool["label"]!.GetValue<string>()).Append("\" (").Append(tool["id"]!.GetValue<string>()).Append(')');
        sb.Append(". Parameter panel: ").Append(data["parameterPanelVisible"]!.GetValue<bool>() ? "open" : "closed");
        sb.Append(", waveform lane: ").Append(data["waveformVisible"]?.GetValue<bool>() == true ? "shown" : "hidden").Append('.');
        // 侧栏单独一句：它是一个单槽（一次只开一个面），故报“开的是哪个”而不是逐面的开关。
        sb.Append("\nSide panel: ");
        if (data["sidebar"] is JsonObject sidebar)
            sb.Append('"').Append(sidebar["label"]!.GetValue<string>()).Append("\" (").Append(sidebar["id"]!.GetValue<string>()).Append(')');
        else
            sb.Append("hidden");
        sb.Append('.');

        // 焦点面单独说一句并给出后果：它决定剪贴板类动作作用在哪儿、以及它们此刻能不能用。
        sb.Append("\nKeyboard focus: ").Append(data["focusedSurface"]?.GetValue<string>() switch
        {
            "arrangement" => "the arrangement, so copy/cut/paste/delete/select-all act on the parts selected there",
            "pianoRoll" => "the piano roll, so copy/cut/paste/delete/select-all act on what is selected there",
            _ => "neither edit surface (TuneLab is probably not the foreground window), so copy/cut/paste/delete/select-all have nothing to act on until the user clicks into the arrangement or the piano roll",
        }).Append('.');

        if (data["currentPart"] is JsonObject part)
        {
            sb.Append("\nPart open in the piano roll: \"").Append(part["name"]!.GetValue<string>()).Append('"');
            if (part["trackNumber"] is { } trackNumber)
                sb.Append(string.Format(" (part {0} of track {1} \"{2}\")",
                    part["partNumber"]!.GetValue<int>(), trackNumber.GetValue<int>(), part["trackName"]!.GetValue<string>()));
            sb.Append('.');
        }
        else
        {
            sb.Append("\nNo part is open in the piano roll.");
        }

        if (data["quantization"] is JsonObject q)
        {
            sb.Append(" Quantization (the snap grid): ").Append(q["label"]?.GetValue<string>() ?? ("1/" + q["division"]!.GetValue<int>()));
            int b = q["base"]!.GetValue<int>();
            sb.Append(b switch { 3 => " (triplets)", 5 => " (quintuplets)", _ => "" });
            if (q["actionId"]?.GetValue<string>() is { } id)
                sb.Append(", i.e. ").Append(id);
            sb.Append('.');
        }
    }

    public static string Render(JsonObject data)
    {
        var sb = new StringBuilder();
        Append(sb, data);
        return sb.ToString();
    }

    // 读一个数值字段。同一个字段在不同来路上是不同的 JSON 数字类型——宿主侧直接塞 double，经桥回来的是
    // JsonElement，合成的用例里可能是整型字面量——而 GetValue<double> 只认对上的那一种（JsonValue<int> 会抛）。
    // 渲染是纯函数、拿到什么读什么，故一律走这里。
    static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        if (value.TryGetValue<double>(out var d))
            return d;
        return value.TryGetValue<int>(out var i) ? i : null;
    }

    // m:ss.mm —— 秒带两位小数，够外部把播放位置对上；分钟不补零（"0:03.24"）。
    public static string Time(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            return "?";
        bool negative = seconds < 0;
        var abs = negative ? -seconds : seconds;
        int minutes = (int)(abs / 60);
        return string.Format(CultureInfo.InvariantCulture, "{0}{1}:{2:00.00}", negative ? "-" : "", minutes, abs - minutes * 60);
    }
}
