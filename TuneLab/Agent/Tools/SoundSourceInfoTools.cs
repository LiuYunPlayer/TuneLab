using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Extensions;
using TuneLab.Extensions.Instruments;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Agent;

// 环境感知（只读）——音源目录。让 agent 枚举可用的 voice/instrument 音源、读其元数据，以推荐/识别音源。
// 是「诉求 5（枚举全部音源 + 元数据）」的地基。分两层（渐进式披露、避免一次性 Init 全部引擎）：
//  · 不给 engine → 列音源【引擎】（type id / 显示名 / 提供包），不触发 Init；
//  · 给 engine  → 列该引擎的具体【音源】（id / 名 / 描述），仅 Init 该引擎。
// 当前 part 用的是哪个音源走 run_script 的 part.soundSource()（只读快照）；「切换 part 音源」是写操作、属后续写通道。
internal sealed class ListSoundSourcesTool : IAgentTool
{
    public string Name => "list_sound_sources";

    public string Description =>
        "List the voice/instrument sound sources available in TuneLab. WITHOUT `engine`: lists the sound-source ENGINES " +
        "(their type id, display name, and providing package). WITH `engine`=<type id>: lists that engine's individual sources " +
        "(each source's id, name, description). Optional `kind`='voice'|'instrument' filters to one kind. " +
        "Read-only — use to recommend or identify sources. (The source the current part uses is read via run_script: part.soundSource().)";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "kind": { "type": "string", "enum": ["voice", "instrument"], "description": "Optional: limit to voice or instrument sources." },
            "engine": { "type": "string", "description": "Optional: an engine's type id (from a prior no-engine call) to list that engine's individual sources." }
          },
          "additionalProperties": false
        }
        """;

    // 单次回灌的音源条数上限（防超大声库淹没上下文）；超出截断并注明。
    const int MaxSources = 300;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string? kind, engine;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            kind = doc.RootElement.GetStringOrNull("kind");
            engine = doc.RootElement.GetStringOrNull("engine");
        }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        bool wantVoice = kind == null || string.Equals(kind, "voice", StringComparison.OrdinalIgnoreCase);
        bool wantInstr = kind == null || string.Equals(kind, "instrument", StringComparison.OrdinalIgnoreCase);
        if (!wantVoice && !wantInstr)
            return "Error: \"kind\" must be \"voice\" or \"instrument\".";

        // 音源枚举会惰性 Init 引擎（跑插件代码），在 UI 线程执行以对齐宿主其余引擎操作。
        return await Dispatcher.UIThread.InvokeAsync(() =>
            engine != null ? ListEngineSources(engine, wantVoice, wantInstr) : ListEngines(wantVoice, wantInstr));
    }

    // 列引擎（不 Init）：type id / 显示名 / 提供包。空引擎 type="" 是无音源回退，跳过。
    static string ListEngines(bool wantVoice, bool wantInstr)
    {
        var sb = new StringBuilder();
        if (wantVoice)
            AppendEngineList(sb, "Voice", VoicesManager.GetAllVoiceEngines(), VoicesManager.GetDisplayName, VoicesManager.GetProviders);
        if (wantInstr)
            AppendEngineList(sb, "Instrument", InstrumentsManager.GetAllInstrumentEngines(), InstrumentsManager.GetDisplayName, InstrumentsManager.GetProviders);
        if (sb.Length == 0)
            return "No sound-source engines are available.";
        sb.Append("\nPass engine=<type id> to list an engine's individual sources.");
        return sb.ToString();
    }

    static void AppendEngineList(StringBuilder sb, string kindLabel, IReadOnlyList<string> engines, Func<string, string> displayName, Func<string, IReadOnlyList<(string PackageId, string DisplayName)>> providers)
    {
        var real = engines.Where(t => !string.IsNullOrEmpty(t)).ToList();
        if (sb.Length > 0) sb.Append('\n');
        sb.Append(kindLabel).Append(" engines (").Append(real.Count).Append("):");
        if (real.Count == 0)
            sb.Append("\n  (none)");
        foreach (var type in real)
        {
            var pkgs = providers(type);
            string pkgLabel = pkgs.Count switch
            {
                0 => "unknown",
                1 => ExtensionManager.GetPackageName(pkgs[0].PackageId),
                _ => "multiple: " + string.Join(", ", pkgs.Select(p => ExtensionManager.GetPackageName(p.PackageId))),
            };
            sb.Append("\n- \"").Append(displayName(type)).Append("\" [type=").Append(type).Append(", package=").Append(pkgLabel).Append("]");
        }
    }

    // 列某引擎的音源（Init 该引擎）：id / 名 / 描述。
    static string ListEngineSources(string engine, bool wantVoice, bool wantInstr)
    {
        if (string.IsNullOrEmpty(engine))
            return "Error: \"engine\" is the empty (no-source) engine; pass a real engine type id from list_sound_sources.";

        bool isVoice = VoicesManager.GetAllVoiceEngines().Contains(engine);
        bool isInstr = InstrumentsManager.GetAllInstrumentEngines().Contains(engine);

        if (wantVoice && isVoice)
            return DescribeSources("voice", engine, VoicesManager.GetDisplayName(engine),
                VoicesManager.GetAllVoiceInfos(engine)?.Select(kv => (kv.Key, kv.Value.Name, kv.Value.Description)));
        if (wantInstr && isInstr)
            return DescribeSources("instrument", engine, InstrumentsManager.GetDisplayName(engine),
                InstrumentsManager.GetAllInstrumentInfos(engine)?.Select(kv => (kv.Key, kv.Value.Name, kv.Value.Description)));

        return string.Format("Error: no {0}engine with type id \"{1}\". Call list_sound_sources (no engine) to see engine type ids.",
            (isVoice || isInstr) ? "matching " : "", engine);
    }

    static string DescribeSources(string kindLabel, string engine, string displayName, IEnumerable<(string Id, string Name, string Description)>? sources)
    {
        if (sources == null)
            return string.Format("The {0} engine \"{1}\" ({2}) could not be loaded, so its sources are unavailable.", kindLabel, displayName, engine);

        var list = sources.ToList();
        var sb = new StringBuilder();
        sb.Append(string.Format("Sources in {0} engine \"{1}\" (type={2}), {3} source(s):", kindLabel, displayName, engine, list.Count));
        int shown = 0;
        foreach (var (id, name, description) in list)
        {
            if (shown++ >= MaxSources)
            {
                sb.Append("\n… (").Append(list.Count - MaxSources).Append(" more; refine your request to narrow the list)");
                break;
            }
            sb.Append("\n- ").Append(id).Append("  \"").Append(name).Append('"');
            if (!string.IsNullOrEmpty(description))
                sb.Append(" — ").Append(description);
        }
        return sb.ToString();
    }
}
