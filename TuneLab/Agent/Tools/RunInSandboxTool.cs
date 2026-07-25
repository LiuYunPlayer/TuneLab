using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Scripting;

namespace TuneLab.Agent;

// 探测沙箱工具（F 支柱）：让模型在一个【可丢弃的无头工程】里跑一段 JavaScript，用同一 `tl` 动作面造场景，
// 再真触发合成、读回显——够到静态读 schema 够不着的东西（尤其【真实音素】：只有拿真音源合成一个带合法歌词的
// note 之后才存在，list_sound_sources 的静态读明确标注了这点、留给这里发现）。
//
// 与 run_script 的关键区别：这里的工程是全新、隔离、跑完即弃的，与用户工程无关——故写入【不碰用户数据、
// 不需授权】，可放开试。合成很重（引擎要加载模型、耗时数秒），故【一段脚本一次探完】最省：把造场景→合成→
// 读回显→精炼成结论全写在一段里，迭代/中间数据都留在脚本内、不进上下文。
internal sealed class RunInSandboxTool : IAgentTool
{
    public string Name => "run_in_sandbox";

    public string Description =>
        "Run JavaScript in a THROWAWAY, isolated, headless project to PROBE things a static read can't reach — above all the REAL phonemes an engine " +
        "produces (which only exist after you synthesize a note that has a real voice and a valid lyric; list_sound_sources says as much and defers them here). " +
        "This project is brand-new and discarded when the script returns: it is NOT the user's project, so edits here touch no user data and need no authorization — experiment freely. " +
        "Build the scene with the same `tl` object as run_script (tl.currentProject().addTrack(), track.addPart({startPos,endPos}), part.addNote({pos,dur,pitch,lyric}) …); " +
        "call get_script_api once if you don't know the tl API. Attach a voice with the normal tl write `part.setSoundSource({kind:'voice', type, id})`. " +
        "Plus a `sandbox` global for synthesis (only meaningful here — it drives synthesis on a headless pump):\n" +
        "  • sandbox.voices() → [{type,id,name}] of installed voice sources (this loads engines).\n" +
        "  • sandbox.synthesize(part, {timeoutMs?, maxDispatches?}) → run offline synthesis and WAIT; returns {done, dispatches, ms, timedOut}.\n" +
        "  • sandbox.syllable(note) → the synthesized phonemes: {leading:[{symbol,duration,stretchWeight}], body:[...], bodyOffset, symbols:[...]} or null if not synthesized.\n" +
        "Synthesis is heavy — do the WHOLE probe (build → synthesize → read → summarize) in ONE script; print(...) your findings and return a short conclusion. " +
        "Typical order: pick a voice from sandbox.voices(); add a track and a part; part.setSoundSource({kind:'voice', type, id}); THEN add a note with a valid lyric; " +
        "sandbox.synthesize(part); read sandbox.syllable(note).symbols.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "code": { "type": "string", "description": "JavaScript to run in the throwaway sandbox. Use `tl` to build the scene and `sandbox` to synthesize/read; print(...) for output." }
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

        var result = await SandboxHost.RunAsync(code, cancellationToken);

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(result.Output))
            sb.Append("Output:\n").Append(result.Output.TrimEnd()).Append('\n');
        if (result.Ok)
        {
            if (result.ResultText != null)
                sb.Append("Result: ").Append(result.ResultText).Append('\n');
            if (sb.Length == 0)
                sb.Append("Sandbox script ran (no output or return value).");
        }
        else
        {
            sb.Append("Error: ").Append(result.Error);
        }
        return sb.ToString().TrimEnd();
    }
}
