using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Extensions.Effect;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Agent;

// 环境感知（只读）——effect（音频效果器）引擎目录 + 参数 schema。让 agent 知道装了哪些效果器、各自暴露哪些参数
// （类型/范围/默认），以推荐/解释效果器。是「诉求 6（访问 effect 插件元数据）」的地基。分两层（同 list_sound_sources
// 哲学，避免一次 Init 全部引擎）：不给 engine → 列引擎（不 Init）；给 engine → Init 该引擎、读其参数 schema。
//
// 与音源不同：effect 一个引擎 = 一种效果器类型（无「音源目录」），第二层列的是【参数】而非音源。参数经引擎三个声明
// 方法（GetPropertyConfig / GetAutomationConfigs / GetSynthesizedParameterConfigs）纯静态求值——用一个 part-free 的
// 空 context（空 View：无改过的值 → 各参数取引擎默认）即可拿到「默认值下」的整棵 schema。条件化 schema 只能拿默认版，
// 是静态枚举的固有上限。当前 effect 对 tl 脚本面完全不可见（读写 effect 链属后续写通道），本工具是 agent 触达 effect 的唯一通道。
internal sealed class ListEffectsTool : IAgentTool
{
    public string Name => "list_effects";

    public string Description =>
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

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string? engine;
        try { using var doc = JsonDocument.Parse(argumentsJson); engine = doc.RootElement.GetStringOrNull("engine"); }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        // 读参数 schema 会惰性 Init 引擎（跑插件代码），在 UI 线程执行以对齐宿主其余引擎操作。列引擎不 Init，一并放里无碍。
        return await Dispatcher.UIThread.InvokeAsync(() => engine != null ? DescribeEngine(engine) : ListEngines());
    }

    // 列引擎（不 Init）。effect 无内建引擎，全来自插件。
    static string ListEngines()
    {
        if (EffectManager.GetAllEffectEngines().Count == 0)
            return "No effect engines are installed. Effects come from plugins (there are no built-in effect engines).";

        var sb = new StringBuilder();
        EngineCatalog.AppendEngineList(sb, "Effect", "effect", EffectManager.GetAllEffectEngines(), EffectManager.GetDisplayName, EffectManager.GetProviders);
        sb.Append("\nPass engine=<type id> to see an engine's parameters.");
        return sb.ToString();
    }

    // 列某引擎的参数 schema（Init 该引擎）：静态属性 + 可编辑自动化轨 + 只读回显轨。
    static string DescribeEngine(string type)
    {
        if (!EffectManager.Exists(type))
            return string.Format("Error: no effect engine with type id \"{0}\". Call list_effects (no engine) to see engine type ids.", type);

        var engine = EffectManager.GetInitedEngine(type);
        if (engine == null)
            return string.Format("The effect engine \"{0}\" (type={1}) could not be loaded, so its parameters are unavailable.", EffectManager.GetDisplayName(type), type);

        var ctx = new StaticEffectContext();
        var sb = new StringBuilder();
        sb.Append(string.Format("Effect engine \"{0}\" (type={1}):", EffectManager.GetDisplayName(type), type));

        // 三类参数各自 try/catch（在 SchemaText 内）：插件求值抛错不拖垮整个回报（如实标注该组不可用）。
        int total = 0;
        total += SchemaText.AppendProperties(sb, "Static properties", () => engine.GetPropertyConfig(ctx));
        total += SchemaText.AppendAutomations(sb, "Automation parameters (editable tracks)", () => engine.GetAutomationConfigs(ctx));
        total += SchemaText.AppendAutomations(sb, "Read-only synthesized parameter tracks", () => engine.GetSynthesizedParameterConfigs(ctx));

        if (total == 0)
            sb.Append("\nThis effect exposes no parameters (or none at default values).");
        return sb.ToString();
    }

    // part-free 的声明面 context：一个空视图（无改过的值 → 各参数取引擎默认；无曲线数据）。宿主自带的
    // EffectPropertyContext 绑 part 且 private，不可复用；这两个接口是 public，Agent 层自建即可纯静态求 schema。
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
