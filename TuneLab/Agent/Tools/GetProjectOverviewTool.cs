using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Agent;

// 唯一的只读「定向」工具：返回工程结构摘要（PPQ、tempo、拍号、各轨 1-based 编号/名/状态/part 数/音符数），
// 让模型在写脚本前一眼看清轨/part 号与 PPQ。其余读取（音符明细、参数曲线、当前 part/播放线等）一律走 run_script
// （tl.currentPart()/notes()/samplePitch()/playhead()…）——本工具只兜住"动手前先看一眼"与"用户随口问事实"两个场景。
// 直接读 IProject，不经任何 facade。
internal sealed class GetProjectOverviewTool(IProject project) : IAgentTool
{
    public string Name => "get_project_overview";

    public string Description =>
        "Get an overview of the current project: PPQ (ticks per quarter note), tempo, time signature, and every track with its 1-based number, " +
        "name, mute/solo, gain/pan, part count and note count. Call this first to orient before editing. " +
        "For note-level detail or any edit, write a script with run_script (call get_script_api once for the `tl` API). Track/part/note numbers are 1-based.";

    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Project: PPQ={0} (ticks per quarter note). Positions/durations are in ticks.", MusicTheory.RESOLUTION));

        var tempos = project.TempoManager.Tempos;
        if (tempos.Count > 0)
        {
            sb.Append("Tempo: ");
            sb.Append(string.Join(", ", tempos.Select(t => string.Format(CultureInfo.InvariantCulture, "{0:0.##}bpm@tick{1:0}", t.Bpm, t.Pos))));
            sb.AppendLine();
        }
        var timeSigs = project.TimeSignatureManager.TimeSignatures;
        if (timeSigs.Count > 0)
        {
            sb.Append("Time signature: ");
            sb.Append(string.Join(", ", timeSigs.Select(s => string.Format(CultureInfo.InvariantCulture, "{0}/{1}@bar{2}", s.Numerator, s.Denominator, s.BarIndex + 1))));
            sb.AppendLine();
        }

        var tracks = project.Tracks;
        sb.AppendLine(string.Format("Tracks ({0}):", tracks.Count));
        for (int i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            var parts = track.Parts.ToList();
            int noteCount = parts.OfType<IMidiPart>().Sum(p => p.Notes.Count());
            var flags = new List<string>();
            if (track.IsMute.Value) flags.Add("mute");
            if (track.IsSolo.Value) flags.Add("solo");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  Track {0}: \"{1}\"{2}, gain={3:0.#}dB, pan={4:0.##}, parts={5}, notes={6}",
                i + 1, track.Name.Value, flags.Count > 0 ? " [" + string.Join(",", flags) + "]" : "",
                track.Gain.Value, track.Pan.Value, parts.Count, noteCount));
        }
        return Task.FromResult(sb.ToString());
    }
}
