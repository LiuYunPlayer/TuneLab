using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Bridge;
using TuneLab.Commands;

namespace TuneLab.Cli.Mcp;

// `tunelab mcp` —— 把命令面接到 MCP 上，让订阅式的外部 agent 驱动 TuneLab。
//
// 【为什么是独立进程、不由宿主 spawn】宿主没开的时候，这个 server 仍然活着，并且能**在对话里**告诉
// agent "请先启动 TuneLab"。反过来（宿主自己开一个端口、客户端连它）的失败模式是工具从 agent 的工具
// 列表里静默消失——agent 连"该怎么修"都说不出来，那是死胡同。
// tools/list 由 CommandRegistry 就地自省（纯声明），故**列能力不需要 TuneLab 开着**；真要执行才连桥。
//
// 【为什么住在 CLI 里而不是又一个二进制】BridgeClient、runner、参数校验、文本换名都在这里，而 MCP 要的
// 就是这些外加一层协议壳；命令行本身也随发布产物分发（build-artifacts 把它 publish 进同一个目录）。
//
// 【授权】elicitation 明确不做（docs/command-surface.md §11）：批准 UI 在 MCP 客户端那边，annotation
// 已经如实标了哪些工具会改东西，服务端再问一遍既是双重询问、这里也没有 UI 可用。故每次执行声明 auto
// 且 canAsk=false——**这不是提权**：宿主那侧的天花板是用户在 TuneLab 里设的档位，声明 auto 只是说
// "这个入口自己不再加码"。用户设成 confirm 时，改动会被如实拒绝（"没法在这里问"），而不是被偷偷做掉。
internal static class McpServer
{
    // 支持的协议版本，新→旧。客户端在 initialize 里说它要哪一版：认得就回同一版（对端据此按那一版说话），
    // 认不得就回我们最新的一版，由客户端决定还谈不谈。
    static readonly string[] ProtocolVersions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // stdout 是协议本身，任何多打的一个字都会让对端解析失败。故这条路上**不许**往 stdout 写别的
        // （诊断一律 stderr），而 Main 里设过的 Console.OutputEncoding 在这里也换成不带 BOM 的写法。
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false };

        using var connection = new HostConnection();
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(cancellationToken);
            if (line == null)
                break;      // 客户端收工
            if (line.Length == 0)
                continue;

            JsonObject? message;
            try { message = JsonNode.Parse(line) as JsonObject; }
            catch (JsonException) { message = null; }

            if (message == null)
            {
                await WriteAsync(output, Error(null, BridgeProtocol.ErrorParse, "not a JSON-RPC object"), cancellationToken);
                continue;
            }

            var id = message["id"];
            var method = message["method"]?.GetValue<string>();
            if (method == null)
                continue;   // 响应（我们不发请求，故不会有）——忽略，不是错误

            JsonObject response;
            try
            {
                var result = await DispatchAsync(method, message["params"] as JsonObject, connection, cancellationToken);
                if (result == null)
                {
                    // 通知（无 id 的请求）：按规范不回响应。
                    if (id == null)
                        continue;
                    response = Error(id, BridgeProtocol.ErrorMethodNotFound, "unknown method \"" + method + "\"");
                }
                else
                {
                    response = Result(id, result);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                response = Error(id, BridgeProtocol.ErrorInternal, ex.Message);
            }

            if (id != null)
                await WriteAsync(output, response, cancellationToken);
        }
        return 0;
    }

    // null = 这个方法我们不认（通知则忽略、请求则回 method not found）。
    static async Task<JsonNode?> DispatchAsync(string method, JsonObject? parameters, HostConnection connection, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "initialize":
                return Initialize(parameters);
            case "tools/list":
                return new JsonObject { ["tools"] = McpTools.List() };
            case "tools/call":
                return await CallAsync(parameters, connection, cancellationToken);
            case "ping":
                return new JsonObject();
            case "notifications/initialized":
            case "notifications/cancelled":
                return new JsonObject();   // 通知：有 id 才会被回，实际不会
            default:
                return null;
        }
    }

    static JsonObject Initialize(JsonObject? parameters)
    {
        var asked = parameters?["protocolVersion"]?.GetValue<string>();
        var version = asked != null && ProtocolVersions.Contains(asked) ? asked : ProtocolVersions[0];
        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "tunelab",
                ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            },
            ["instructions"] =
                "These tools drive TuneLab, a singing-voice editor: its project (tracks, parts, notes, parameters, phonemes), its script API, its saved scripts, its extensions, its settings and keyboard shortcuts, and the editor window itself. "
                + "They are grouped by subject, split into the ones that only read and the ones that change something; each takes a \"subcommand\" plus that subcommand's \"arguments\". "
                + "The tool list carries a one-line summary of each subcommand — call " + McpTools.HelpToolName + " for the full manual of one before using it the first time. "
                + "\nTo edit the project itself, prefer script_edit with subcommand \"run\" (a JavaScript program over the tl API — one undoable change, and it reports exactly what it changed); read docs_read with subcommand \"script-api\" first. "
                + "\nEverything except the docs and the help needs TuneLab to be running with its command bridge on (Settings → General). If it is not, the tools say so instead of failing silently. "
                + "\nHow much the tools are allowed to change is the user's setting inside TuneLab and cannot be raised from here: if they set it to confirm or to read-only, changes are reported but not applied, and the reply says so.",
        };
    }

    static async Task<JsonNode> CallAsync(JsonObject? parameters, HostConnection connection, CancellationToken cancellationToken)
    {
        var name = parameters?["name"]?.GetValue<string>() ?? "";
        var arguments = parameters?["arguments"] as JsonObject ?? new JsonObject();

        if (name == McpTools.HelpToolName)
            return Help(arguments);

        if (!McpTools.TryGet(name, out var tool))
            return Failure(string.Format("There is no tool called \"{0}\". Call tools/list to see what there is.", name));

        var subcommand = (arguments["subcommand"]?.GetValue<string>() ?? "").Trim();
        if (subcommand.Length == 0)
            return Failure(string.Format("\"{0}\" needs a \"subcommand\". It takes: {1}.", name, Verbs(tool)));
        if (!McpTools.TryGetCommand(tool, subcommand, out var command))
            return Failure(string.Format("\"{0}\" has no subcommand \"{1}\". It takes: {2}.", name, subcommand, Verbs(tool)));

        // DeepClone：这棵子树还挂在收到的那条报文上，而 JsonNode 不许一个节点有两个父亲——直接往下传，
        // 桥那侧把它塞进请求体时会当场抛（实测：每一条要连桥的命令都会变成一句 "The node already has a parent"）。
        var commandArguments = (arguments["arguments"] as JsonObject)?.DeepClone().AsObject() ?? new JsonObject();
        if (Unknown(command, commandArguments) is { } unknown)
            return Failure(unknown);

        // 文档与手册不看宿主环境（答案只取决于程序自带的东西），故它们不连桥——TuneLab 开着没开着都行。
        if (!command.NeedsHost)
            return Rendered(await InProcess.RunAsync(command, commandArguments, new CommandContext(), cancellationToken));

        var (runner, connectError) = await connection.RunnerAsync(cancellationToken);
        if (runner == null)
            return Failure(connectError!);

        var outcome = await connection.RunWithRetryAsync(runner, command, commandArguments, cancellationToken);
        return Rendered(outcome);
    }

    static JsonNode Help(JsonObject arguments)
    {
        var path = (arguments["command"]?.GetValue<string>() ?? "").Trim();
        if (!CommandRegistry.TryGet(path, out var command))
            return Failure(string.Format(
                "There is no command \"{0}\". A command's path is its tool's group and its subcommand, e.g. \"project export\". Call tools/list to see them.", path));

        return new JsonObject
        {
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = McpTools.Help(command) } },
            ["isError"] = false,
        };
    }

    // 认不出的参数名【不静默忽略】：忽略掉的那个参数正是调用方以为生效了的东西（同 CLI 的口径）。
    static string? Unknown(ICommand command, JsonObject arguments)
    {
        using var schema = JsonDocument.Parse(command.ParametersJsonSchema);
        var properties = schema.RootElement.TryGetProperty("properties", out var p) ? p : default;
        var known = properties.ValueKind == JsonValueKind.Object
            ? properties.EnumerateObject().Select(x => x.Name).ToList()
            : [];

        foreach (var argument in arguments)
        {
            if (known.Contains(argument.Key))
                continue;
            return string.Format("\"{0}\" has no parameter \"{1}\": {2}. Call {3} with command \"{0}\" for what each one means.",
                command.Path, argument.Key,
                known.Count == 0 ? "it takes no parameters at all" : "it takes " + string.Join(", ", known),
                McpTools.HelpToolName);
        }
        return null;
    }

    static string Verbs(McpTools.Tool tool) => string.Join(", ", tool.Commands.Select(McpTools.Verb));

    // 一条命令跑完 → MCP 的结果。**只给渲染文本**：那份措辞是命令面自己写的，CLI 的 stdout 与模型
    // 上下文里出现的是同一句话（结构化的 Data 留给命令行的 --json，不在这条路上另开一套形状）。
    // 命令自己失败走 isError=true（不是 JSON-RPC 层的 error）——那是"工具跑了但没成事"，不是协议出错。
    static JsonNode Rendered(CommandOutcome outcome)
        => outcome.Failure != null
            ? Failure(McpText.ForMcp(outcome.Failure))
            : new JsonObject
            {
                ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = McpText.ForMcp(outcome.Text) } },
                ["isError"] = false,
            };

    static JsonNode Failure(string message) => new JsonObject
    {
        ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = message } },
        ["isError"] = true,
    };

    static async Task WriteAsync(StreamWriter output, JsonObject message, CancellationToken cancellationToken)
    {
        // 一条报文一行（stdio 传输就是这么切的），故序列化里不能有裸换行——JsonNode 默认就不写缩进。
        await output.WriteLineAsync(message.ToJsonString().AsMemory(), cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    static JsonObject Result(JsonNode? id, JsonNode result)
        => new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };

    static JsonObject Error(JsonNode? id, int code, string message)
        => new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };
}

// 与运行中的 TuneLab 的那条连接：**懒连、断了重连**。
//
// server 的寿命是整个对话，而 TuneLab 可能在中途才启动、也可能中途退出又开起来。开机就连会让"宿主
// 没开"变成启动失败（那正是这个设计要避免的）；一次性连上不管则会在宿主重启后永远失败下去。
internal sealed class HostConnection : IDisposable
{
    BridgeClient? mClient;
    readonly AuthorizationState mAuthorization = new();

    public async Task<(ICommandRunner? Runner, string? Error)> RunnerAsync(CancellationToken cancellationToken)
    {
        if (mClient == null)
        {
            var (client, error) = await BridgeClient.ConnectAsync(cancellationToken);
            if (client == null)
                return (null, error);
            mClient = client;
        }
        return (new BridgeRunner(mClient, mAuthorization), null);
    }

    // 跑一条命令；连接在此期间断掉（宿主被关掉/重启）就重连一次再试。**只重试一次，且只在连接层面
    // 出问题时**——命令本身失败是它的答案，重跑一遍既不会变对，还可能把一次写做成两次。
    public async Task<CommandOutcome> RunWithRetryAsync(ICommandRunner runner, ICommand command, JsonObject arguments, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await runner.RunAsync(command, arguments, BridgeProtocol.AuthAuto, canAsk: false, cancellationToken);
            if (!Disconnected(outcome.Failure))
                return outcome;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // 落到下面重连
        }

        Reset();
        var (retry, error) = await RunnerAsync(cancellationToken);
        if (retry == null)
            return new CommandOutcome(null, string.Empty, error);

        try { return await retry.RunAsync(command, arguments, BridgeProtocol.AuthAuto, canAsk: false, cancellationToken); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Reset();
            return new CommandOutcome(null, string.Empty, "TuneLab closed the connection. Is it still running?");
        }
    }

    static bool Disconnected(string? failure) => failure != null && failure.StartsWith("TuneLab closed the connection", StringComparison.Ordinal);

    void Reset()
    {
        mClient?.Dispose();
        mClient = null;
    }

    public void Dispose() => Reset();
}
