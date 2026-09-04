using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Bridge;
using TuneLab.Commands;

namespace TuneLab.Cli;

// tunelab 命令行：`tunelab <group> <verb> [--flag value ...]`。
//
// 它只是命令面的又一个入口——末端动作、措辞、闸门流程一概在命令面，这里只做四件事：
// 解析参数、连桥、把结果打出来、给一个诚实的退出码。
//
// 【--help 不连宿主】命令树来自 CommandRegistry（纯声明，不启动宿主也自省得出），故列命令、看某条
// 命令的参数、校验参数名都在本地完成。真要执行才连桥。
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
        var positional = args.TakeWhile(a => !a.StartsWith('-')).ToArray();
        bool wantsHelp = args.Contains("--help") || args.Contains("-h");

        if (positional.Length == 0)
        {
            PrintCommandTree(null);
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

        var (arguments, authorization, json, error) = ParseOptions(command, args.Skip(2).ToArray());
        if (error != null)
        {
            Console.Error.WriteLine("tunelab: " + error);
            Console.Error.WriteLine("Run \"tunelab " + path + " --help\" for this command's parameters.");
            return ExitUsage;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

        var (client, connectError) = await BridgeClient.ConnectAsync(cancellation.Token);
        if (client == null)
        {
            Console.Error.WriteLine("tunelab: " + connectError);
            return ExitNoHost;
        }

        using (client)
        {
            var (result, failure) = await client.RequestAsync(BridgeProtocol.MethodCommandExecute, new JsonObject
            {
                ["path"] = path,
                ["arguments"] = arguments,
                ["authorization"] = authorization,
            }, Confirm, cancellation.Token);

            if (failure != null)
            {
                Console.Error.WriteLine(failure);
                return ExitCommandFailed;
            }

            var payload = result as JsonObject;
            if (json)
                Console.Out.WriteLine((payload?["data"] ?? JsonValue.Create((object?)null))?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null");
            else
                Console.Out.WriteLine(payload?["text"]?.GetValue<string>() ?? "");
            return ExitOk;
        }
    }

    // 宿主问"这次写做不做"。打到 stderr、读 stdin：stdout 留给结果，方便管道。
    static JsonObject Confirm(JsonObject request)
    {
        var phrase = (request["params"] as JsonObject)?["actionPhrase"]?.GetValue<string>() ?? "make this change";
        // 非交互场景（CI、管道）不能停在这里等一个永远不来的回车——如实拒绝并告诉调用方用哪个开关。
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("tunelab: TuneLab asks to confirm: " + phrase);
            Console.Error.WriteLine("tunelab: stdin is not interactive, so nothing was changed. Pass --yes to allow, or --dry-run to only see the plan.");
            return new JsonObject { ["decision"] = BridgeProtocol.DecisionReject };
        }

        Console.Error.WriteLine("TuneLab asks to confirm: " + phrase);
        Console.Error.Write("[y] once  [a] always (switches TuneLab to auto-apply)  [N] reject: ");
        var answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        return new JsonObject
        {
            ["decision"] = answer switch
            {
                "y" or "yes" => BridgeProtocol.DecisionOnce,
                "a" or "always" => BridgeProtocol.DecisionAlways,
                _ => BridgeProtocol.DecisionReject,
            },
        };
    }

    // 解析这一条命令的参数：schema 说什么类型就转成什么类型（CLI 的 flag 全是字符串，而
    // `--reset` 这种布尔要能省略值）。认不出的参数名当用法错——静默忽略会让人以为设置生效了。
    static (JsonObject Arguments, string Authorization, bool Json, string? Error) ParseOptions(ICommand command, string[] options)
    {
        var arguments = new JsonObject();
        var authorization = BridgeProtocol.AuthConfirm;
        bool json = false;

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

    static void PrintCommandTree(string? onlyGroup)
    {
        Console.Out.WriteLine("tunelab — drive a running TuneLab from the terminal.");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Usage: tunelab <group> <verb> [--name value ...] [--json] [--yes | --dry-run]");
        Console.Out.WriteLine("       tunelab <group> <verb> --help     what one command does and which parameters it takes");
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
