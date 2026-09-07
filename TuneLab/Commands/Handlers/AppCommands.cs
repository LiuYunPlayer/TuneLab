using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Commands.Handlers;

// `app` group：这个 TuneLab 自己的事实。刻意与 `project` 分开——工程是用户的文档，这里说的是
// **命令跑在哪个装置里**（哪一版、数据在哪、日志在哪、有没有编辑器、出了问题去哪说）。
//
// 存在的理由是排障与反馈：外部 agent 查出问题后不该自己去提 issue（结论错了，撤不回来），
// 而是把证据交给用户由他决定。要交的东西第一样就是"你这是哪一版"，而那件事此前**没有任何命令答得出**。
internal sealed class AppInfoCommand : ICommand
{
    public string Path => "app info";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "get_app_info";

    public string Brief => "Version, data dir, log file, where to report a bug";

    public string Documentation =>
        "Facts about the TuneLab this command is running in: version and build, where its user data and log file live, "
        + "whether it has an editor (a window and a user in front of it), and where problems get reported. "
        + "Use it when you are about to tell the user something is broken: quote the version and build, the relevant lines from the log file named here, "
        + "the output of list_extensions, and the smallest script that reproduces it — then let the USER file it. "
        + "Do not open issues or pull requests yourself: a wrong conclusion filed in someone else's tracker cannot be taken back. "
        + "It deliberately does NOT list extensions, settings or the open project — list_extensions, list_settings and get_project_overview are those.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    public Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
        => Task.FromResult(CommandResult.Ok(new JsonObject
        {
            ["version"] = AppInfo.VersionString,
            ["build"] = AppInfo.Build,
            ["dataDir"] = PathManager.TuneLabFolder,
            // 这个进程正在写的那一份，以及它们所在的目录（要看上一次崩溃就得去目录里找更早的）。
            ["logFile"] = PathManager.LogFilePath,
            ["logsDir"] = PathManager.LogsFolder,
            // 有没有编辑器 = 有没有窗口和一个坐在前面的人。依赖选区的命令、以及"问用户"这件事都由它决定。
            ["hasEditor"] = ctx.EditorState != null,
            ["os"] = RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture + ")",
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["issues"] = AppInfo.IssuesUrl,
            ["forum"] = AppInfo.ForumUrl,
        }));

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        string? Text(string key) => obj[key]?.GetValue<string>();
        var sb = new StringBuilder();
        sb.Append("TuneLab ").Append(Text("version"));
        if (Text("build") is { Length: > 0 } build)
            sb.Append(" (build ").Append(build).Append(')');
        sb.AppendLine(" — the TuneLab these commands run in.");
        sb.Append("  data directory: ").AppendLine(Text("dataDir"));
        sb.Append("  log file: ").AppendLine(Text("logFile"));
        sb.Append("  earlier logs: ").AppendLine(Text("logsDir"));
        sb.Append("  editor: ").AppendLine(obj["hasEditor"]?.GetValue<bool>() ?? false
            ? "yes — there is a window and a user, so the current selection exists and the user can be asked"
            : "no — windowless, so nothing depends on \"what the user has selected\" and there is nobody to ask here");
        sb.Append("  os: ").AppendLine(Text("os"));
        sb.Append("  runtime: ").AppendLine(Text("runtime"));
        sb.AppendLine();
        // 报告模板留在这里而不是各入口的引导语里：这样 CLI、侧栏 agent、MCP 看到的是同一份要求。
        sb.AppendLine("Reporting a problem: hand the user the version and build above, the lines around the failure in the log file, "
            + "the output of list_extensions, and the smallest script that reproduces it. The USER decides whether to file it — "
            + "do not open an issue or a pull request yourself.");
        sb.Append("  issues: ").AppendLine(Text("issues"));
        sb.Append("  forum: ").Append(Text("forum"));
        return sb.ToString();
    }
}
