using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions;

namespace TuneLab.Commands.Handlers;

// `extension list`：枚举全部已装扩展 + 每条包级元数据 + 逐能力位（身份/一句话摘要/本次结局/冲突态）。
//
// 两层粒度各有其用，不可互相替代：**包**承载排障与管理事实（加载状态/sdk 门/版本/作者/卸载单位、
// routing 的选择值也是包 id），**能力位**才是调用方推荐与使用时真正引用的东西（引擎 id / 文件后缀）。
// 故这里按包一条、内嵌逐能力位行；而 `source list` / `effect list` 是纯能力位视角。
//
// 取事实前先把缺的摘要补齐（见 ExtensionSummaryFiller）：**短文档直接用作者原话、长文档才调一次模型**，
// 故对调用方而言 summary 就是能力位自带的属性，它感知不到生成过程、也没有对应的命令。
// 摘要只是索引；要作者原文仍走 `extension introduction`（渐进式披露的下一级）。
//
// 【没有模型的入口降级而非禁用（§5.3）】CLI / MCP / headless 的 ctx.SideModel 是 null：缓存命中就用、
// 短文档照样出原话，只有"长文档且没缓存"那部分留空——末尾如实说明**这个入口生成不了**，而不是沿用
// 侧栏那句"稍后再问一次"（在没有模型的入口里，再问一次也不会有）。枚举扩展是外部入口最常用的读之一，
// 不该因为补不了摘要就整条消失。
internal sealed class ExtensionListCommand : ICommand
{
    public string Path => "extension list";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "list_extensions";

    public string Brief => "List installed extensions and the capabilities they provide";

    public string Documentation =>
        "List the TuneLab extensions (plugins) the user has installed: each one's name, id, version, author, kind(s) " +
        "(format / voice / instrument / effect, or a resource type), load status, its package-level description, and — per capability it provides — " +
        "that capability's identity, whether it is DISABLED / failed to load, a one-line summary of what it does, and whether it is SHADOWED by another package. " +
        "Use to know what the user has installed and to guide them. The summaries are an index — for the author's full text on one capability, call get_extension_introduction. " +
        "Never claim a capability is available without checking these per-capability notes: an installed package can be switched off by the user (see set_extension_enabled), " +
        "and a single package can have one capability working and another one off or broken. " +
        "When troubleshooting \"plugin X doesn't work\": status=Loaded only means it loaded — check the shadowed note here (and list_extension_routing), then confirm the capability itself shows up in list_sound_sources / list_effects.";

    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var results = ExtensionManager.LoadResults;
        bool summarizerAvailable = ctx.SideModel != null;
        if (results.Count == 0)
            return CommandResult.Ok(Data(new JsonArray(), 0, summarizerAvailable));

        // 取事实前补齐缺的摘要。绝大多数条目走"短文档直接用原话"零成本；只有长文档才各花一次模型调用，
        // 且按内容哈希缓存 → 一份文档一辈子一次。补不完的如实回报在末尾。
        // 这一段【不上主线程】：它要读盘、可能发网络请求，占着主线程会把界面卡住。
        var paths = new List<string>();
        foreach (var package in results)
            foreach (var entry in package.Entries)
                if (!string.IsNullOrEmpty(entry.IntroductionPath))
                    paths.Add(entry.IntroductionPath!);
        var (_, missingSummaries) = await ExtensionSummaryFiller.FillAsync(ctx.SideModel, paths, cancellationToken);
        // 补齐之后读一次快照，下面逐条查（别对每个条目都读一遍盘——那既浪费，也会让同一份清单里
        // 前后几条读到文件的不同版本）。
        var summaries = ExtensionSummaryCache.Read();

        // 冲突聚合会问各注册表，与宿主其余扩展查询一致放主线程。
        return CommandResult.Ok(await ctx.OnMainThread(
            () => Data(Packages(results, summaries), missingSummaries, summarizerAvailable)));
    }

    static JsonNode Data(JsonArray packages, int missingSummaries, bool summarizerAvailable) => new JsonObject
    {
        ["packages"] = packages,
        ["missingSummaries"] = missingSummaries,
        // 摘要补不齐时，末尾那句话取决于【这个入口有没有模型】：有模型是"这次没成，待会儿再问"，
        // 没模型是"这里永远补不了，去读作者原文"。两句话的行动建议完全不同，故这个事实要进结果。
        ["summarizerAvailable"] = summarizerAvailable,
    };

    static JsonArray Packages(IReadOnlyList<ExtensionLoadResult> results, ExtensionSummaryCache.Snapshot summaries)
    {
        var packages = new JsonArray();
        foreach (var r in results)
        {
            var entries = new JsonArray();
            foreach (var e in r.Entries)
            {
                var routing = new JsonArray();
                foreach (var identity in e.Identities)
                    CollectEntryRouting(routing, r.Id, e.Kind, identity);

                var summary = summaries.Get(ExtensionSummaryCache.ContentKey(e.IntroductionPath));
                entries.Add(new JsonObject
                {
                    ["kind"] = string.IsNullOrEmpty(e.Kind) ? null : e.Kind,
                    ["identities"] = new JsonArray(e.Identities.Select(i => (JsonNode?)i).ToArray()),
                    ["displayName"] = string.IsNullOrEmpty(e.DisplayName) ? null : e.DisplayName,
                    ["status"] = e.Status.ToString(),
                    ["error"] = string.IsNullOrEmpty(e.Error) ? null : e.Error,
                    ["hasIntroduction"] = !string.IsNullOrEmpty(e.IntroductionPath),
                    // 摘要的出处照实分两种：短文档是作者原话，长文档是 TuneLab 的转述。
                    ["summary"] = summary == null ? null : new JsonObject
                    {
                        ["text"] = summary.Summary,
                        ["verbatim"] = summary.Verbatim,
                    },
                    ["routing"] = routing,
                });
            }

            // Legacy 包无 manifest 条目（能力靠盲扫发现），仍按包列出它参与的冲突。
            var packageRouting = new JsonArray();
            if (r.Entries.Count == 0)
                CollectPackageRouting(packageRouting, r.Id);

            packages.Add(new JsonObject
            {
                ["id"] = string.IsNullOrEmpty(r.Id) ? null : r.Id,
                ["name"] = r.Name,
                ["version"] = r.Version,
                ["generation"] = r.Generation.ToString(),
                ["status"] = r.Status.ToString(),
                ["types"] = new JsonArray(r.Types.Select(t => (JsonNode?)t).ToArray()),
                ["author"] = string.IsNullOrEmpty(r.Author) ? null : r.Author,
                ["description"] = string.IsNullOrEmpty(r.Description) ? null : r.Description,
                ["error"] = string.IsNullOrEmpty(r.Error) ? null : r.Error,
                ["entries"] = entries,
                ["routing"] = packageRouting,
            });
        }
        return packages;
    }

    // 某能力位的冲突事实。format 在 routing 里细分成 format-import / format-export 两条可路由身份，
    // 故按前缀认亲——一个 format 条目可能带出两条（导入与导出可各自选不同包）。
    static void CollectEntryRouting(JsonArray rows, string? packageId, string kind, string identity)
    {
        if (string.IsNullOrEmpty(packageId) || identity.Length == 0 || string.IsNullOrEmpty(kind))
            return;

        foreach (var row in ExtensionRouting.GetConflicts())
        {
            if (!string.Equals(row.Identity, identity, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!row.Kind.StartsWith(kind, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!row.Options.Any(o => o.PackageId == packageId))
                continue;
            rows.Add(RoutingRow(row, packageId));
        }
    }

    // 该包提供的身份里，凡与别的包撞车的都如实标出「本包是生效者还是被顶替者」。
    // 这是排障的关键一句：status=Loaded 是真的（确实加载了），但"被路由掉"是另一根轴——缺了它，
    // 调用方只会说"插件装好了、应该能用"。
    static void CollectPackageRouting(JsonArray rows, string? packageId)
    {
        if (string.IsNullOrEmpty(packageId))
            return;
        foreach (var row in ExtensionRouting.GetConflicts())
            if (row.Options.Any(o => o.PackageId == packageId))
                rows.Add(RoutingRow(row, packageId));
    }

    static JsonObject RoutingRow(ExtensionRouting.RouteRow row, string packageId) => new()
    {
        ["kind"] = row.Kind,
        ["identity"] = row.Identity,
        ["active"] = row.ActivePackageId == packageId,
        ["activePackage"] = ExtensionManager.GetPackageName(row.ActivePackageId),
        // 同一身份的其余提供者（本包除外）：生效时用来说"还有谁被顶替了"。
        ["otherPackages"] = new JsonArray(row.Options.Where(o => o.PackageId != packageId)
            .Select(o => (JsonNode?)ExtensionManager.GetPackageName(o.PackageId)).ToArray()),
    };

    // 措辞与搬家前逐字一致。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var packages = obj["packages"]!.AsArray();
        if (packages.Count == 0)
            return "No extensions are installed. TuneLab is running with only its built-in capabilities.";

        var sb = new StringBuilder();
        sb.Append(packages.Count).Append(" extension(s) installed:");
        foreach (var node in packages)
            AppendPackage(sb, node!.AsObject());

        int missing = obj["missingSummaries"]!.GetValue<int>();
        if (missing > 0)
        {
            // 补不完就如实说，别让调用方以为"没摘要 = 这插件没东西可说"。
            sb.Append("\n\nNote: ").Append(missing);
            sb.Append(obj["summarizerAvailable"]!.GetValue<bool>()
                ? " capability(ies) could not be summarized this time (a summarization request failed, or the time budget ran out). "
                  + "Their entries say so above. Everything else here is complete and correct — tell the user they can ask again in a moment to fill those in; the ones already done are cached and cost nothing."
                : " capability(ies) have no one-line summary yet, and this entry point has no model to write one (only the in-app assistant does), so asking again will not fill them in. "
                  + "Their entries say so above. Everything else here is complete and correct — read the author's own text for those with get_extension_introduction.");
        }
        return sb.ToString();
    }

    static void AppendPackage(StringBuilder sb, JsonObject package)
    {
        var id = package["id"]?.GetValue<string>() ?? "(legacy, no id)";
        var types = package["types"]!.AsArray();
        sb.Append("\n- \"").Append(package["name"]!.GetValue<string>()).Append("\" [id=").Append(id)
          .Append(", v").Append(package["version"]!.GetValue<string>())
          .Append(", ").Append(package["generation"]!.GetValue<string>())        // V1 / Legacy
          .Append(", status=").Append(package["status"]!.GetValue<string>())     // Loaded / PartiallyLoaded / Skipped / Failed
          .Append("]  kinds: ").Append(types.Count > 0 ? string.Join("/", types.Select(t => t!.GetValue<string>())) : "none");
        var author = package["author"]?.GetValue<string>();
        if (author != null)
            sb.Append("  by ").Append(author);
        // 整包被用户关掉：这不是故障，但后果是"装了 ≠ 能用"。不说清楚，模型会照着 kinds 那一行
        // 向用户保证一个本次运行根本没注册的能力。
        bool packageDisabled = package["status"]!.GetValue<string>() == nameof(ExtensionLoadStatus.Disabled);
        if (packageDisabled)
            sb.Append("\n    DISABLED by the user — installed but switched off, so NONE of its capabilities exist in this session."
                    + " Re-enable it in the Extensions sidebar or with set_extension_enabled (takes effect after a restart).");
        // 包级 description：讲【整个包】。各能力位自己的一句话在下面的 provides 行里，两者不互相顶替。
        var description = package["description"]?.GetValue<string>();
        if (description != null)
            sb.Append("\n    ").Append(description);
        var error = package["error"]?.GetValue<string>();
        if (error != null)
            sb.Append("\n    note: ").Append(error);

        var entries = package["entries"]!.AsArray();
        if (entries.Count > 0)
        {
            foreach (var entry in entries)
                AppendEntry(sb, entry!.AsObject(), packageDisabled);
        }
        else
        {
            foreach (var row in package["routing"]!.AsArray())
            {
                var r = row!.AsObject();
                sb.Append("\n    provides ").Append(r["kind"]!.GetValue<string>()).Append(':').Append(r["identity"]!.GetValue<string>());
                sb.Append(r["active"]!.GetValue<bool>()
                    ? " — ACTIVE (also provided by " + OtherPackages(r) + ", which is/are shadowed)."
                    : " — SHADOWED: \"" + r["activePackage"]!.GetValue<string>()
                      + "\" provides it instead, so THIS package's implementation is loaded but never used. See list_extension_routing / set_extension_routing.");
            }
        }
    }

    // 逐能力位：身份 + 显示名 + 作者写的一句话摘要 + 是否有 introduction 可拉，冲突注记挂在各自名下。
    static void AppendEntry(StringBuilder sb, JsonObject entry, bool packageDisabled)
    {
        var kind = entry["kind"]?.GetValue<string>();
        var identities = entry["identities"]!.AsArray();
        sb.Append("\n    provides ").Append(kind ?? "(no type)");
        // 一个条目可占多个能力位（format 的后缀别名共用一份实现与说明），如实列全。
        if (identities.Count > 0)
            sb.Append(':').Append(string.Join(",", identities.Select(i => i!.GetValue<string>())));
        var displayName = entry["displayName"]?.GetValue<string>();
        if (displayName != null)
            sb.Append(" \"").Append(displayName).Append('"');
        AppendEntryStatus(sb, entry, packageDisabled);
        if (entry["hasIntroduction"]!.GetValue<bool>() && identities.Count > 0)
            sb.Append("  [full text: get_extension_introduction(\"")
              .Append(kind).Append(':').Append(identities[0]!.GetValue<string>()).Append("\")]");

        // 一句话摘要。**出处照实分两种**：短文档直接用了作者原话，长文档才是 TuneLab 的转述——
        // 后者不该被当成作者的官方说法转述给用户。
        if (entry["summary"] is JsonObject summary)
        {
            sb.Append(summary["verbatim"]!.GetValue<bool>()
                ? "\n        (author's own words)"
                : "\n        (TuneLab's condensation of the author's introduction, not their wording)");
            AppendIndented(sb, summary["text"]!.GetValue<string>());
        }
        else if (entry["hasIntroduction"]!.GetValue<bool>())
            sb.Append("\n        (not summarized yet — see the note at the end)");

        foreach (var row in entry["routing"]!.AsArray())
        {
            var r = row!.AsObject();
            sb.Append("\n        ").Append(r["kind"]!.GetValue<string>()).Append(": ");
            sb.Append(r["active"]!.GetValue<bool>()
                ? "ACTIVE (also provided by " + OtherPackages(r) + ", which is/are shadowed)."
                : "SHADOWED: \"" + r["activePackage"]!.GetValue<string>()
                  + "\" provides it instead, so THIS package's implementation is loaded but never used. See list_extension_routing / set_extension_routing.");
        }
    }

    static string OtherPackages(JsonObject row)
        => string.Join(", ", row["otherPackages"]!.AsArray().Select(p => "\"" + p!.GetValue<string>() + "\""));

    // 摘要正文按行缩进后附上。**刻意不把换行拍平**：作者（或模型）用列表/表格分点列出的关键信息，
    // 那结构本身就是信息——拍成一行会丢掉"这是几个并列项"、以及表格里名与值的对应。缩进既留住结构，
    // 又让"一条摘要到哪儿结束"在这份逐条目清单里保持清楚。
    static void AppendIndented(StringBuilder sb, string text)
    {
        foreach (var line in text.Split('\n'))
            sb.Append("\n          ").Append(line.TrimEnd());
    }

    // 单个条目的结局注记（接在 provides 行末）。Registered 不写——正常态无需噪音。
    static void AppendEntryStatus(StringBuilder sb, JsonObject entry, bool packageDisabled)
    {
        if (!Enum.TryParse<ExtensionEntryStatus>(entry["status"]!.GetValue<string>(), out var status))
            return;
        var error = entry["error"]?.GetValue<string>();
        switch (status)
        {
            case ExtensionEntryStatus.Disabled:
                // 逐条目结局：包级 status 是汇总，一个包完全可以"一个能力好好的、另一个被关掉或坏了"。
                // 整包被禁时逐条目也标一遍——调用方常只读到自己关心的那一行。
                sb.Append(packageDisabled
                    ? "  [NOT AVAILABLE: the whole package is disabled]"
                    : "  [DISABLED by the user: this capability alone is switched off (the rest of the package still works). Re-enable with set_extension_enabled; needs a restart]");
                break;
            case ExtensionEntryStatus.Failed:
                sb.Append("  [FAILED to load: ").Append(error ?? "unknown error").Append(" — this capability does NOT exist in this session]");
                break;
            case ExtensionEntryStatus.Skipped:
                sb.Append("  [SKIPPED: ").Append(error ?? "unknown reason").Append(" — this capability does NOT exist in this session]");
                break;
        }
    }
}

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
