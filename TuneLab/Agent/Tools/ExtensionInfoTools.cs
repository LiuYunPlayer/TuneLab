using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions;

namespace TuneLab.Agent;

// 环境感知（只读）——插件/扩展目录。让 agent 知道用户装了哪些扩展（格式/声库/乐器/效果/模型适配），
// 以据此指导用户、判断某能力是否可用。直接读宿主 ExtensionManager 的结构化加载结果，不经门面；
// introduction 因可能很长，作按需拉取的独立工具（渐进式披露，同 get_script_api 哲学）。
//
// 两层粒度各有其用，不可互相替代：**包**承载排障与管理事实（加载状态/sdk 门/版本/作者/卸载单位、
// routing 的选择值也是包 id），**能力位**才是 agent 推荐与使用时真正引用的东西（引擎 id / 文件后缀）。
// 故 list_extensions 按包一条、内嵌逐能力位行；而 list_sound_sources / list_effects 是纯能力位视角。

// list_extensions：枚举全部已装扩展 + 每条包级元数据 + 逐能力位（身份/一句话摘要/本次结局/冲突态）。
// 渲染前先把缺的摘要补齐（见 ExtensionSummaryFiller）：**短文档直接用作者原话、长文档才调一次模型**，
// 故对 agent 而言 summary 就是能力位自带的属性，它感知不到生成过程、也没有对应的工具。
// 摘要只是索引；要作者原文仍走 get_extension_introduction（渐进式披露的下一级）。
internal sealed class ListExtensionsTool(ExtensionSummaryFiller.Summarizer? summarize = null) : IAgentTool
{
    public string Name => "list_extensions";

    public string Description =>
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

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var results = ExtensionManager.LoadResults;
        if (results.Count == 0)
            return "No extensions are installed. TuneLab is running with only its built-in capabilities.";

        // 渲染前补齐缺的摘要。绝大多数条目走"短文档直接用原话"零成本；只有长文档才各花一次模型调用，
        // 且按内容哈希缓存 → 一份文档一辈子一次。补不完的如实回报在末尾，让模型转告用户稍后再问一次。
        var paths = new List<string>();
        foreach (var package in results)
            foreach (var entry in package.Entries)
                if (!string.IsNullOrEmpty(entry.IntroductionPath))
                    paths.Add(entry.IntroductionPath!);
        var (_, missingSummaries) = await ExtensionSummaryFiller.FillAsync(summarize, paths, cancellationToken);
        // 补齐之后读一次快照，下面逐条查（别对每个条目都读一遍盘——那既浪费，也会让同一份清单里
        // 前后几条读到文件的不同版本）。
        var summaries = ExtensionSummaryCache.Read();

        var sb = new StringBuilder();
        sb.Append(results.Count).Append(" extension(s) installed:");
        foreach (var r in results)
        {
            var id = string.IsNullOrEmpty(r.Id) ? "(legacy, no id)" : r.Id;
            var types = r.Types.Count > 0 ? string.Join("/", r.Types) : "none";
            sb.Append("\n- \"").Append(r.Name).Append("\" [id=").Append(id)
              .Append(", v").Append(r.Version)
              .Append(", ").Append(r.Generation)          // V1 / Legacy
              .Append(", status=").Append(r.Status)        // Loaded / PartiallyLoaded / Skipped / Failed
              .Append("]  kinds: ").Append(types);
            if (!string.IsNullOrEmpty(r.Author))
                sb.Append("  by ").Append(r.Author);
            // 整包被用户关掉：这不是故障，但后果是"装了 ≠ 能用"。不说清楚，模型会照着 kinds 那一行
            // 向用户保证一个本次运行根本没注册的能力。
            if (r.Status == ExtensionLoadStatus.Disabled)
                sb.Append("\n    DISABLED by the user — installed but switched off, so NONE of its capabilities exist in this session."
                        + " Re-enable it in the Extensions sidebar or with set_extension_enabled (takes effect after a restart).");
            // 包级 description：讲【整个包】。各能力位自己的一句话在下面的 provides 行里，两者不互相顶替。
            if (!string.IsNullOrEmpty(r.Description))
                sb.Append("\n    ").Append(r.Description);
            if (!string.IsNullOrEmpty(r.Error))
                sb.Append("\n    note: ").Append(r.Error);

            if (r.Entries.Count > 0)
            {
                // 逐能力位：身份 + 显示名 + 作者写的一句话摘要 + 是否有 introduction 可拉，冲突注记挂在各自名下。
                foreach (var e in r.Entries)
                {
                    sb.Append("\n    provides ").Append(string.IsNullOrEmpty(e.Kind) ? "(no type)" : e.Kind);
                    // 一个条目可占多个能力位（format 的后缀别名共用一份实现与说明），如实列全。
                    if (e.Identities.Count > 0)
                        sb.Append(':').Append(string.Join(",", e.Identities));
                    if (!string.IsNullOrEmpty(e.DisplayName))
                        sb.Append(" \"").Append(e.DisplayName).Append('"');
                    // 逐条目结局：包级 status 是汇总，一个包完全可以"一个能力好好的、另一个被关掉或坏了"。
                    // 整包被禁时逐条目也标一遍——模型常只读到自己关心的那一行。
                    AppendEntryStatus(sb, e, r.Status == ExtensionLoadStatus.Disabled);
                    if (!string.IsNullOrEmpty(e.IntroductionPath) && e.Identities.Count > 0)
                        sb.Append("  [full text: get_extension_introduction(\"")
                          .Append(e.Kind).Append(':').Append(e.Identities[0]).Append("\")]");
                    // 一句话摘要。**出处照实分两种**：短文档直接用了作者原话，长文档才是 TuneLab 的转述——
                    // 后者不该被当成作者的官方说法转述给用户。
                    var summary = summaries.Get(ExtensionSummaryCache.ContentKey(e.IntroductionPath));
                    if (summary != null)
                    {
                        sb.Append(summary.Verbatim
                            ? "\n        (author's own words)"
                            : "\n        (TuneLab's condensation of the author's introduction, not their wording)");
                        AppendIndented(sb, summary.Summary);
                    }
                    else if (!string.IsNullOrEmpty(e.IntroductionPath))
                        sb.Append("\n        (not summarized yet — see the note at the end)");
                    foreach (var identity in e.Identities)
                        AppendEntryRouting(sb, r.Id, e.Kind, identity);
                }
            }
            else
            {
                // Legacy 包无 manifest 条目（能力靠盲扫发现），仍按包列出它参与的冲突。
                AppendRouting(sb, r.Id);
            }
        }
        // 补不完就如实说，别让模型以为"没摘要 = 这插件没东西可说"。
        if (missingSummaries > 0)
            sb.Append("\n\nNote: ").Append(missingSummaries)
              .Append(" capability(ies) could not be summarized this time (a summarization request failed, or the time budget ran out). ")
              .Append("Their entries say so above. Everything else here is complete and correct — tell the user they can ask again in a moment to fill those in; the ones already done are cached and cost nothing.");
        return sb.ToString();
    }

    // 摘要正文按行缩进后附上。**刻意不把换行拍平**：作者（或模型）用列表/表格分点列出的关键信息，
    // 那结构本身就是信息——拍成一行会丢掉"这是几个并列项"、以及表格里名与值的对应。缩进既留住结构，
    // 又让"一条摘要到哪儿结束"在这份逐条目清单里保持清楚。
    static void AppendIndented(StringBuilder sb, string text)
    {
        foreach (var line in text.Split('\n'))
            sb.Append("\n          ").Append(line.TrimEnd());
    }

    // 单个条目的结局注记（接在 provides 行末）。Registered 不写——正常态无需噪音。
    static void AppendEntryStatus(StringBuilder sb, ExtensionEntryInfo entry, bool packageDisabled)
    {
        switch (entry.Status)
        {
            case ExtensionEntryStatus.Disabled:
                sb.Append(packageDisabled
                    ? "  [NOT AVAILABLE: the whole package is disabled]"
                    : "  [DISABLED by the user: this capability alone is switched off (the rest of the package still works). Re-enable with set_extension_enabled; needs a restart]");
                break;
            case ExtensionEntryStatus.Failed:
                sb.Append("  [FAILED to load: ").Append(entry.Error ?? "unknown error").Append(" — this capability does NOT exist in this session]");
                break;
            case ExtensionEntryStatus.Skipped:
                sb.Append("  [SKIPPED: ").Append(entry.Error ?? "unknown reason").Append(" — this capability does NOT exist in this session]");
                break;
        }
    }

    // 某能力位的冲突注记（挂在该 provides 行之下）。format 在 routing 里细分成 format-import / format-export
    // 两条可路由身份，故按前缀认亲——一个 format 条目可能带出两条注记（导入与导出可各自选不同包）。
    static void AppendEntryRouting(StringBuilder sb, string? packageId, string kind, string identity)
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

            bool active = row.ActivePackageId == packageId;
            sb.Append("\n        ").Append(row.Kind).Append(": ");
            if (active)
                sb.Append("ACTIVE (also provided by ")
                  .Append(string.Join(", ", row.Options.Where(o => o.PackageId != packageId).Select(o => "\"" + ExtensionManager.GetPackageName(o.PackageId) + "\"")))
                  .Append(", which is/are shadowed).");
            else
                sb.Append("SHADOWED: \"").Append(ExtensionManager.GetPackageName(row.ActivePackageId))
                  .Append("\" provides it instead, so THIS package's implementation is loaded but never used. See list_extension_routing / set_extension_routing.");
        }
    }

    // 该包提供的身份里，凡与别的包撞车的都如实标出「本包是生效者还是被顶替者」。
    // 这是排障的关键一句：status=Loaded 是真的（确实加载了），但"被路由掉"是另一根轴——缺了它，
    // agent 只会说"插件装好了、应该能用"。
    static void AppendRouting(StringBuilder sb, string? packageId)
    {
        if (string.IsNullOrEmpty(packageId))
            return;
        foreach (var row in ExtensionRouting.GetConflicts())
        {
            if (!row.Options.Any(o => o.PackageId == packageId))
                continue;
            bool active = row.ActivePackageId == packageId;
            sb.Append("\n    provides ").Append(row.Kind).Append(':').Append(row.Identity);
            if (active)
                sb.Append(" — ACTIVE (also provided by ")
                  .Append(string.Join(", ", row.Options.Where(o => o.PackageId != packageId).Select(o => "\"" + ExtensionManager.GetPackageName(o.PackageId) + "\"")))
                  .Append(", which is/are shadowed).");
            else
                sb.Append(" — SHADOWED: \"").Append(ExtensionManager.GetPackageName(row.ActivePackageId))
                  .Append("\" provides it instead, so THIS package's implementation is loaded but never used. See list_extension_routing / set_extension_routing.");
        }
    }
}

// get_extension_introduction：读某个【能力】的 introduction（作者写的 markdown 介绍）。
// 粒度是能力位（一个 manifest 条目 = 一个 voice/instrument/effect 引擎，或一个 format 后缀），不是安装包——
// 一个包可含多个能力，各自有各自的介绍。宿主【只认 manifest 声明的 introduction】：包里的 README 是作者
// 面向仓库读者的自留文件（含 build/license 等与用户无关的内容），不再被当作元数据。
// 按需拉取——介绍可能很长，只有模型确需细节时才调。
internal sealed class GetExtensionIntroductionTool : IAgentTool
{
    public string Name => "get_extension_introduction";

    public string Description =>
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
    // internal：摘要补齐也按这个口径喂模型——**绝不用比 agent 自己能看到的更少的信息去总结**，
    // 两处若各设一个数，早晚漂移成"摘要是从半份文档提炼的"而没人察觉。
    internal const int MaxIntroductionChars = 20000;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string query, packageId;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            query = doc.RootElement.GetString("capability");
            packageId = doc.RootElement.GetStringOrNull("packageId") ?? string.Empty;
        }
        catch (Exception ex) { return Task.FromResult("Error: invalid arguments — " + ex.Message); }

        query = (query ?? "").Trim();
        if (query.Length == 0)
            return Task.FromResult("Error: \"capability\" is empty.");

        // 三种写法（kind:identity / 裸 identity / 显示名）的匹配与消歧走共用查找——它与
        // set_extension_enabled 必须认同一套写法，见 ExtensionCapabilityLookup。
        var matches = ExtensionCapabilityLookup.Find(query, packageId);
        if (matches.Count == 0)
            return Task.FromResult(ExtensionCapabilityLookup.NotFoundError(query));
        if (matches.Count > 1)
            return Task.FromResult(ExtensionCapabilityLookup.AmbiguousError(query, matches));

        var (package, match) = matches[0];
        var label = string.Format("{0}:{1} (\"{2}\", from package \"{3}\")",
            match.Kind, string.Join(",", match.Identities), match.DisplayName, package.Name);

        if (string.IsNullOrEmpty(match.IntroductionPath))
        {
            // 没有 introduction 时如实说明（作者没写，不去读包里的 README 充数），并做【标注式降级】：
            // 给出所属包的自述，同时点明它是包级的、可能涵盖包里别的能力，不是这个能力的描述。
            var packageDescription = ExtensionManager.GetPackageDescription(package.Id);
            return Task.FromResult(string.IsNullOrWhiteSpace(packageDescription)
                ? string.Format("{0} ships no introduction — its author wrote none, and its package offers no description either. Nothing more is known about this capability than its name.", label)
                : string.Format(
                    "{0} ships no introduction — its author wrote none.\n\n"
                    + "Falling back to the PACKAGE-level description, given only because this capability has none of its own:\n\n{1}\n\n"
                    + "Treat that as a hint about the package, not as a description of this capability — the package may provide other capabilities that the sentence also covers. Do not relay it to the user as what this capability does.",
                    label, packageDescription));
        }

        string text;
        try { text = File.ReadAllText(match.IntroductionPath); }
        catch (Exception ex) { return Task.FromResult("Error: failed to read introduction — " + ex.Message); }

        if (text.Length > MaxIntroductionChars)
            text = text.Substring(0, MaxIntroductionChars) + "\n\n… (introduction truncated; " + (text.Length - MaxIntroductionChars) + " more characters)";
        return Task.FromResult(string.Format("Introduction for {0}, as written by the plugin author:\n\n{1}", label, text));
    }
}
