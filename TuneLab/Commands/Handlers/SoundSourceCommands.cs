using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions.Instruments;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Commands.Handlers;

// 环境感知（只读）——音源目录 + 音源参数 schema（诉求 5 + 诉求 4）。分三层（渐进式披露、避免一次性
// Init 全部引擎）：
//  · 不给 engine        → 列音源【引擎】（type id / 显示名 / 提供包），不触发 Init；
//  · 给 engine          → 列该引擎的具体【音源】（id / 名 / 描述），仅 Init 该引擎；
//  · 给 engine + source → 读该【音源】的参数 schema（part/note/自动化/音素级），仅 Init 该引擎。
// 参数 schema 是 voiceId 的函数（不同音源可声明不同参数），且 manager 对未知 id 静默回退空引擎给出误导性
// 空 schema——故必须按「引擎 + 真实音源 id」读，用自建 part-free 合成 context 纯静态求「默认值版」schema。
//
// 【Data 形状】引擎与音源清单结构化（身份/名称/描述/生效包都是事实字段）；参数 schema 留成文本一段
// （schemaText），理由见 EffectListCommand 的同名说明。
internal sealed class SoundSourceListCommand : ICommand
{
    public string Path => "source list";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "list_sound_sources";

    public string Brief => "Drill down engines → sources → a source's parameters";

    public string Documentation =>
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

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var kind = args.Json.GetStringOrNull("kind");
        var engine = args.Json.GetStringOrNull("engine");
        var source = args.Json.GetStringOrNull("source");

        bool wantVoice = kind == null || string.Equals(kind, "voice", StringComparison.OrdinalIgnoreCase);
        bool wantInstr = kind == null || string.Equals(kind, "instrument", StringComparison.OrdinalIgnoreCase);
        if (!wantVoice && !wantInstr)
            return CommandResult.Fail("bad_kind", "\"kind\" must be \"voice\" or \"instrument\".");
        if (source != null && engine == null)
            return CommandResult.Fail("source_without_engine",
                "\"source\" needs \"engine\" — a source id belongs to an engine. Call list_sound_sources with just engine=<type id> first to see its source ids.");

        // 音源枚举 / 参数 schema 会惰性 Init 引擎（跑插件代码），故在宿主主线程上执行以对齐其余引擎操作。
        return await ctx.OnMainThread(() =>
            engine == null ? ListEngines(wantVoice, wantInstr)
            : source == null ? ListEngineSources(engine, wantVoice, wantInstr)
            : DescribeSourceParameters(engine, source, wantVoice, wantInstr));
    }

    // ── 第一层：列引擎（不 Init）
    static CommandResult ListEngines(bool wantVoice, bool wantInstr)
    {
        var groups = new JsonArray();
        if (wantVoice)
            groups.Add(new JsonObject
            {
                ["kind"] = "voice",
                ["kindLabel"] = "Voice",
                ["engines"] = EngineCatalog.Collect("voice", VoicesManager.GetAllVoiceEngines(),
                    VoicesManager.GetDisplayName, VoicesManager.GetProviders),
            });
        if (wantInstr)
            groups.Add(new JsonObject
            {
                ["kind"] = "instrument",
                ["kindLabel"] = "Instrument",
                ["engines"] = EngineCatalog.Collect("instrument", InstrumentsManager.GetAllInstrumentEngines(),
                    InstrumentsManager.GetDisplayName, InstrumentsManager.GetProviders),
            });

        return CommandResult.Ok(new JsonObject { ["mode"] = "engines", ["groups"] = groups });
    }

    // ── 第二层：列某引擎的音源（Init 该引擎）
    static CommandResult ListEngineSources(string engine, bool wantVoice, bool wantInstr)
    {
        if (string.IsNullOrEmpty(engine))
            return EmptyEngineError("from list_sound_sources");

        bool isVoice = VoicesManager.GetAllVoiceEngines().Contains(engine);
        bool isInstr = InstrumentsManager.GetAllInstrumentEngines().Contains(engine);

        if (wantVoice && isVoice)
            return CollectSources("voice", engine, VoicesManager.GetDisplayName(engine),
                VoicesManager.GetAllVoiceInfos(engine)?.Select(kv => (kv.Key, kv.Value.Name, kv.Value.Description)));
        if (wantInstr && isInstr)
            return CollectSources("instrument", engine, InstrumentsManager.GetDisplayName(engine),
                InstrumentsManager.GetAllInstrumentInfos(engine)?.Select(kv => (kv.Key, kv.Value.Name, kv.Value.Description)));

        return UnknownEngineError(engine, isVoice || isInstr);
    }

    static CommandResult CollectSources(string kind, string engine, string displayName, IEnumerable<(string Id, string Name, string Description)>? sources)
    {
        var data = new JsonObject
        {
            ["mode"] = "sources",
            ["kind"] = kind,
            ["engine"] = engine,
            ["engineName"] = displayName,
        };

        // 引擎加载不上不是调用方的错，故走成功路径、如实标注（同搬家前不带 "Error:" 前缀）。
        if (sources == null)
        {
            data["loaded"] = false;
            return CommandResult.Ok(data);
        }

        var list = sources.ToList();
        var array = new JsonArray();
        foreach (var (id, name, description) in list.Take(MaxSources))
            array.Add(new JsonObject
            {
                ["id"] = id,
                ["name"] = name,
                ["description"] = string.IsNullOrEmpty(description) ? null : description,
            });

        data["loaded"] = true;
        data["total"] = list.Count;            // 截断前的真实总数
        data["sources"] = array;
        return CommandResult.Ok(data);
    }

    // ── 第三层：读某音源的参数 schema（Init 该引擎）
    // config 是 voiceId 的函数、且未知 id 会静默回退空引擎，故必先校验 source 存在（TryGet*Info）。
    static CommandResult DescribeSourceParameters(string engine, string source, bool wantVoice, bool wantInstr)
    {
        if (string.IsNullOrEmpty(engine))
            return EmptyEngineError(null);

        bool isVoice = VoicesManager.GetAllVoiceEngines().Contains(engine);
        bool isInstr = InstrumentsManager.GetAllInstrumentEngines().Contains(engine);

        if (wantVoice && isVoice)
        {
            if (!VoicesManager.TryGetVoiceInfo(engine, source, out var info))
                return UnknownSourceError("voice", engine, source);

            var partCtx = new StaticVoicePartContext(source);
            var noteCtx = new StaticVoiceNoteContext(source);
            var sb = new StringBuilder();
            int total = 0;
            total += SchemaText.AppendProperties(sb, "Part properties", () => VoicesManager.GetPartPropertyConfig(engine, partCtx));
            total += SchemaText.AppendAutomations(sb, "Part automation tracks (editable)", () => VoicesManager.GetAutomationConfigs(engine, partCtx));
            total += SchemaText.AppendAutomations(sb, "Read-only synthesized parameter tracks", () => VoicesManager.GetSynthesizedParameterConfigs(engine, partCtx));
            total += SchemaText.AppendProperties(sb, "Note properties", () => VoicesManager.GetNotePropertyConfig(engine, noteCtx));
            // phoneme schema 是【数据驱动】的（slot 来自 note 里真实音素）——空 note 拿不到。若引擎恰好静态声明了
            // slot schema 则照列；否则如实说明它需合成后才可见（不造假 note），phoneme 的真发现留给探测沙箱。
            int phonemeShown = SchemaText.AppendPhonemes(sb, "Phoneme properties", () => VoicesManager.GetPhonemePropertyConfigs(engine, noteCtx));
            if (phonemeShown == 0)
                sb.Append("\nPhoneme properties: not available from this static read — this engine declares them per actual phoneme, so they only appear once a note with a real lyric is synthesized (the other groups above are complete).");

            return Parameters("voice", engine, VoicesManager.GetDisplayName(engine), source, info.Name, total, sb.ToString());
        }

        if (wantInstr && isInstr)
        {
            if (!InstrumentsManager.TryGetInstrumentInfo(engine, source, out var info))
                return UnknownSourceError("instrument", engine, source);

            var partCtx = new StaticInstrumentPartContext(source);
            var noteCtx = new StaticInstrumentNoteContext(source);
            var sb = new StringBuilder();
            int total = 0;
            total += SchemaText.AppendProperties(sb, "Part properties", () => InstrumentsManager.GetPartPropertyConfig(engine, partCtx));
            total += SchemaText.AppendAutomations(sb, "Part automation tracks (editable)", () => InstrumentsManager.GetAutomationConfigs(engine, partCtx));
            total += SchemaText.AppendAutomations(sb, "Read-only synthesized parameter tracks", () => InstrumentsManager.GetSynthesizedParameterConfigs(engine, partCtx));
            total += SchemaText.AppendProperties(sb, "Note properties", () => InstrumentsManager.GetNotePropertyConfig(engine, noteCtx));

            return Parameters("instrument", engine, InstrumentsManager.GetDisplayName(engine), source, info.Name, total, sb.ToString());
        }

        return UnknownEngineError(engine, isVoice || isInstr);
    }

    static CommandResult Parameters(string kind, string engine, string engineName, string source, string sourceName, int count, string schemaText)
        => CommandResult.Ok(new JsonObject
        {
            ["mode"] = "parameters",
            ["kind"] = kind,
            ["engine"] = engine,
            ["engineName"] = engineName,
            ["source"] = source,
            ["sourceName"] = sourceName,
            ["parameterCount"] = count,
            ["schemaText"] = schemaText,
        });

    static CommandResult EmptyEngineError(string? suffix)
        => CommandResult.Fail("empty_engine", "\"engine\" is the empty (no-source) engine; pass a real engine type id"
            + (suffix == null ? "." : " " + suffix + "."));

    static CommandResult UnknownEngineError(string engine, bool wrongKind)
        => CommandResult.Fail("unknown_engine", string.Format(
            "no {0}engine with type id \"{1}\". Call list_sound_sources (no engine) to see engine type ids.",
            wrongKind ? "matching " : "", engine));

    static CommandResult UnknownSourceError(string kind, string engine, string source)
        => CommandResult.Fail("unknown_source", string.Format(
            "no source \"{0}\" in {1} engine \"{2}\" (or the engine could not load). Call list_sound_sources with engine=\"{2}\" to see its source ids.",
            source, kind, engine));

    // 措辞与搬家前逐字一致。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        switch (obj["mode"]!.GetValue<string>())
        {
            case "engines":
            {
                var sb = new StringBuilder();
                foreach (var group in obj["groups"]!.AsArray())
                    EngineCatalog.AppendEngineList(sb, group!["kindLabel"]!.GetValue<string>(), group["engines"]!.AsArray());
                if (sb.Length == 0)
                    return "No sound-source engines are available.";
                sb.Append("\nPass engine=<type id> to list an engine's individual sources.");
                return sb.ToString();
            }

            case "sources":
            {
                var kind = obj["kind"]!.GetValue<string>();
                var engineName = obj["engineName"]!.GetValue<string>();
                var engine = obj["engine"]!.GetValue<string>();
                if (!obj["loaded"]!.GetValue<bool>())
                    return string.Format("The {0} engine \"{1}\" ({2}) could not be loaded, so its sources are unavailable.", kind, engineName, engine);

                int total = obj["total"]!.GetValue<int>();
                var sources = obj["sources"]!.AsArray();
                var sb = new StringBuilder();
                sb.Append(string.Format("Sources in {0} engine \"{1}\" (type={2}), {3} source(s):", kind, engineName, engine, total));
                foreach (var node in sources)
                {
                    var item = node!.AsObject();
                    sb.Append("\n- ").Append(item["id"]!.GetValue<string>()).Append("  \"").Append(item["name"]!.GetValue<string>()).Append('"');
                    var description = item["description"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(description))
                        sb.Append(" — ").Append(description);
                }
                if (total > sources.Count)
                    sb.Append("\n… (").Append(total - MaxSources).Append(" more; refine your request to narrow the list)");
                return sb.ToString();
            }

            default:
            {
                var sb = new StringBuilder();
                sb.Append(string.Format("Parameters for {0} source \"{1}\" ({2}) in engine \"{3}\" (type={4}):",
                    obj["kind"]!.GetValue<string>(), obj["sourceName"]!.GetValue<string>(), obj["source"]!.GetValue<string>(),
                    obj["engineName"]!.GetValue<string>(), obj["engine"]!.GetValue<string>()));
                sb.Append(obj["schemaText"]!.GetValue<string>());
                if (obj["parameterCount"]!.GetValue<int>() == 0)
                    sb.Append("\nThis source exposes no custom parameters (at default values).");
                // 静态枚举固有上限：条件化 schema 只呈现默认分支；改这些参数目前也不可脚本化。
                sb.Append("\n(Schema is at default values; some engines reveal more parameters once specific values are set. Editing these is not yet scriptable.)");
                return sb.ToString();
            }
        }
    }

    // part-free 合成 context：真实音源 id + 空 note/属性/自动化。宿主自带的 PartPropertyContext 绑真 part 且
    // internal，故这里自建（同 EffectListCommand.StaticEffectContext）。空 Notes → 引擎给「默认/无选中」版
    // part/note 属性 schema；但【phoneme schema 例外】——它按 note 里真实音素声明（slot 数据驱动），空 note 恒空。
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
