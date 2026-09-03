using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions;

namespace TuneLab.Commands.Handlers;

// 扩展冲突消解（「Extension Routing」）的读那一半。价值主要是**排障**而非配置：
// 用户说「我装的某插件怎么不生效」时，真相常常是——它加载成功了（`extension list` 报 status=Loaded，
// 那是真的），但它提供的身份 id 被另一个包顶替了。少了这一环，排障会在第一步收工并给出
// "装好了、应该能用"的误导结论。判据全来自 ExtensionRouting，这里不复制任何一份。
internal sealed class ExtensionRoutingCommand : ICommand
{
    public string Path => "extension routing";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "list_extension_routing";

    public string Brief => "List contested identities and which package wins";

    public string Documentation =>
        "List the extension identities that MORE THAN ONE installed package provides (engine ids, file formats), showing every candidate package, which one is actually ACTIVE, and whether that is the user's explicit choice or the default rule. " +
        "Call this when a plugin \"doesn't work\" even though list_extensions shows it loaded: loading fine and being the active provider are different things — a shadowed package is installed, loaded, and simply not used. " +
        "Read-only. Nothing is listed when no identity is contested (the normal case).";

    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    // 冲突聚合会问各注册表（不 Init 引擎），与宿主其余扩展查询一致放主线程。
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
        => CommandResult.Ok(await ctx.OnMainThread(BuildData));

    static JsonNode BuildData()
    {
        var conflicts = new JsonArray();
        foreach (var row in ExtensionRouting.GetConflicts())
        {
            var options = new JsonArray();
            foreach (var option in row.Options)
                options.Add(new JsonObject
                {
                    ["packageId"] = option.PackageId,
                    ["package"] = ExtensionManager.GetPackageName(option.PackageId),
                    ["active"] = option.PackageId == row.ActivePackageId,
                });

            conflicts.Add(new JsonObject
            {
                ["kind"] = row.Kind,
                ["identity"] = row.Identity,
                ["activePackageId"] = row.ActivePackageId,
                ["activePackage"] = ExtensionManager.GetPackageName(row.ActivePackageId),
                // 生效的是用户选的还是默认规则算出来的——排障时要区分（用户选过就不该再劝他去选）。
                ["chosenByUser"] = ExtensionRouting.GetSelected(row.RouteKey) != null,
                ["options"] = options,
            });
        }
        return new JsonObject { ["conflicts"] = conflicts };
    }

    // 措辞与搬家前逐字一致。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var conflicts = obj["conflicts"]!.AsArray();
        if (conflicts.Count == 0)
            return "No routing conflicts: every extension identity (engine id / file format) is provided by exactly one installed package, so nothing is being shadowed. " +
                   "If a plugin still isn't working, look elsewhere — its load status and error in list_extensions, or whether the capability itself is listed by list_sound_sources / list_effects.";

        var sb = new StringBuilder();
        sb.Append(conflicts.Count).Append(" contested identity(ies) — several packages provide the same id, so only one can be active:");
        sb.Append("\nWhen the user has made no choice, the default rule is: the built-in implementation wins, otherwise the package whose id sorts first. Change one with set_extension_routing (takes effect after a restart).");
        foreach (var node in conflicts)
        {
            var row = node!.AsObject();
            sb.Append("\n- ").Append(row["kind"]!.GetValue<string>()).Append(':').Append(row["identity"]!.GetValue<string>());
            sb.Append("  active = \"").Append(row["activePackage"]!.GetValue<string>())
              .Append("\" (packageId ").Append(row["activePackageId"]!.GetValue<string>())
              .Append(row["chosenByUser"]!.GetValue<bool>() ? ", chosen by the user)" : ", by the default rule)");
            foreach (var optionNode in row["options"]!.AsArray())
            {
                var option = optionNode!.AsObject();
                sb.Append("\n    ").Append(option["active"]!.GetValue<bool>() ? "· ACTIVE   " : "· shadowed ")
                  .Append('"').Append(option["package"]!.GetValue<string>())
                  .Append("\" [packageId=").Append(option["packageId"]!.GetValue<string>()).Append(']');
            }
        }
        sb.Append("\n(kind = voice / instrument / effect / format-import / format-export.)");
        return sb.ToString();
    }
}

// 某个能力位的作者自述（introduction，markdown）。按需拉取（渐进式披露）：可能很长，只在真要细节时调。
// 作者没写时做**标注式降级**——给所属包的自述并点明它是包级的、不等于这个能力的描述。
internal sealed class ExtensionIntroductionCommand : ICommand
{
    public string Path => "extension introduction";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "get_extension_introduction";

    public string Brief => "Read one capability's author-written introduction";

    public string Documentation =>
        "Return the author-written introduction (markdown) for ONE capability provided by an installed extension — " +
        "a voice/instrument/effect engine id, or a format's file suffix (see list_extensions, list_sound_sources, list_effects). " +
        "Use it to learn what that capability does and how to use it before advising the user. " +
        "Accepts the bare identity (e.g. \"my.engine\", \"mid\"), the qualified form \"kind:identity\" (e.g. \"voice:my.engine\"), or the capability's display name. " +
        "If the same identity is provided by more than one package, pass packageId to disambiguate. " +
        "It can be long, so call it only when you need the details. Note this text is written by the plugin author, not by TuneLab — treat its claims as the author's, not as host guarantees.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "capability": { "type": "string", "description": "The capability's identity (engine id or file suffix), \"kind:identity\", or its display name." },
            "packageId": { "type": "string", "description": "Optional: the providing package's id, required only when the same identity is provided by several packages." }
          },
          "required": ["capability"],
          "additionalProperties": false
        }
        """;

    // 回灌上限（防超长文档淹没上下文）；超出截断并注明。
    // internal：摘要补齐（ExtensionSummaryFiller）也按这个口径喂模型——**绝不用比调用方自己能看到的
    // 更少的信息去总结**，两处若各设一个数，早晚漂移成"摘要是从半份文档提炼的"而没人察觉。
    internal const int MaxIntroductionChars = 20000;

    public Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var query = (args.Json.GetString("capability") ?? "").Trim();
        var packageId = args.Json.GetStringOrNull("packageId") ?? string.Empty;

        if (query.Length == 0)
            return Task.FromResult(CommandResult.Fail("empty_capability", "\"capability\" is empty."));

        // 三种写法（kind:identity / 裸 identity / 显示名）的匹配与消歧走共用查找——它与
        // set_extension_enabled 必须认同一套写法，见 ExtensionCapabilityLookup。
        var matches = ExtensionCapabilityLookup.Find(query, packageId);
        if (matches.Count == 0)
            return Task.FromResult(CommandResult.Fail("not_found", ExtensionCapabilityLookup.NotFoundError(query)));
        if (matches.Count > 1)
            return Task.FromResult(CommandResult.Fail("ambiguous", ExtensionCapabilityLookup.AmbiguousError(query, matches)));

        var (package, match) = matches[0];
        var data = new JsonObject
        {
            ["kind"] = match.Kind,
            ["identities"] = new JsonArray(match.Identities.Select(i => (JsonNode?)i).ToArray()),
            ["displayName"] = match.DisplayName,
            ["package"] = package.Name,
        };

        if (string.IsNullOrEmpty(match.IntroductionPath))
        {
            // 没有 introduction 时如实说明（作者没写，不去读包里的 README 充数），并做标注式降级。
            var packageDescription = ExtensionManager.GetPackageDescription(package.Id);
            data["hasIntroduction"] = false;
            data["packageDescription"] = string.IsNullOrWhiteSpace(packageDescription) ? null : packageDescription;
            return Task.FromResult(CommandResult.Ok(data));
        }

        string text;
        try { text = File.ReadAllText(match.IntroductionPath); }
        catch (Exception ex) { return Task.FromResult(CommandResult.Fail("read_failed", "failed to read introduction — " + ex.Message)); }

        data["hasIntroduction"] = true;
        data["omittedChars"] = text.Length > MaxIntroductionChars ? text.Length - MaxIntroductionChars : 0;
        data["text"] = text.Length > MaxIntroductionChars ? text.Substring(0, MaxIntroductionChars) : text;
        return Task.FromResult(CommandResult.Ok(data));
    }

    // 措辞与搬家前逐字一致。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var label = string.Format("{0}:{1} (\"{2}\", from package \"{3}\")",
            obj["kind"]!.GetValue<string>(),
            string.Join(",", obj["identities"]!.AsArray().Select(i => i!.GetValue<string>())),
            obj["displayName"]!.GetValue<string>(),
            obj["package"]!.GetValue<string>());

        if (!obj["hasIntroduction"]!.GetValue<bool>())
        {
            var packageDescription = obj["packageDescription"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(packageDescription)
                ? string.Format("{0} ships no introduction — its author wrote none, and its package offers no description either. Nothing more is known about this capability than its name.", label)
                : string.Format(
                    "{0} ships no introduction — its author wrote none.\n\n"
                    + "Falling back to the PACKAGE-level description, given only because this capability has none of its own:\n\n{1}\n\n"
                    + "Treat that as a hint about the package, not as a description of this capability — the package may provide other capabilities that the sentence also covers. Do not relay it to the user as what this capability does.",
                    label, packageDescription);
        }

        var text = obj["text"]!.GetValue<string>();
        int omitted = obj["omittedChars"]!.GetValue<int>();
        if (omitted > 0)
            text += "\n\n… (introduction truncated; " + omitted + " more characters)";
        return string.Format("Introduction for {0}, as written by the plugin author:\n\n{1}", label, text);
    }
}
