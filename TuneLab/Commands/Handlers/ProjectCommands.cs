using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Commands.Handlers;

// 唯一的只读「定向」命令：返回工程结构摘要（PPQ、tempo、拍号、各轨 1-based 编号/名/状态/part 数/音符数），
// 让调用方在动手前一眼看清轨/part 号与 PPQ。其余读取（音符明细、参数曲线、当前 part/播放线等）一律走
// `script run`（tl.currentPart()/notes()/samplePitch()/playhead()…）——本命令只兜住"动手前先看一眼"
// 与"用户随口问事实"两个场景。直接读 IProject，不经任何 facade。
internal sealed class ProjectStatusCommand : ICommand
{
    public string Path => "project status";
    public CommandKind Kind => CommandKind.Read;

    // 沿用搬家前的工具名，模型侧零感知（系统提示与其它工具的描述文本都在引用它）。
    public string AgentToolName => "get_project_overview";

    public string Brief => "PPQ, tempo, time signature and a per-track summary";

    public string Documentation =>
        "Get an overview of the current project: PPQ (ticks per quarter note), tempo, time signature, and every track with its 1-based number, " +
        "name, mute/solo, gain/pan, part count and note count. Call this first to orient before editing. " +
        "For note-level detail or any edit, write a script with run_script (call get_script_api once for the `tl` API). Track/part/note numbers are 1-based.";

    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    public Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var project = ctx.Project;
        if (project == null)
            return Task.FromResult(CommandResult.Fail("no_project", "No project is open, so there is nothing to report."));

        var tempos = new JsonArray();
        foreach (var tempo in project.TempoManager.Tempos)
            tempos.Add(new JsonObject { ["bpm"] = tempo.Bpm, ["pos"] = tempo.Pos });

        var timeSignatures = new JsonArray();
        foreach (var sig in project.TimeSignatureManager.TimeSignatures)
            timeSignatures.Add(new JsonObject
            {
                ["numerator"] = sig.Numerator,
                ["denominator"] = sig.Denominator,
                ["bar"] = sig.BarIndex + 1,   // 1-based，与呈现一致
            });

        var tracks = new JsonArray();
        var trackList = project.Tracks;
        for (int i = 0; i < trackList.Count; i++)
        {
            var track = trackList[i];
            var parts = track.Parts.ToList();
            tracks.Add(new JsonObject
            {
                ["number"] = i + 1,          // 1-based
                ["name"] = track.Name.Value,
                ["mute"] = track.IsMute.Value,
                ["solo"] = track.IsSolo.Value,
                ["gain"] = track.Gain.Value,
                ["pan"] = track.Pan.Value,
                ["parts"] = parts.Count,
                ["notes"] = parts.OfType<IMidiPart>().Sum(p => p.Notes.Count()),
            });
        }

        return Task.FromResult(CommandResult.Ok(new JsonObject
        {
            ["ppq"] = MusicTheory.RESOLUTION,
            ["tempos"] = tempos,
            ["timeSignatures"] = timeSignatures,
            ["tracks"] = tracks,
        }));
    }

    // 措辞与搬家前逐字一致——这是"内置 agent 行为不变"这个验收标准的一部分。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Project: PPQ={0} (ticks per quarter note). Positions/durations are in ticks.", obj["ppq"]!.GetValue<int>()));

        var tempos = obj["tempos"]!.AsArray();
        if (tempos.Count > 0)
        {
            sb.Append("Tempo: ");
            sb.Append(string.Join(", ", tempos.Select(t => string.Format(CultureInfo.InvariantCulture,
                "{0:0.##}bpm@tick{1:0}", t!["bpm"]!.GetValue<double>(), t["pos"]!.GetValue<double>()))));
            sb.AppendLine();
        }

        var timeSignatures = obj["timeSignatures"]!.AsArray();
        if (timeSignatures.Count > 0)
        {
            sb.Append("Time signature: ");
            sb.Append(string.Join(", ", timeSignatures.Select(s => string.Format(CultureInfo.InvariantCulture,
                "{0}/{1}@bar{2}", s!["numerator"]!.GetValue<int>(), s["denominator"]!.GetValue<int>(), s["bar"]!.GetValue<int>()))));
            sb.AppendLine();
        }

        var tracks = obj["tracks"]!.AsArray();
        sb.AppendLine(string.Format("Tracks ({0}):", tracks.Count));
        foreach (var track in tracks)
        {
            var flags = new List<string>();
            if (track!["mute"]!.GetValue<bool>()) flags.Add("mute");
            if (track["solo"]!.GetValue<bool>()) flags.Add("solo");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  Track {0}: \"{1}\"{2}, gain={3:0.#}dB, pan={4:0.##}, parts={5}, notes={6}",
                track["number"]!.GetValue<int>(), track["name"]!.GetValue<string>(),
                flags.Count > 0 ? " [" + string.Join(",", flags) + "]" : "",
                track["gain"]!.GetValue<double>(), track["pan"]!.GetValue<double>(),
                track["parts"]!.GetValue<int>(), track["notes"]!.GetValue<int>()));
        }
        return sb.ToString();
    }
}
