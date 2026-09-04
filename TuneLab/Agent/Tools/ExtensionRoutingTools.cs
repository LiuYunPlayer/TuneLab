using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Extensions;
using TuneLab.Commands;

namespace TuneLab.Agent;

// 扩展冲突消解（「Extension Routing」）的【写】那一半，尚未搬进命令面（读那一半已是
// `extension routing`，见 TuneLab/Commands/Handlers/ExtensionCommands.cs）。判据全来自
// ExtensionRouting（冲突行 / 活实现解析 / 用户选择存取），这里不复制任何一份。

// 为某个冲突身份选定提供包（或清除回默认规则）。存进 app 设置的 ExtensionRouting 映射、即时落盘，但**要重启才生效**
// （工程只引身份 id，解析发生在加载期）。改用户的应用配置 → 过授权闸门。
internal sealed class SetExtensionRoutingTool(Func<AuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm = null) : IAgentTool
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
            new AuthorizationRequest(WriteKind.RoutingChange, 0, plan.RouteLabel, plan.TargetLabel), confirm, cancellationToken);
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
