using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

        if (options.Search != null)
        {
            if (positional.Length > 0)
            {
                Console.Error.WriteLine("tunelab: --search looks through the command help; don't also name a command.");
                return ExitUsage;
            }
            return PrintSearch(options.Search);
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

            return await WithRunner(options, NeedsHost(lines), cancellation.Token,
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

        return await WithRunner(options, command.NeedsHost, cancellation.Token, async runner =>
        {
            WarnIfUnattended(command, authorization);
            WarnIfCapped(command, authorization);
            var outcome = await runner.RunAsync(command, arguments, authorization, CanAsk, cancellation.Token);
            if (outcome.Failure != null)
            {
                Console.Error.WriteLine(CommandText.ForCli(outcome.Failure));
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
            WarnIfCapped(command, authorization);

            var outcome = await runner.RunAsync(command, arguments, authorization, CanAsk, cancellationToken);
            if (outcome.Failure != null)
            {
                Console.Error.WriteLine("tunelab: line " + number + ": " + CommandText.ForCli(outcome.Failure));
                Console.Error.WriteLine(string.Format("tunelab: stopped at \"{0}\"; {1} command(s) ran before it.", path, ran));
                return ExitCommandFailed;
            }
            Print(outcome, json);
            ran++;
        }

        Console.Error.WriteLine(string.Format("tunelab: {0} command(s) ok.", ran));
        return ExitOk;
    }

    // 这一趟要不要宿主：清单里只要有一条需要就要。认不出的行【当作需要】——那条行的用法错该由正常
    // 那条路按行号报出来，不该在这里被"反正不用连"顺手咽掉。
    static bool NeedsHost(IReadOnlyList<(int Number, string Text)> lines)
    {
        foreach (var (_, text) in lines)
        {
            var tokens = Tokenize(text, out var error);
            if (error != null || tokens.Count < 2 || !CommandRegistry.TryGet(tokens[0] + " " + tokens[1], out var command))
                return true;
            if (command.NeedsHost)
                return true;
        }
        return false;
    }

    static int BatchUsageError(int number, string message)
    {
        Console.Error.WriteLine("tunelab: line " + number + ": " + message);
        return ExitUsage;
    }

    static void Print(CommandOutcome outcome, bool json)
    {
        if (json)
            // 【Data 不换名】那是机器契约（CI 的断言、外部工具的解析都按它写），字段值不该随入口变形。
            Console.Out.WriteLine((outcome.Data ?? JsonValue.Create((object?)null))?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null");
        else
            Console.Out.WriteLine(CommandText.ForCli(outcome.Text));
    }

    // 命令送去哪儿跑。headless 那条路整趟都在无头宿主的那条线程上（body 也是），故这里是同步等它跑完。
    static async Task<int> WithRunner(ProcessOptions options, bool needsHost, CancellationToken cancellationToken, Func<ICommandRunner, Task<int>> body)
    {
        var authorization = new AuthorizationState();

        // 这一趟一条命令都不需要宿主（只读 API 参考 / 查手册）→ 什么都不启动，也不要求 TuneLab 开着。
        // 【连 --headless 也不起】起了也观察不到任何差别，只是白等一次插件加载；--project 同理无从生效。
        if (!needsHost)
            return await body(new LocalRunner());

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
        {
            mCeiling = client.Ceiling;
            return await body(new BridgeRunner(client, authorization));
        }
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

    // attach 时宿主在握手里回它当前的授权天花板：--yes 越不过它。不说这一句的话，CI 里的人只看到
    // "没做"，会去怀疑命令本身。整趟只说一次；headless 那条路没有天花板（那是调用方自己的进程、
    // 自己打开的工程，不碰用户此刻开着的会话），故 mCeiling 保持 null。
    static string? mCeiling;
    static bool mWarnedCapped;
    static void WarnIfCapped(ICommand command, string authorization)
    {
        if (mWarnedCapped || command.Kind != CommandKind.Edit || authorization != BridgeProtocol.AuthAuto)
            return;
        if (mCeiling == null || mCeiling == BridgeProtocol.AuthAuto)
            return;
        mWarnedCapped = true;
        Console.Error.WriteLine(mCeiling == BridgeProtocol.AuthReadOnly
            ? "tunelab: TuneLab's agent authorization is set to read-only advice, and that is the ceiling for --yes: writes are reported but NOT applied."
            : "tunelab: TuneLab's agent authorization is set to confirm, and that is the ceiling for --yes: you are still asked before anything is applied.");
        Console.Error.WriteLine("tunelab: only the user can raise it, in the AI Agent panel header inside TuneLab.");
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

            var (coerced, coerceError) = Coerce(name, value, types);
            if (coerceError != null)
                return (arguments, authorization, json, coerceError);
            arguments[name] = coerced;
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

    static (JsonNode? Value, string? Error) Coerce(string name, string value, string[] types)
    {
        if (types.Contains("boolean") && bool.TryParse(value, out var b))
            return (b, null);
        if ((types.Contains("number") || types.Contains("integer")) && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
            return (d, null);
        if (types.Contains("object") || types.Contains("array"))
        {
            // 对象/数组参数（如 run-saved 的 inputs）直接收 JSON 文本。
            //
            // 【解析不了就是用法错】以前是"按字符串送过去，让命令自己报错"，而命令那边收到一个不是
            // 对象的 inputs 只会无声地忽略它 —— 于是脚本拿默认值照跑、回报还是"跑成功了"。那是最坏的
            // 一种失败：自动化测试会"通过"，测的却是默认值（实测正是这么栽的：Windows 路径里的反斜杠
            // 被 shell 吃掉一层，JSON 就此不合法）。故在这里当场拦下，并把那个坑说出来。
            try { return (JsonNode.Parse(value), null); }
            catch (JsonException ex)
            {
                return (null, "\"" + name + "\" takes JSON, and this is not valid JSON — " + ex.Message
                    + "\nOn Windows: a backslash inside a JSON string must be doubled (\"C:\\\\path\\\\x.wav\"), and your shell may"
                    + " eat one level of that on top — forward slashes work everywhere and avoid the whole question.");
            }
        }
        return (value, null);
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

    // `--search`：把全部命令的帮助语料（Brief + 完整说明）过一遍正则。离线（注册表是纯声明），故
    // 不连宿主也能用——"有没有一条命令能干这件事"是外部 agent 最先要问的问题，不该以启动为前提。
    // 匹配到就给一段上下文而不是整篇说明：命令说明是长句，整篇打出来等于让人自己再找一遍。
    static int PrintSearch(string pattern)
    {
        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.IgnoreCase); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("tunelab: --search takes a regular expression, and \"" + pattern + "\" is not one — " + ex.Message);
            return ExitUsage;
        }

        var matched = new List<ICommand>();
        var excerpts = new List<string>();
        foreach (var command in CommandRegistry.All)
        {
            var brief = CommandText.ForCli(command.Brief);
            var documentation = CommandText.ForCli(command.Documentation);
            if (!regex.IsMatch(brief) && !regex.IsMatch(documentation))
                continue;
            matched.Add(command);
            excerpts.Add(Excerpt(regex, documentation));
        }

        if (matched.Count == 0)
        {
            Console.Out.WriteLine("No command's help matches \"" + pattern + "\". Run \"tunelab --help\" for the whole command tree.");
            return ExitOk;
        }

        Console.Out.WriteLine(string.Format("{0} command(s) whose help matches \"{1}\":", matched.Count, pattern));
        Console.Out.WriteLine();
        for (int i = 0; i < matched.Count; i++)
        {
            Console.Out.WriteLine(matched[i].Path.PadRight(26) + CommandText.ForCli(matched[i].Brief)
                + (matched[i].Kind == CommandKind.Read ? "" : "  [" + matched[i].Kind.ToString().ToLowerInvariant() + "]"));
            if (excerpts[i].Length > 0)
                Console.Out.WriteLine("  " + excerpts[i]);
        }
        Console.Out.WriteLine();
        Console.Out.WriteLine("Read one command in full with \"tunelab <group> <verb> --help\".");
        return ExitOk;
    }

    // 命中处前后各一段。命令说明是不换行的长句，故按字符取窗口而不是按行。
    static string Excerpt(Regex regex, string text)
    {
        var match = regex.Match(text);
        if (!match.Success)
            return string.Empty;
        int start = Math.Max(0, match.Index - 60);
        int end = Math.Min(text.Length, match.Index + match.Length + 60);
        return (start > 0 ? "…" : "") + text[start..end].Replace('\n', ' ') + (end < text.Length ? "…" : "");
    }

    static void PrintCommandTree(string? onlyGroup)
    {
        Console.Out.WriteLine("tunelab — drive TuneLab from the terminal.");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Usage: tunelab <group> <verb> [--name value ...] [--json] [--yes | --dry-run]");
        Console.Out.WriteLine("       tunelab <group> <verb> --help     what one command does and which parameters it takes");
        Console.Out.WriteLine("       tunelab --commands <file|->       run a list of commands (one per line, # comments) in one go");
        Console.Out.WriteLine("       tunelab --search <regex>          which command mentions this? (searches every command's help)");
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
                Console.Out.WriteLine("  " + command.Path.PadRight(26) + CommandText.ForCli(command.Brief)
                    + (command.Kind == CommandKind.Read ? "" : "  [" + command.Kind.ToString().ToLowerInvariant() + "]"));
        }
        Console.Out.WriteLine();
        Console.Out.WriteLine("Anything marked [edit] changes the user's data and needs authorization: you are asked here on stdin,");
        Console.Out.WriteLine("unless you pass --yes (do it) or --dry-run (only report what would change).");
        Console.Out.WriteLine("--yes never gets more than the user's own authorization setting inside TuneLab: if that is set to confirm");
        Console.Out.WriteLine("you are still asked, and if it is set to read-only advice nothing is applied at all.");
        Console.Out.WriteLine("Exit codes: 0 ok, 1 the command failed, 2 wrong usage, 3 TuneLab is not reachable.");
    }

    static void PrintCommandHelp(ICommand command)
    {
        Console.Out.WriteLine(command.Path + "  [" + command.Kind.ToString().ToLowerInvariant() + "]");
        Console.Out.WriteLine();
        Console.Out.WriteLine(CommandText.ForCli(command.Documentation));
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
                Console.Out.WriteLine("      " + CommandText.ForCli(description.GetString() ?? ""));
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
    public string? Search { get; init; }
    public string Authorization { get; init; } = BridgeProtocol.AuthConfirm;
    public bool Json { get; init; }

    // CLI 自己占掉的参数名【全集】——进程级这三个，加上可以逐条覆盖的那几个（见 Program.ParseOptions）。
    // 命令的参数名不能与它们撞车：撞了就再也传不进去，且用户看不出为什么。CommandRegistryTests 有一条
    // 封条盯着，撞了当场红。改下面的 switch 时同步改这份清单。
    public static readonly string[] Names = ["headless", "project", "commands", "search", "json", "yes", "dry-run", "help"];

    public static (ProcessOptions Options, string[] Remaining, string? Error) Extract(string[] args)
    {
        bool headless = false, json = false;
        string? projectPath = null, commandsFile = null, search = null, authorization = null;
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
                case "search":
                    if (value == null)
                    {
                        if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                            return (new ProcessOptions(), [], "--" + name + " needs a value.");
                        value = args[++i];
                    }
                    if (name == "project")
                        projectPath = value;
                    else if (name == "commands")
                        commandsFile = value;
                    else
                        search = value;
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
            Search = search,
            Authorization = authorization ?? BridgeProtocol.AuthConfirm,
            Json = json,
        }, rest.ToArray(), null);
    }
}
