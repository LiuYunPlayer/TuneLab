using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Extensions;

namespace TuneLab.Agent;

// 扩展冲突消解（「Extension Routing」）的 agent 面。价值主要是**排障**而非配置：
// 用户说「我装的某插件怎么不生效」时，真相常常是——它加载成功了（list_extensions 报 status=Loaded，那是真的），
// 但它提供的身份 id 被另一个包顶替了。少了这一环，agent 会在第一步收工并给出"装好了、应该能用"的误导结论。
// 判据全来自 ExtensionRouting（冲突行 / 活实现解析 / 用户选择存取），这里不复制任何一份。
//  · list_extension_routing 只读：列全部冲突身份 + 各候选包 + 当前生效 + 是"用户选定"还是"默认规则"；
//  · set_extension_routing  写：为某身份选包（或清除回默认），过 ToolAuthorization 闸门，**改动重启后生效**。

internal sealed class ListExtensionRoutingTool : IAgentTool
{
    public string Name => "list_extension_routing";

    public string Description =>
        "List the extension identities that MORE THAN ONE installed package provides (engine ids, file formats), showing every candidate package, which one is actually ACTIVE, and whether that is the user's explicit choice or the default rule. " +
        "Call this when a plugin \"doesn't work\" even though list_extensions shows it loaded: loading fine and being the active provider are different things — a shadowed package is installed, loaded, and simply not used. " +
        "Read-only. Nothing is listed when no identity is contested (the normal case).";

    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
        // 冲突聚合会问各注册表（不 Init 引擎），与宿主其余扩展查询一致放 UI 线程。
        => await Dispatcher.UIThread.InvokeAsync(Describe);

    static string Describe()
    {
        var rows = ExtensionRouting.GetConflicts();
        if (rows.Count == 0)
            return "No routing conflicts: every extension identity (engine id / file format) is provided by exactly one installed package, so nothing is being shadowed. " +
                   "If a plugin still isn't working, look elsewhere — its load status and error in list_extensions, or whether the capability itself is listed by list_sound_sources / list_effects.";

        var sb = new StringBuilder();
        sb.Append(rows.Count).Append(" contested identity(ies) — several packages provide the same id, so only one can be active:");
        sb.Append("\nWhen the user has made no choice, the default rule is: the built-in implementation wins, otherwise the package whose id sorts first. Change one with set_extension_routing (takes effect after a restart).");
        foreach (var row in rows)
        {
            sb.Append("\n- ").Append(row.Kind).Append(':').Append(row.Identity);
            bool chosen = ExtensionRouting.GetSelected(row.RouteKey) != null;
            sb.Append("  active = \"").Append(ExtensionManager.GetPackageName(row.ActivePackageId))
              .Append("\" (packageId ").Append(row.ActivePackageId).Append(chosen ? ", chosen by the user)" : ", by the default rule)");
            foreach (var option in row.Options)
            {
                sb.Append("\n    ").Append(option.PackageId == row.ActivePackageId ? "· ACTIVE   " : "· shadowed ")
                  .Append('"').Append(ExtensionManager.GetPackageName(option.PackageId)).Append("\" [packageId=").Append(option.PackageId).Append(']');
            }
        }
        sb.Append("\n(kind = voice / instrument / effect / format-import / format-export.)");
        return sb.ToString();
    }
}

// 为某个冲突身份选定提供包（或清除回默认规则）。存进 app 设置的 ExtensionRouting 映射、即时落盘，但**要重启才生效**
// （工程只引身份 id，解析发生在加载期）。改用户的应用配置 → 过授权闸门。
internal sealed class SetExtensionRoutingTool(Func<AgentAuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm = null) : IAgentTool
{
    public string Name => "set_extension_routing";

    public string Description =>
        "Choose WHICH installed package provides a contested extension identity (see list_extension_routing for the identities and candidate packageIds) — i.e. un-shadow the package the user actually wants. " +
        "Omit `packageId` (or pass \"\") to clear the choice and fall back to the default rule. " +
        "The choice is saved immediately but only takes effect after TuneLab restarts, so always tell the user to restart. Needs the user's authorization; if refused, point them at the Settings window's \"Extension Routing\" page.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "kind": { "type": "string", "description": "Identity kind exactly as listed: voice / instrument / effect / format-import / format-export." },
            "identity": { "type": "string", "description": "The contested identity id (engine type id, or file extension for formats), as listed by list_extension_routing." },
            "packageId": { "type": "string", "description": "The packageId to use, exactly as listed for that identity. Empty/omitted = clear the choice and use the default rule." }
          },
          "required": ["kind", "identity"],
          "additionalProperties": false
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string kind, identity;
        string? packageId;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            kind = doc.RootElement.GetString("kind");
            identity = doc.RootElement.GetString("identity");
            packageId = doc.RootElement.GetStringOrNull("packageId");
        }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        kind = (kind ?? "").Trim();
        identity = (identity ?? "").Trim();
        packageId = (packageId ?? "").Trim();

        var plan = await Dispatcher.UIThread.InvokeAsync(() => Plan(kind, identity, packageId));
        if (plan.Error != null)
            return plan.Error;
        if (plan.NoOp != null)
            return plan.NoOp;

        var (proceed, message) = await ToolAuthorization.AuthorizeAsync(
            new AgentAuthorizationRequest(AgentWriteKind.RoutingChange, 0, plan.RouteLabel, plan.TargetLabel), confirm, cancellationToken);
        if (!proceed)
            return message;

        await Dispatcher.UIThread.InvokeAsync(() => ExtensionRouting.SetSelected(plan.RouteKey!, plan.PackageId));
        return message + string.Format(
            "{0} for {1}. Saved, but it only takes effect after TuneLab restarts — tell the user to restart, then verify with list_extension_routing.",
            string.IsNullOrEmpty(plan.PackageId)
                ? string.Format("Cleared the package choice (back to the default rule, which currently resolves to \"{0}\")", plan.TargetLabel)
                : string.Format("Selected \"{0}\"", plan.TargetLabel),
            plan.RouteLabel);
    }

    readonly record struct RoutePlan(string? Error, string? NoOp, string? RouteKey, string RouteLabel, string? PackageId, string TargetLabel);

    static RoutePlan Plan(string kind, string identity, string packageId)
    {
        var rows = ExtensionRouting.GetConflicts();
        if (rows.Count == 0)
            return new RoutePlan("Error: no extension identity is contested right now, so there is nothing to route. Call list_extension_routing (and check list_extensions for load errors instead).", null, null, "", null, "");

        var row = rows.FirstOrDefault(r => string.Equals(r.Kind, kind, StringComparison.OrdinalIgnoreCase)
                                       && string.Equals(r.Identity, identity, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(row.RouteKey))
            return new RoutePlan(string.Format(
                "Error: \"{0}:{1}\" is not a contested identity (only contested ones can be routed). Call list_extension_routing to see the exact kind + identity pairs.", kind, identity),
                null, null, "", null, "");

        var routeLabel = row.Kind + ":" + row.Identity;

        // 清除选择 → 回默认规则；已无选择则什么都不做。
        if (packageId.Length == 0)
        {
            if (ExtensionRouting.GetSelected(row.RouteKey) == null)
                return new RoutePlan(null, string.Format("\"{0}\" already has no explicit choice (it uses the default rule, currently \"{1}\"). Nothing changed.",
                    routeLabel, ExtensionManager.GetPackageName(row.ActivePackageId)), null, routeLabel, null, "");
            // 清除后的活实现按默认规则重算（内建优先，否则包 id 序最小）——如实告知会落到谁。
            // 传一个空 routeKey：注册表里的键恒为 "kind:identity"，空键必然无用户选择，故解析必走默认分支。
            var fallback = ExtensionRouting.ResolveActivePackageId("", row.Options.Select(o => o.PackageId).ToArray()) ?? "";
            return new RoutePlan(null, null, row.RouteKey, routeLabel, "", ExtensionManager.GetPackageName(fallback));
        }

        var option = row.Options.FirstOrDefault(o => string.Equals(o.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(option.PackageId))
            return new RoutePlan(string.Format(
                "Error: \"{0}\" does not provide \"{1}\". Its candidates are: {2}. (Use the packageId exactly as listed by list_extension_routing.)",
                packageId, routeLabel, string.Join(", ", row.Options.Select(o => o.PackageId))),
                null, null, "", null, "");

        if (ExtensionRouting.GetSelected(row.RouteKey) == option.PackageId)
            return new RoutePlan(null, string.Format("\"{0}\" is already set to \"{1}\". Nothing changed.", routeLabel, ExtensionManager.GetPackageName(option.PackageId)),
                null, routeLabel, null, "");

        return new RoutePlan(null, null, row.RouteKey, routeLabel, option.PackageId, ExtensionManager.GetPackageName(option.PackageId));
    }
}
