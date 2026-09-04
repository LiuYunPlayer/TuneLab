using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Scripting;

namespace TuneLab.Commands.Handlers;

// 探测沙箱：在一个【可丢弃的无头工程】里跑一段 JavaScript，用同一 `tl` 动作面造场景，再真触发合成、
// 读回显——够到静态读 schema 够不着的东西（尤其【真实音素】：只有拿真音源合成一个带合法歌词的 note
// 之后才存在，`source list` 的静态读明确标注了这点、留给这里发现）。
//
// 与 `script run` 的关键区别：这里的工程是全新、隔离、跑完即弃的，与用户工程无关——故写入【不碰用户
// 数据、不需授权】，可放开试。这也是 CommandKind 单列 Sandbox 的原因：压进 Edit 会让所有入口的安全
// 标注说谎（它不该过闸门），归成 Read 又不对（它确实在跑会写数据的代码）。
//
// 合成很重（引擎要加载模型、耗时数秒），故【一段脚本一次探完】最省：把造场景→合成→读回显→精炼成结论
// 全写在一段里，迭代/中间数据都留在脚本内、不进调用方的上下文。
internal sealed class SandboxRunCommand : ICommand
{
    public string Path => "sandbox run";
    public CommandKind Kind => CommandKind.Sandbox;
    public string AgentToolName => "run_in_sandbox";

    public string Brief => "Probe engines in a throwaway headless project";

    public string Documentation =>
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

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var code = args.Json.GetString("code") ?? "";
        if (string.IsNullOrWhiteSpace(code))
            return CommandResult.Fail("empty_code", "\"code\" is empty.");

        // 沙箱自带无头泵、不碰宿主主线程与用户工程，故不经 ctx.OnMainThread（那会把界面卡住整个合成时长）。
        var result = await SandboxHost.RunAsync(code, cancellationToken);

        // 脚本自己出错【不是命令失败】：沙箱如约跑完了，错误是它要回报的产物之一，而且常常与 print 的
        // 输出一起才有诊断价值——套成 CommandError 会把输出丢掉。
        return CommandResult.Ok(new JsonObject
        {
            ["ok"] = result.Ok,
            ["output"] = string.IsNullOrEmpty(result.Output) ? null : result.Output.TrimEnd(),
            ["resultText"] = result.Ok ? result.ResultText : null,
            ["error"] = result.Ok ? null : result.Error,
        });
    }

    // 措辞与搬家前逐字一致。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var sb = new StringBuilder();
        if (obj["output"]?.GetValue<string>() is { } output)
            sb.Append("Output:\n").Append(output).Append('\n');
        if (obj["ok"]!.GetValue<bool>())
        {
            if (obj["resultText"]?.GetValue<string>() is { } resultText)
                sb.Append("Result: ").Append(resultText).Append('\n');
            if (sb.Length == 0)
                sb.Append("Sandbox script ran (no output or return value).");
        }
        else
            sb.Append("Error: ").Append(obj["error"]?.GetValue<string>());
        return sb.ToString().TrimEnd();
    }
}
