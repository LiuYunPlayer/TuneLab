using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions.Effect;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Commands.Handlers;

// 环境感知（只读）——effect（音频效果器）引擎目录 + 参数 schema。分两层（避免一次 Init 全部引擎）：
// 不给 engine → 列引擎（不 Init）；给 engine → Init 该引擎、读其参数 schema。
//
// 与音源不同：effect 一个引擎 = 一种效果器类型（无「音源目录」），第二层列的是【参数】而非音源。参数经引擎
// 三个声明方法纯静态求值——用一个 part-free 的空 context（空视图 → 各参数取引擎默认）即可拿到「默认值下」
// 的整棵 schema。条件化 schema 只能拿默认版，是静态枚举的固有上限。
//
// 【Data 形状】引擎清单结构化（身份/生效包/被顶替包都是事实字段，CI 要断言的正是它们）；参数 schema 留成
// 文本一段（schemaText）——那是 ConfigText 产的"类型/范围/默认"描述短语，拆成字段要重写 9 种 config 的
// 分派逻辑，而没有消费者会去断言某参数的范围。判据同 docs 那处例外：产物的事实形态是什么。
internal sealed class EffectListCommand : ICommand
{
    public string Path => "effect list";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "list_effects";

    public string Brief => "List effect engines, or one engine's parameters";

    public string Documentation =>
        "List the audio effect engines installed in TuneLab. WITHOUT `engine`: lists the effect engines (their type id, display name, providing package). " +
        "WITH `engine`=<type id>: lists that engine's parameters — static properties and automation tracks — each with its type, range and default. " +
        "Read-only, for recommending or explaining effects. (Effects are exposed by plugins; there are no built-in effect engines. Reading/editing a part's effect chain is not yet scriptable.)";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "engine": { "type": "string", "description": "Optional: an effect engine's type id (from a prior no-engine call) to list its parameters." }
          },
          "additionalProperties": false
        }
        """;

    // 读参数 schema 会惰性 Init 引擎（跑插件代码），故在宿主主线程上执行以对齐其余引擎操作。
    // 列引擎不 Init，一并放里无碍。
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var engine = args.Json.GetStringOrNull("engine");
        return await ctx.OnMainThread(() => engine != null ? DescribeEngine(engine) : ListEngines());
    }

    static CommandResult ListEngines() => CommandResult.Ok(new JsonObject
    {
        ["mode"] = "engines",
        ["engines"] = EngineCatalog.Collect("effect", EffectManager.GetAllEffectEngines(),
            EffectManager.GetDisplayName, EffectManager.GetProviders),
    });

    static CommandResult DescribeEngine(string type)
    {
        if (!EffectManager.Exists(type))
            return CommandResult.Fail("unknown_engine", string.Format(
                "no effect engine with type id \"{0}\". Call list_effects (no engine) to see engine type ids.", type));

        var data = new JsonObject
        {
            ["mode"] = "parameters",
            ["engine"] = type,
            ["displayName"] = EffectManager.GetDisplayName(type),
        };

        // 加载不上不是调用方的错，故走成功路径、如实标注（同搬家前不带 "Error:" 前缀）。
        var engine = EffectManager.GetInitedEngine(type);
        if (engine == null)
        {
            data["loaded"] = false;
            return CommandResult.Ok(data);
        }

        var ctx = new StaticEffectContext();
        var sb = new StringBuilder();
        // 三类参数各自 try/catch（在 SchemaText 内）：插件求值抛错不拖垮整个回报（如实标注该组不可用）。
        int total = 0;
        total += SchemaText.AppendProperties(sb, "Static properties", () => engine.GetPropertyConfig(ctx));
        total += SchemaText.AppendAutomations(sb, "Automation parameters (editable tracks)", () => engine.GetAutomationConfigs(ctx));
        total += SchemaText.AppendAutomations(sb, "Read-only synthesized parameter tracks", () => engine.GetSynthesizedParameterConfigs(ctx));

        data["loaded"] = true;
        data["parameterCount"] = total;
        data["schemaText"] = sb.ToString();
        return CommandResult.Ok(data);
    }

    // 措辞与搬家前逐字一致。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        if (obj["mode"]!.GetValue<string>() == "engines")
        {
            var engines = obj["engines"]!.AsArray();
            if (engines.Count == 0)
                return "No effect engines are installed. Effects come from plugins (there are no built-in effect engines).";

            var list = new StringBuilder();
            EngineCatalog.AppendEngineList(list, "Effect", engines);
            list.Append("\nPass engine=<type id> to see an engine's parameters.");
            return list.ToString();
        }

        var type = obj["engine"]!.GetValue<string>();
        var displayName = obj["displayName"]!.GetValue<string>();
        if (!obj["loaded"]!.GetValue<bool>())
            return string.Format("The effect engine \"{0}\" (type={1}) could not be loaded, so its parameters are unavailable.", displayName, type);

        var sb = new StringBuilder();
        sb.Append(string.Format("Effect engine \"{0}\" (type={1}):", displayName, type));
        sb.Append(obj["schemaText"]!.GetValue<string>());
        if (obj["parameterCount"]!.GetValue<int>() == 0)
            sb.Append("\nThis effect exposes no parameters (or none at default values).");
        return sb.ToString();
    }

    // part-free 的声明面 context：一个空视图（无改过的值 → 各参数取引擎默认；无曲线数据）。宿主自带的
    // EffectPropertyContext 绑 part 且 private，不可复用；这两个接口是 public，这里自建即可纯静态求 schema。
    sealed class StaticEffectContext : IEffectSynthesisPropertyContext
    {
        public IReadOnlyList<IEffectSynthesisView> Effects { get; } = new IEffectSynthesisView[] { new EmptyView() };

        sealed class EmptyView : IEffectSynthesisView
        {
            public PropertyObject Properties => PropertyObject.Empty;
            public IReadOnlyMap<string, IAutomationEvaluator> Automations => Map<string, IAutomationEvaluator>.Empty;
        }
    }
}
