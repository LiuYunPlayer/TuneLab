using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Extensions;

namespace TuneLab.Agent;

// 扩展启停的 agent 写面（读面在 list_extensions：包级 DISABLED 与逐能力位的 [DISABLED]/[FAILED] 注记）。
// 与 set_extension_routing 对称：同属"改用户的应用配置、即时落盘、重启后生效"，故复用同一授权闸门与话术。
// 两者答的不是一个问题——routing 在多个实现里挑一个，启停决定某份实现要不要参与加载（独苗同样适用）。
// 判据全来自 ExtensionActivation 与 ExtensionManager.LoadResults，这里不复制任何一份状态。
internal sealed class SetExtensionEnabledTool(Func<AgentAuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm = null) : IAgentTool
{
    public string Name => "set_extension_enabled";

    public string Description =>
        "Turn an installed extension — or ONE capability inside it — on or off, without uninstalling anything. " +
        "Use it when a plugin misbehaves, is slow to load, or the user simply wants it out of the way but kept installed. " +
        "Omit `capability` to switch the whole package; pass it (\"kind:identity\" as shown by list_extensions, e.g. \"voice:my.engine\" or \"format:mid\") to switch just that one capability and leave the rest of the package working. " +
        "Disabling means the capability is not registered at all next launch: anything referring to it (a project using that voice, a file of that format) will stop resolving — say so before you do it. " +
        "The choice is saved immediately but only takes effect after TuneLab restarts, so always tell the user to restart. " +
        "Needs the user's authorization; if refused, point them at the Extensions sidebar: opening a package's detail window shows the same switches — one for the package in its header, one per capability on that capability's tab.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "packageId": { "type": "string", "description": "The installed package's id exactly as listed by list_extensions (legacy packages use their folder name)." },
            "enabled": { "type": "boolean", "description": "true = enable, false = disable." },
            "capability": { "type": "string", "description": "Optional: \"kind:identity\" of ONE capability in that package (e.g. \"voice:my.engine\"). A bare identity or the capability's display name is accepted too. Omit to switch the whole package." }
          },
          "required": ["packageId", "enabled"],
          "additionalProperties": false
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string packageId, capability;
        bool enabled;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            packageId = doc.RootElement.GetString("packageId");
            // 缺省/无法判读一律当"没说清"报错——启停是二选一的破坏性动作，猜一个默认值不可接受。
            enabled = doc.RootElement.GetBoolOrNull("enabled")
                ?? throw new ArgumentException("\"enabled\" must be true or false.");
            capability = doc.RootElement.GetStringOrNull("capability") ?? string.Empty;
        }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        packageId = (packageId ?? "").Trim();
        capability = (capability ?? "").Trim();

        // 查询 LoadResults 与注册表侧的状态一律回 UI 线程（与其余扩展类工具一致）。
        var plan = await Dispatcher.UIThread.InvokeAsync(() => Plan(packageId, capability, enabled));
        if (plan.Error != null)
            return plan.Error;
        if (plan.NoOp != null)
            return plan.NoOp;

        var (proceed, message) = await ToolAuthorization.AuthorizeAsync(
            new AgentAuthorizationRequest(AgentWriteKind.ExtensionActivationChange, 0,
                plan.Target, enabled ? "enable" : "disable", plan.SecondaryTarget), confirm, cancellationToken);
        if (!proceed)
            return message;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (plan.EntryKind == null)
                ExtensionActivation.SetPackageEnabled(packageId, enabled);
            else
                ExtensionActivation.SetEntryEnabled(packageId, plan.EntryKind, plan.Identities, enabled);
        });

        return message + string.Format(
            "{0} {1}. Saved, but it only takes effect after TuneLab restarts — tell the user to restart, then verify with list_extensions.{2}",
            enabled ? "Enabled" : "Disabled",
            plan.EntryKind == null
                ? string.Format("the extension \"{0}\"", plan.Target)
                : string.Format("the \"{0}\" capability of \"{1}\"", plan.Target, plan.SecondaryTarget),
            enabled ? "" : " Until then it stays loaded and usable this session.");
    }

    // EntryKind=null 表示整包；否则是条目级（Identities 为该条目的全部身份，多后缀 format 一并写入）。
    readonly record struct ActivationPlan(string? Error, string? NoOp, string Target, string? SecondaryTarget, string? EntryKind, IReadOnlyList<string> Identities);

    static ActivationPlan Plan(string packageId, string capability, bool enabled)
    {
        if (packageId.Length == 0)
            return Fail("Error: \"packageId\" is empty. Call list_extensions and use the id exactly as listed.");

        var package = ExtensionManager.LoadResults.FirstOrDefault(r => string.Equals(r.Id, packageId, StringComparison.OrdinalIgnoreCase));
        if (package == null)
        {
            var known = ExtensionManager.LoadResults.Where(r => !string.IsNullOrEmpty(r.Id)).Select(r => r.Id).ToArray();
            return Fail(string.Format("Error: no installed package has id \"{0}\". Installed ids: {1}.",
                packageId, known.Length == 0 ? "(none)" : string.Join(", ", known)));
        }

        // ── 整包 ──
        if (capability.Length == 0)
        {
            if (ExtensionActivation.IsPackageDisabled(package.Id) == !enabled)
                return NoChange(string.Format("The extension \"{0}\" is already {1}. Nothing changed.", package.Name, enabled ? "enabled" : "disabled"));
            return new ActivationPlan(null, null, package.Name, null, null, []);
        }

        // ── 包内某个条目 ──"kind:identity" / 裸身份 / 显示名 三种写法都认；匹配规则与
        // get_extension_introduction 共用一份（见 ExtensionCapabilityLookup）。
        // 这里已按 packageId 锁定了包，故只需在包内消歧。
        var matches = ExtensionCapabilityLookup.Find(capability, package.Id ?? string.Empty);
        if (matches.Count == 0)
        {
            var provided = package.Entries.Where(e => e.Identities.Count > 0)
                .Select(e => e.Kind + ":" + string.Join(",", e.Identities)).ToArray();
            return Fail(string.Format("Error: \"{0}\" is not a capability of \"{1}\". It provides: {2}.",
                capability, package.Name, provided.Length == 0 ? "(nothing switchable)" : string.Join(", ", provided)));
        }
        if (matches.Count > 1)
            return Fail(string.Format("Error: \"{0}\" matches {1} capabilities of \"{2}\" ({3}). Use the exact \"kind:identity\" form.",
                capability, matches.Count, package.Name, string.Join(", ", matches.Select(m => m.Label))));

        var entry = matches[0].Entry;
        if (!ExtensionActivation.CanDisableEntry(package.Id, entry.Kind, entry.Identities))
            return Fail(string.Format("Error: \"{0}\" has no switchable identity (resource entries are not registered individually). Switch the whole package instead: call again without \"capability\".", capability));

        var label = entry.Kind + ":" + string.Join(",", entry.Identities);

        // 整包已关时，单个能力的开关无从谈起——如实说清该先开整包，而不是写下一个看不出效果的选择。
        if (ExtensionActivation.IsPackageDisabled(package.Id))
            return enabled
                ? NoChange(string.Format("The whole extension \"{0}\" is disabled, so \"{1}\" cannot be enabled on its own. Enable the package first (call again without \"capability\").", package.Name, label))
                : NoChange(string.Format("\"{0}\" is already off, because the whole extension \"{1}\" is disabled. Nothing changed.", label, package.Name));

        if (ExtensionActivation.IsEntryDisabledSelf(package.Id, entry.Kind, entry.Identities) == !enabled)
            return NoChange(string.Format("\"{0}\" of \"{1}\" is already {2}. Nothing changed.", label, package.Name, enabled ? "enabled" : "disabled"));

        return new ActivationPlan(null, null, label, package.Name, entry.Kind, entry.Identities);
    }

    static ActivationPlan Fail(string error) => new(error, null, "", null, null, []);
    static ActivationPlan NoChange(string message) => new(null, message, "", null, null, []);
}
