using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Bridge;
using TuneLab.Commands;
using TuneLab.Headless;

namespace TuneLab.Cli;

// tunelab 命令行：`tunelab <group> <verb> [--flag value ...]`。
//
// 它只是命令面的又一个入口——末端动作、措辞、闸门流程一概在命令面，这里只做四件事：
// 解析参数、把命令送出去（连桥 / 就地起无头宿主）、把结果打出来、给一个诚实的退出码。
//
// 【--help 不连宿主】命令树来自 CommandRegistry（纯声明，不启动宿主也自省得出），故列命令、看某条
// 命令的参数、校验参数名都在本地完成。真要执行才连桥（或起 headless）。
internal static class Program
{
    // 退出码（docs/command-surface.md §9.2）：脚本据此分支，故这四种必须分得开。
    const int ExitOk = 0;
    const int ExitCommandFailed = 1;
    const int ExitUsage = 2;
    const int ExitNoHost = 3;

    static async Task<int> Main(string[] args)
    {
        // 结果里有中文（扩展名、手册正文、脚本输出）。Windows 控制台默认还是本地代码页，不改这一下
        // 就会把它们打成乱码——而那是【结果本身】看起来出了问题，不是显示问题，最容易误导人。
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (Exception) { /* 重定向/无控制台时不必强求 */ }

        try { return await RunAsync(args); }
        catch (OperationCanceledException) { return ExitCommandFailed; }
        catch (Exception ex)
        {
            Console.Error.WriteLine("tunelab: " + ex.Message);
            return ExitCommandFailed;
        }
    }

    static async Task<int> RunAsync(string[] args)
    {
        var (options, rest, optionError) = ProcessOptions.Extract(args);
        if (optionError != null)
        {
            Console.Error.WriteLine("tunelab: " + optionError);
            return ExitUsage;
        }

        bool wantsHelp = rest.Contains("--help") || rest.Contains("-h");
        var positional = rest.TakeWhile(a => !a.StartsWith('-')).ToArray();

        if (options.ProjectPath != null && !options.Headless)
        {
            Console.Error.WriteLine("tunelab: --project only applies with --headless. Without it the commands run in the project the running TuneLab has open.");
            return ExitUsage;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

        // ── 批量：一个进程跑一串命令（同一个工程连着跑，见 --commands 的帮助）
        if (options.CommandsFile != null)
        {
            if (positional.Length > 0)
            {
                Console.Error.WriteLine("tunelab: --commands runs a list of commands, so don't also name one on the command line.");
                return ExitUsage;
            }

            var (lines, readError) = ReadCommandList(options.CommandsFile);
            if (readError != null)
            {
                Console.Error.WriteLine("tunelab: " + readError);
                return ExitUsage;
            }

            return await WithRunner(options, cancellation.Token,
                runner => RunBatchAsync(runner, lines, options, cancellation.Token));
        }

        if (positional.Length == 0)
        {
            PrintCommandTree(null);
            if (options.Headless)
            {
                Console.Error.WriteLine("tunelab: --headless needs a command to run, or --commands <file> to run a list of them.");
                return ExitUsage;
            }
            return wantsHelp || args.Length == 0 ? ExitOk : ExitUsage;
        }
        if (positional.Length == 1)
        {
            // 只给了 group：列这一组（认不出这个 group 就是用法错，且照样把整棵树打出来指路）。
            bool known = CommandRegistry.Groups.Contains(positional[0]);
            PrintCommandTree(known ? positional[0] : null);
            if (!known)
                Console.Error.WriteLine("tunelab: unknown command group \"" + positional[0] + "\".");
            return known ? ExitOk : ExitUsage;
        }

        var path = positional[0] + " " + positional[1];
        if (!CommandRegistry.TryGet(path, out var command))
        {
            PrintCommandTree(CommandRegistry.Groups.Contains(positional[0]) ? positional[0] : null);
            Console.Error.WriteLine("tunelab: unknown command \"" + path + "\".");
            return ExitUsage;
        }

        if (wantsHelp)
        {
            PrintCommandHelp(command);
            return ExitOk;
        }

        var (arguments, authorization, json, error) = ParseOptions(command, rest.Skip(2).ToArray(), options);
        if (error != null)
        {
            Console.Error.WriteLine("tunelab: " + error);
            Console.Error.WriteLine("Run \"tunelab " + path + " --help\" for this command's parameters.");
            return ExitUsage;
        }

        return await WithRunner(options, cancellation.Token, async runner =>
        {
            WarnIfUnattended(command, authorization);
            var outcome = await runner.RunAsync(command, arguments, authorization, CanAsk, cancellation.Token);
            if (outcome.Failure != null)
            {
                Console.Error.WriteLine(outcome.Failure);
                return ExitCommandFailed;
            }
            Print(outcome, json);
            return ExitOk;
        });
    }

    // 一串命令，同一个进程、同一个工程连着跑。首条失败即止——CI 里让后面的命令在一个已经不对的
    // 状态上接着跑，只会把"哪一步坏了"埋掉。
    //
    // 【回声打 stderr、结果打 stdout】故 --json 时 stdout 仍是一串干净的 JSON，人看的进度不混进去。
    static async Task<int> RunBatchAsync(ICommandRunner runner, IReadOnlyList<(int Number, string Text)> lines, ProcessOptions options, CancellationToken cancellationToken)
    {
        int ran = 0;
        foreach (var (number, text) in lines)
        {
            var tokens = Tokenize(text, out var tokenError);
            if (tokenError != null)
                return BatchUsageError(number, tokenError);
            if (tokens.Count < 2)
                return BatchUsageError(number, "a command is \"<group> <verb> [--name value ...]\".");

            var path = tokens[0] + " " + tokens[1];
            if (!CommandRegistry.TryGet(path, out var command))
                return BatchUsageError(number, "unknown command \"" + path + "\".");

            var (arguments, authorization, json, error) = ParseOptions(command, tokens.Skip(2).ToArray(), options);
            if (error != null)
                return BatchUsageError(number, error);

            Console.Error.WriteLine("$ " + text);
            WarnIfUnattended(command, authorization);

            var outcome = await runner.RunAsync(command, arguments, authorization, CanAsk, cancellationToken);
            if (outcome.Failure != null)
            {
                Console.Error.WriteLine("tunelab: line " + number + ": " + outcome.Failure);
                Console.Error.WriteLine(string.Format("tunelab: stopped at \"{0}\"; {1} command(s) ran before it.", path, ran));
                return ExitCommandFailed;
            }
            Print(outcome, json);
            ran++;
        }

        Console.Error.WriteLine(string.Format("tunelab: {0} command(s) ok.", ran));
        return ExitOk;
    }

    static int BatchUsageError(int number, string message)
    {
        Console.Error.WriteLine("tunelab: line " + number + ": " + message);
        return ExitUsage;
    }

    static void Print(CommandOutcome outcome, bool json)
    {
        if (json)
            Console.Out.WriteLine((outcome.Data ?? JsonValue.Create((object?)null))?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null");
        else
            Console.Out.WriteLine(outcome.Text);
    }

    // 命令送去哪儿跑。headless 那条路整趟都在无头宿主的那条线程上（body 也是），故这里是同步等它跑完。
    static async Task<int> WithRunner(ProcessOptions options, CancellationToken cancellationToken, Func<ICommandRunner, Task<int>> body)
    {
        var authorization = new AuthorizationState();

        if (options.Headless)
        {
            var headless = new HeadlessOptions
            {
                ProjectPath = options.ProjectPath,
                // 授权逐条派生（每条命令自己声明档位），故这里不给——宿主共同环境里"没配授权"是实话。
                Authorization = null,
                Report = message => Console.Error.WriteLine("tunelab: " + message),
            };
            // 工程打不开就抛出来，由 Main 统一报（那时一条命令都还没跑）。
            return HeadlessHost.Run(headless, ctx => body(new HeadlessRunner(ctx, authorization)));
        }

        var (client, connectError) = await BridgeClient.ConnectAsync(cancellationToken);
        if (client == null)
        {
            Console.Error.WriteLine("tunelab: " + connectError);
            return ExitNoHost;
        }
        using (client)
            return await body(new BridgeRunner(client, authorization));
    }

    // 能不能就地问用户"这次写做不做"。管道/重定向进来的 stdin 上没人可问。
    static bool CanAsk => !Console.IsInputRedirected;

    // 非交互的 shell 里跑 edit 命令：命令会如实回报"需要确认但这里没法问"，但那句话出现在结果里、
    // 混在正常输出中间。先在 stderr 上说一次，免得 CI 里的人以为写生效了。整趟只说一次。
    static bool mWarnedUnattended;
    static void WarnIfUnattended(ICommand command, string authorization)
    {
        if (mWarnedUnattended || CanAsk || command.Kind != CommandKind.Edit || authorization != BridgeProtocol.AuthConfirm)
            return;
        mWarnedUnattended = true;
        Console.Error.WriteLine("tunelab: stdin is not interactive, so nothing can be confirmed here — anything that writes will be reported but NOT applied.");
        Console.Error.WriteLine("tunelab: pass --yes to allow it, or --dry-run to only see what it would change.");
    }

    // 解析这一条命令的参数：schema 说什么类型就转成什么类型（CLI 的 flag 全是字符串，而
    // `--reset` 这种布尔要能省略值）。认不出的参数名当用法错——静默忽略会让人以为设置生效了。
    // 进程级的 --json / --yes / --dry-run 是这一趟的默认，批量文件里每行都可以自己覆盖。
    static (JsonObject Arguments, string Authorization, bool Json, string? Error) ParseOptions(ICommand command, string[] options, ProcessOptions defaults)
    {
        var arguments = new JsonObject();
        var authorization = defaults.Authorization;
        bool json = defaults.Json;

        using var schema = JsonDocument.Parse(command.ParametersJsonSchema);
        var properties = schema.RootElement.TryGetProperty("properties", out var p) ? p : default;

        for (int i = 0; i < options.Length; i++)
        {
            var option = options[i];
            if (!option.StartsWith("--"))
                return (arguments, authorization, json, "unexpected argument \"" + option + "\" (parameters are given as --name value).");

            var name = option[2..];
            string? value = null;
            int eq = name.IndexOf('=');
            if (eq >= 0)
            {
                value = name[(eq + 1)..];
                name = name[..eq];
            }

            switch (name)
            {
                case "json": json = true; continue;
                case "yes": authorization = BridgeProtocol.AuthAuto; continue;
                case "dry-run": authorization = BridgeProtocol.AuthReadOnly; continue;
                case "help": continue;
            }

            if (properties.ValueKind != JsonValueKind.Object || !properties.TryGetProperty(name, out var declared))
                return (arguments, authorization, json, "\"" + command.Path + "\" has no parameter \"" + name + "\".");

            var types = DeclaredTypes(declared);
            bool boolean = types.Contains("boolean");
            if (value == null)
            {
                // 布尔参数允许只写 --flag；其余必须带值（下一个 token 若是另一个 --flag 就说明漏了值）。
                if (boolean && (i + 1 >= options.Length || options[i + 1].StartsWith("--")))
                    value = "true";
                else if (i + 1 < options.Length)
                    value = options[++i];
                else
                    return (arguments, authorization, json, "parameter \"" + name + "\" needs a value.");
            }

            arguments[name] = Coerce(value, types);
        }
        return (arguments, authorization, json, null);
    }

    static string[] DeclaredTypes(JsonElement declared)
    {
        if (!declared.TryGetProperty("type", out var type))
            return [];
        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(t => t.GetString() ?? "").ToArray()
            : [type.GetString() ?? ""];
    }

    static JsonNode? Coerce(string value, string[] types)
    {
        if (types.Contains("boolean") && bool.TryParse(value, out var b))
            return b;
        if ((types.Contains("number") || types.Contains("integer")) && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        if (types.Contains("object") || types.Contains("array"))
        {
            // 对象/数组参数（如 run-saved 的 inputs）直接收 JSON 文本。
            try { return JsonNode.Parse(value); }
            catch (JsonException) { /* 解析不了就按字符串送过去，让命令自己报它自己的错 */ }
        }
        return value;
    }

    // 读命令清单：一行一条，空行与 # 开头的注释跳过（行号照原样留着，报错时指得准）。
    // "-" 表示从 stdin 读。
    static (IReadOnlyList<(int Number, string Text)> Lines, string? Error) ReadCommandList(string path)
    {
        string[] raw;
        try
        {
            raw = path == "-"
                ? (Console.In.ReadToEnd() ?? string.Empty).Split('\n')
                : File.ReadAllLines(path);
        }
        catch (Exception ex) { return ([], "cannot read the command list \"" + path + "\" — " + ex.Message); }

        var lines = new List<(int, string)>();
        for (int i = 0; i < raw.Length; i++)
        {
            var text = raw[i].Trim();
            if (text.Length == 0 || text.StartsWith('#'))
                continue;
            lines.Add((i + 1, text));
        }
        return lines.Count == 0 ? ([], "the command list \"" + path + "\" has no commands in it.") : (lines, null);
    }

    // 批量文件里一行的切词：空白分隔，双引号成组，组内两个连写的双引号表示一个字面双引号。
    // 【不用反斜杠转义】——这些行里最常出现的就是 Windows 路径，反斜杠一旦是转义符就处处是坑。
    internal static List<string> Tokenize(string line, out string? error)
    {
        var tokens = new List<string>();
        var token = new System.Text.StringBuilder();
        bool quoted = false, started = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    token.Append('"');
                    i++;
                    continue;
                }
                quoted = !quoted;
                started = true;
                continue;
            }
            if (!quoted && char.IsWhiteSpace(c))
            {
                if (started)
                    tokens.Add(token.ToString());
                token.Clear();
                started = false;
                continue;
            }
            token.Append(c);
            started = true;
        }

        if (quoted)
        {
            error = "unbalanced double quote.";
            return tokens;
        }
        if (started)
            tokens.Add(token.ToString());
        error = null;
        return tokens;
    }

    static void PrintCommandTree(string? onlyGroup)
    {
        Console.Out.WriteLine("tunelab — drive TuneLab from the terminal.");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Usage: tunelab <group> <verb> [--name value ...] [--json] [--yes | --dry-run]");
        Console.Out.WriteLine("       tunelab <group> <verb> --help     what one command does and which parameters it takes");
        Console.Out.WriteLine("       tunelab --commands <file|->       run a list of commands (one per line, # comments) in one go");
        Console.Out.WriteLine();
        Console.Out.WriteLine("By default the commands run in the TuneLab you have open. With --headless they run in a");
        Console.Out.WriteLine("windowless TuneLab started right here: extensions load and synthesis works, but there is no");
        Console.Out.WriteLine("audio device and no editor, so nothing depends on \"what the user has selected\".");
        Console.Out.WriteLine("  --headless              run without a window instead of attaching to a running TuneLab");
        Console.Out.WriteLine("  --project <file>        (--headless only) open this project; without it you get an empty one");
        Console.Out.WriteLine("Headless uses the user data directory (settings, extensions, script library) like the app does;");
        Console.Out.WriteLine("set TUNELAB_DATA_DIR to point it at a sandbox instead.");
        Console.Out.WriteLine();
        foreach (var group in CommandRegistry.Groups)
        {
            if (onlyGroup != null && group != onlyGroup)
                continue;
            Console.Out.WriteLine(group);
            foreach (var command in CommandRegistry.All.Where(c => CommandRegistry.GroupOf(c) == group))
                Console.Out.WriteLine("  " + command.Path.PadRight(26) + command.Brief
                    + (command.Kind == CommandKind.Read ? "" : "  [" + command.Kind.ToString().ToLowerInvariant() + "]"));
        }
        Console.Out.WriteLine();
        Console.Out.WriteLine("Anything marked [edit] changes the user's data and needs authorization: you are asked here on stdin,");
        Console.Out.WriteLine("unless you pass --yes (do it) or --dry-run (only report what would change).");
        Console.Out.WriteLine("Exit codes: 0 ok, 1 the command failed, 2 wrong usage, 3 TuneLab is not reachable.");
    }

    static void PrintCommandHelp(ICommand command)
    {
        Console.Out.WriteLine(command.Path + "  [" + command.Kind.ToString().ToLowerInvariant() + "]");
        Console.Out.WriteLine();
        Console.Out.WriteLine(command.Documentation);
        Console.Out.WriteLine();

        using var schema = JsonDocument.Parse(command.ParametersJsonSchema);
        if (!schema.RootElement.TryGetProperty("properties", out var properties) || !properties.EnumerateObject().Any())
        {
            Console.Out.WriteLine("Parameters: none.");
            return;
        }

        var required = schema.RootElement.TryGetProperty("required", out var r)
            ? r.EnumerateArray().Select(x => x.GetString()).ToHashSet()
            : [];
        Console.Out.WriteLine("Parameters:");
        foreach (var property in properties.EnumerateObject())
        {
            Console.Out.WriteLine("  --" + property.Name
                + " <" + string.Join("|", DeclaredTypes(property.Value)) + ">"
                + (required.Contains(property.Name) ? "  (required)" : ""));
            if (property.Value.TryGetProperty("description", out var description))
                Console.Out.WriteLine("      " + description.GetString());
        }
    }
}

// 进程级的开关：与"跑哪条命令"无关，管的是【这一趟怎么跑】。
//
// 它们在整个命令行里就地识别（不要求写在命令名之前），代价是名字不能与任何命令的参数撞车——
// CommandRegistryTests 有一条封条盯着这件事，撞了会当场红。
internal sealed record ProcessOptions
{
    public bool Headless { get; init; }
    public string? ProjectPath { get; init; }
    public string? CommandsFile { get; init; }
    public string Authorization { get; init; } = BridgeProtocol.AuthConfirm;
    public bool Json { get; init; }

    // CLI 自己占掉的参数名【全集】——进程级这三个，加上可以逐条覆盖的那几个（见 Program.ParseOptions）。
    // 命令的参数名不能与它们撞车：撞了就再也传不进去，且用户看不出为什么。CommandRegistryTests 有一条
    // 封条盯着，撞了当场红。改下面的 switch 时同步改这份清单。
    public static readonly string[] Names = ["headless", "project", "commands", "json", "yes", "dry-run", "help"];

    public static (ProcessOptions Options, string[] Remaining, string? Error) Extract(string[] args)
    {
        bool headless = false, json = false;
        string? projectPath = null, commandsFile = null, authorization = null;
        var rest = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var name = args[i];
            string? value = null;
            if (name.StartsWith("--"))
            {
                int eq = name.IndexOf('=');
                if (eq >= 0)
                {
                    value = name[(eq + 1)..];
                    name = name[..eq];
                }
                name = name[2..];
            }
            else
            {
                rest.Add(args[i]);
                continue;
            }

            switch (name)
            {
                case "headless":
                    headless = true;
                    continue;
                case "json":
                    json = true;
                    continue;
                case "yes":
                case "dry-run":
                    var declared = name == "yes" ? BridgeProtocol.AuthAuto : BridgeProtocol.AuthReadOnly;
                    if (authorization != null && authorization != declared)
                        return (new ProcessOptions(), [], "--yes and --dry-run mean opposite things; pass at most one.");
                    authorization = declared;
                    continue;
                case "project":
                case "commands":
                    if (value == null)
                    {
                        if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                            return (new ProcessOptions(), [], "--" + name + " needs a value.");
                        value = args[++i];
                    }
                    if (name == "project")
                        projectPath = value;
                    else
                        commandsFile = value;
                    continue;
                default:
                    rest.Add(args[i]);
                    continue;
            }
        }

        return (new ProcessOptions
        {
            Headless = headless,
            ProjectPath = projectPath,
            CommandsFile = commandsFile,
            Authorization = authorization ?? BridgeProtocol.AuthConfirm,
            Json = json,
        }, rest.ToArray(), null);
    }
}
