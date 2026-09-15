using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions;
using TuneLab.Extensions.Scripted;
using TuneLab.Utils;

namespace TuneLab.Commands.Handlers;

// `extension fetch-runtime` —— 脚本运行时的自动部署（设计文档 §8.6）。装一个解释器发行（python/node）
// 到扩展目录，成为一个 type:"runtime" 资源包；之后声明它的脚本插件（manifest `runtime`）在 ③ 级探测命中、
// 懒起。这是「用户已经下载了 .tlx 里的脚本插件、但机器上没有解释器」那条路的自动化替代。
//
// 与 `extension install` 同一授权档（ExtensionInstall）：都往用户机器上放并在之后运行第三方代码。
// 差别只在来源——install 吃本地 .tlx，fetch-runtime 从镜像表下载（国内优先、竞速择快、可选 sha256 钉）。
internal sealed class ExtensionFetchRuntimeCommand : ICommand
{
    public string Path => "extension fetch-runtime";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "fetch_scripted_runtime";

    public string Brief => "Download and install a scripting runtime (Python / Node.js)";

    public string Documentation =>
        "Fetch an interpreter distribution (a 'runtime' resource package) so scripted extensions (manifest entries with a `runtime` field) can load. "
        + "Give it a runtime spec exactly like the manifest field: \"python>=3.10\" or \"node>=18\". It picks the fastest reachable mirror (domestic mirrors are tried before the official one), "
        + "verifies the download when the built-in feed pins a checksum, then installs it into the user's extensions folder and registers it \u2014 after which list_extensions / the failing scripted plugin will resolve. "
        + "\nIt downloads from the network and needs the user's authorization (same as installing a plugin). If nothing in the feed matches the platform, it refuses and the user must supply an interpreter themselves (set the path in settings, or install a 'runtime' .tlx). "
        + "\nAlready-satisfied interpreters are left alone: check the plugin's status first (list_extensions shows 'no matching runtime' only when one is genuinely missing).";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "runtime": { "type": "string", "description": "Runtime spec, same grammar as the manifest field, e.g. \"python>=3.10\" or \"node>=18\"." }
          },
          "required": ["runtime"],
          "additionalProperties": false
        }
        """;

    // 测试注入点：非空则用它替代真实下载（喂预制归档走完整落盘/登记链）。生产恒 null。
    public ScriptedRuntimeFetcher.DownloadAsyncDelegate? DownloaderOverride;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var specText = (args.Json.GetString("runtime") ?? "").Trim().Trim('"');
        if (specText.Length == 0)
            return CommandResult.Fail("empty_runtime", "\"runtime\" is required (e.g. \"python>=3.10\").");
        if (!RuntimeSpec.TryParse(specText, out var spec, out var parseError))
            return CommandResult.Fail("bad_runtime", parseError!);
        var cell = RuntimeFeed.Match(spec);
        if (cell == null)
            return CommandResult.Fail("no_feed_cell", string.Format(CultureInfo.InvariantCulture,
                "the built-in feed has no auto-installable runtime matching \"{0}\" for this platform ({1}); provide an interpreter yourself.",
                spec.Describe(), PlatformHelper.GetPlatform()));

        var label = string.Format(CultureInfo.InvariantCulture, "{0} {1}", cell.Value.Flavor, cell.Value.Version.ToString(3));
        var (proceed, message) = await ctx.Authorize(new AuthorizationRequest(WriteKind.ExtensionInstall, 0, label, "runtime"), cancellationToken);
        if (!proceed)
            return CommandResult.Ok(new JsonObject { ["runtime"] = spec.Describe(), ["outcome"] = "refused", ["note"] = message });

        var result = await ScriptedRuntimeFetcher.FetchInstallAsync(spec, null, cancellationToken, DownloaderOverride);
        var node = new JsonObject
        {
            ["runtime"] = spec.Describe(),
            ["resolved"] = label,
            ["outcome"] = result.Ok ? "applied" : "failed",
            ["note"] = result.Ok ? (string.IsNullOrEmpty(message) ? null : message) : result.Message,
        };
        if (result.PackageDir != null)
            node["path"] = result.PackageDir;
        return result.Ok ? CommandResult.Ok(node) : CommandResult.Fail("fetch_failed", result.Message, node);
    }

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;
        var runtime = obj["runtime"]?.GetValue<string>() ?? "?";
        var label = obj["resolved"]?.GetValue<string>() ?? runtime;
        switch (obj["outcome"]?.GetValue<string>())
        {
            case "refused":
                return obj["note"]?.GetValue<string>() ?? string.Empty;
            case "failed":
                return string.Format("Could not install the {0} runtime: {1}.", label, obj["note"]?.GetValue<string>() ?? "unknown error");
            default:
                return string.Format("Installed the {0} runtime into the extensions folder. Scripted plugins that asked for \"{1}\" "
                    + "will pick it up on their next (re)load — from the Extensions sidebar use Reload, or restart TuneLab.",
                    label, runtime);
        }
    }
}
