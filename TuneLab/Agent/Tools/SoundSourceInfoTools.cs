using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Extensions.Instruments;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Agent;

// 环境感知（只读）——音源目录 + 音源参数 schema。让 agent 枚举可用的 voice/instrument 音源、读其元数据与自定义参数，
// 以推荐/识别音源、了解某音源暴露哪些参数（诉求 5 + 诉求 4）。分三层（渐进式披露、避免一次性 Init 全部引擎）：
//  · 不给 engine        → 列音源【引擎】（type id / 显示名 / 提供包），不触发 Init；
//  · 给 engine          → 列该引擎的具体【音源】（id / 名 / 描述），仅 Init 该引擎；
//  · 给 engine + source → 读该【音源】的参数 schema（part/note/自动化/音素级，各带类型/范围/默认），仅 Init 该引擎。
// 参数 schema 是 voiceId 的函数（不同音源可声明不同参数），且 manager 对未知 id 静默回退空引擎给出误导性空 schema——
// 故必须按「引擎 + 真实音源 id」读，用自建 part-free 合成 context（真 VoiceId + 空 note/属性）纯静态求「默认值版」schema。
// 当前 part 用哪个音源走 run_script 的 part.soundSource()（只读快照）；「切换音源 / 改参」是写操作、属后续写通道。
internal sealed class ListSoundSourcesTool : IAgentTool
{
    public string Name => "list_sound_sources";

    public string Description =>
        "Explore the voice/instrument sound sources in TuneLab (a drill-down). WITHOUT `engine`: lists the sound-source ENGINES " +
        "(type id, display name, providing package). WITH `engine`=<type id>: lists that engine's individual SOURCES (each source's id, name, description). " +
        "WITH `engine` AND `source`=<source id>: lists that source's PARAMETERS (part/note/automation/phoneme, each with type, range and default). " +
        "Optional `kind`='voice'|'instrument' filters engine listing. Read-only — use to recommend/identify sources and understand their parameters. " +
        "(The source the current part uses is read via run_script: part.soundSource(). Changing a source or its parameters is not yet scriptable.)";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "kind": { "type": "string", "enum": ["voice", "instrument"], "description": "Optional: limit engine listing to voice or instrument." },
            "engine": { "type": "string", "description": "Optional: an engine's type id (from a prior no-engine call) to list its sources, or (with source) to read a source's parameters." },
            "source": { "type": "string", "description": "Optional: a source id within `engine` (from a prior engine call) to list that source's parameter schema. Requires `engine`." }
          },
          "additionalProperties": false
        }
        """;

    // 单次回灌的音源条数上限（防超大声库淹没上下文）；超出截断并注明。
    const int MaxSources = 300;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string? kind, engine, source;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            kind = doc.RootElement.GetStringOrNull("kind");
            engine = doc.RootElement.GetStringOrNull("engine");
            source = doc.RootElement.GetStringOrNull("source");
        }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        bool wantVoice = kind == null || string.Equals(kind, "voice", StringComparison.OrdinalIgnoreCase);
        bool wantInstr = kind == null || string.Equals(kind, "instrument", StringComparison.OrdinalIgnoreCase);
        if (!wantVoice && !wantInstr)
            return "Error: \"kind\" must be \"voice\" or \"instrument\".";
        if (source != null && engine == null)
            return "Error: \"source\" needs \"engine\" — a source id belongs to an engine. Call list_sound_sources with just engine=<type id> first to see its source ids.";

        // 音源枚举 / 参数 schema 会惰性 Init 引擎（跑插件代码），在 UI 线程执行以对齐宿主其余引擎操作。
        return await Dispatcher.UIThread.InvokeAsync(() =>
            engine == null ? ListEngines(wantVoice, wantInstr)
            : source == null ? ListEngineSources(engine, wantVoice, wantInstr)
            : DescribeSourceParameters(engine, source, wantVoice, wantInstr));
    }

    // 列引擎（不 Init）：type id / 显示名 / 提供包（共用 EngineCatalog，与 effect 同格式）。
    static string ListEngines(bool wantVoice, bool wantInstr)
    {
        var sb = new StringBuilder();
        if (wantVoice)
            EngineCatalog.AppendEngineList(sb, "Voice", VoicesManager.GetAllVoiceEngines(), VoicesManager.GetDisplayName, VoicesManager.GetProviders);
        if (wantInstr)
            EngineCatalog.AppendEngineList(sb, "Instrument", InstrumentsManager.GetAllInstrumentEngines(), InstrumentsManager.GetDisplayName, InstrumentsManager.GetProviders);
        if (sb.Length == 0)
            return "No sound-source engines are available.";
        sb.Append("\nPass engine=<type id> to list an engine's individual sources.");
        return sb.ToString();
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

    // 读某【音源】的参数 schema（Init 该引擎）：用真实 source id 建 part-free 合成 context，纯静态调 manager 的
    // Get*Config 方法。config 是 voiceId 的函数、且未知 id 静默回退空引擎，故必先校验 source 存在（TryGet*Info）。
    static string DescribeSourceParameters(string engine, string source, bool wantVoice, bool wantInstr)
    {
        if (string.IsNullOrEmpty(engine))
            return "Error: \"engine\" is the empty (no-source) engine; pass a real engine type id.";

        bool isVoice = VoicesManager.GetAllVoiceEngines().Contains(engine);
        bool isInstr = InstrumentsManager.GetAllInstrumentEngines().Contains(engine);

        if (wantVoice && isVoice)
        {
            if (!VoicesManager.TryGetVoiceInfo(engine, source, out var info))
                return string.Format("Error: no source \"{0}\" in voice engine \"{1}\" (or the engine could not load). Call list_sound_sources with engine=\"{1}\" to see its source ids.", source, engine);

            var partCtx = new StaticVoicePartContext(source);
            var noteCtx = new StaticVoiceNoteContext(source);
            var sb = new StringBuilder();
            sb.Append(string.Format("Parameters for voice source \"{0}\" ({1}) in engine \"{2}\" (type={3}):", info.Name, source, VoicesManager.GetDisplayName(engine), engine));
            int total = 0;
            total += SchemaText.AppendProperties(sb, "Part properties", () => VoicesManager.GetPartPropertyConfig(engine, partCtx));
            total += SchemaText.AppendAutomations(sb, "Part automation tracks (editable)", () => VoicesManager.GetAutomationConfigs(engine, partCtx));
            total += SchemaText.AppendAutomations(sb, "Read-only readback tracks", () => VoicesManager.GetSynthesizedParameterConfigs(engine, partCtx));
            total += SchemaText.AppendProperties(sb, "Note properties", () => VoicesManager.GetNotePropertyConfig(engine, noteCtx));
            // phoneme schema 是【数据驱动】的（slot 来自 note 里真实音素）——空 note 拿不到。若引擎恰好静态声明了 slot
            // schema 则照列；否则如实说明它需合成后才可见（不造假 note），phoneme 的真发现留给未来「探测沙箱」。
            int phonemeShown = SchemaText.AppendPhonemes(sb, "Phoneme properties", () => VoicesManager.GetPhonemePropertyConfigs(engine, noteCtx));
            if (phonemeShown == 0)
                sb.Append("\nPhoneme properties: not available from this static read — this engine declares them per actual phoneme, so they only appear once a note with a real lyric is synthesized (the other groups above are complete).");
            return Finish(sb, total);
        }
        if (wantInstr && isInstr)
        {
            if (!InstrumentsManager.TryGetInstrumentInfo(engine, source, out var info))
                return string.Format("Error: no source \"{0}\" in instrument engine \"{1}\" (or the engine could not load). Call list_sound_sources with engine=\"{1}\" to see its source ids.", source, engine);

            var partCtx = new StaticInstrumentPartContext(source);
            var noteCtx = new StaticInstrumentNoteContext(source);
            var sb = new StringBuilder();
            sb.Append(string.Format("Parameters for instrument source \"{0}\" ({1}) in engine \"{2}\" (type={3}):", info.Name, source, InstrumentsManager.GetDisplayName(engine), engine));
            int total = 0;
            total += SchemaText.AppendProperties(sb, "Part properties", () => InstrumentsManager.GetPartPropertyConfig(engine, partCtx));
            total += SchemaText.AppendAutomations(sb, "Part automation tracks (editable)", () => InstrumentsManager.GetAutomationConfigs(engine, partCtx));
            total += SchemaText.AppendAutomations(sb, "Read-only readback tracks", () => InstrumentsManager.GetSynthesizedParameterConfigs(engine, partCtx));
            total += SchemaText.AppendProperties(sb, "Note properties", () => InstrumentsManager.GetNotePropertyConfig(engine, noteCtx));
            return Finish(sb, total);
        }

        return string.Format("Error: no {0}engine with type id \"{1}\". Call list_sound_sources (no engine) to see engine type ids.",
            (isVoice || isInstr) ? "matching " : "", engine);
    }

    static string Finish(StringBuilder sb, int total)
    {
        if (total == 0)
            sb.Append("\nThis source exposes no custom parameters (at default values).");
        // 静态枚举固有上限：条件化 schema 只呈现默认分支；改这些参数目前也不可脚本化。
        sb.Append("\n(Schema is at default values; some engines reveal more parameters once specific values are set. Editing these is not yet scriptable.)");
        return sb.ToString();
    }

    // part-free 合成 context：真实音源 id + 空 note/属性/自动化。宿主自带的 PartPropertyContext 绑真 part 且 internal，
    // 故 Agent 层自建（同 EffectInfoTools.StaticEffectContext）。空 Notes → 引擎给「默认/无选中」版 part/note 属性 schema；
    // 但【phoneme schema 例外】——它按 note 里真实音素声明（slot 数据驱动），空 note 恒空（见上，需合成才可见）。
    sealed class VoicePartView(string voiceId) : IVoiceSynthesisPartView
    {
        public string VoiceId => voiceId;
        public IReadOnlyList<IVoiceSynthesisNoteView> Notes => Array.Empty<IVoiceSynthesisNoteView>();
        public PropertyObject PartProperties => PropertyObject.Empty;
        public IReadOnlyMap<string, IAutomationEvaluator> Automations => Map<string, IAutomationEvaluator>.Empty;
    }

    sealed class StaticVoicePartContext(string voiceId) : IVoiceSynthesisPartPropertyContext
    {
        public IReadOnlyList<IVoiceSynthesisPartView> Parts { get; } = new IVoiceSynthesisPartView[] { new VoicePartView(voiceId) };
    }

    sealed class StaticVoiceNoteContext(string voiceId) : IVoiceSynthesisNotePropertyContext
    {
        public IVoiceSynthesisPartView Part { get; } = new VoicePartView(voiceId);
        public IReadOnlyList<IVoiceSynthesisNoteView> Notes => Array.Empty<IVoiceSynthesisNoteView>();
    }

    sealed class InstrumentPartView(string instrumentId) : IInstrumentSynthesisPartView
    {
        public string InstrumentId => instrumentId;
        public IReadOnlyList<IInstrumentSynthesisNoteView> Notes => Array.Empty<IInstrumentSynthesisNoteView>();
        public PropertyObject PartProperties => PropertyObject.Empty;
        public IReadOnlyMap<string, IAutomationEvaluator> Automations => Map<string, IAutomationEvaluator>.Empty;
    }

    sealed class StaticInstrumentPartContext(string instrumentId) : IInstrumentSynthesisPartPropertyContext
    {
        public IReadOnlyList<IInstrumentSynthesisPartView> Parts { get; } = new IInstrumentSynthesisPartView[] { new InstrumentPartView(instrumentId) };
    }

    sealed class StaticInstrumentNoteContext(string instrumentId) : IInstrumentSynthesisNotePropertyContext
    {
        public IInstrumentSynthesisPartView Part { get; } = new InstrumentPartView(instrumentId);
        public IReadOnlyList<IInstrumentSynthesisNoteView> Notes => Array.Empty<IInstrumentSynthesisNoteView>();
    }
}
