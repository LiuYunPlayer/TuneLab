using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using TuneLab.Commands;

namespace TuneLab.Cli.Mcp;

// MCP 的工具面：**由 CommandRegistry 合成**，不另抄一张表（同 CLI 的命令树、同 agent 的工具声明——
// 三个入口各自自省同一个真源，故结构上不可能漂移）。
//
// 【按 group × 类别合成，不是一命令一工具】33 条命令各带完整 JSON Schema 会挤占 agent 的工具选择
// 上下文，而且随能力增长线性膨胀；按 group 合成让工具数随**组**增长而不是随**叶子**增长。
// 反过来也不折成单个 invoke_command：那样 tools/list 只剩一个不透明的洞，agent 不预先知道就发现不了
// 能力，且安全标注退化成"一律按最坏情况标"，客户端的批准 UI 就失去意义了（docs/command-surface.md §9.3）。
//
// 【tools/list 只带 Brief，全文按需】33 条命令的 Documentation 合起来 24K 字符，全塞进 tools/list 等于
// 每次会话都先吃掉几千 token；而参数说明是【调用所必需】的，故它留在 schema 里。完整说明走 tunelab_help
// ——同 get_script_api / get_manual 的渐进式披露。
internal static class McpTools
{
    // 一个工具：名字 + 它合成自哪几条命令。
    internal sealed record Tool(string Name, string Group, CommandKind Kind, IReadOnlyList<ICommand> Commands);

    public const string HelpToolName = "tunelab_help";

    // 工具全集，呈现序 = 注册表的声明序（group 内 read 在前）。
    public static IReadOnlyList<Tool> All { get; } = Build();

    static IReadOnlyList<Tool> Build()
    {
        var tools = new List<Tool>();
        foreach (var group in CommandRegistry.Groups)
        {
            foreach (var kind in new[] { CommandKind.Read, CommandKind.Edit, CommandKind.Sandbox })
            {
                var commands = CommandRegistry.All.Where(c => CommandRegistry.GroupOf(c) == group && c.Kind == kind).ToList();
                if (commands.Count != 0)
                    tools.Add(new Tool(group + "_" + Suffix(kind), group, kind, commands));
            }
        }
        return tools;
    }

    // 类别 → 工具名后缀。sandbox 那条既非只读也不碰用户数据，压进 read/edit 任何一边都会让安全标注
    // 说谎（见 CommandKind），故它自成一个后缀。
    static string Suffix(CommandKind kind) => kind switch
    {
        CommandKind.Read => "read",
        CommandKind.Edit => "edit",
        _ => "run",
    };

    public static bool TryGet(string toolName, out Tool tool)
    {
        foreach (var t in All)
        {
            if (t.Name == toolName)
            {
                tool = t;
                return true;
            }
        }
        tool = null!;
        return false;
    }

    // 一个工具里的 subcommand → 命令。认不出的名字由调用方报错（列出这个工具接受的全集）。
    public static bool TryGetCommand(Tool tool, string subcommand, out ICommand command)
    {
        foreach (var c in tool.Commands)
        {
            if (Verb(c) == subcommand)
            {
                command = c;
                return true;
            }
        }
        command = null!;
        return false;
    }

    public static string Verb(ICommand command)
    {
        int i = command.Path.IndexOf(' ');
        return i < 0 ? command.Path : command.Path.Substring(i + 1);
    }

    // tools/list 的载荷。
    public static JsonArray List()
    {
        var tools = new JsonArray();
        foreach (var tool in All)
            tools.Add(Describe(tool));
        tools.Add(HelpTool());
        return tools;
    }

    static JsonObject Describe(Tool tool)
    {
        var subcommands = new JsonArray();
        var description = new StringBuilder();
        description.Append(Headline(tool));
        var arguments = new StringBuilder("Parameters of the chosen subcommand, as an object. ");

        foreach (var command in tool.Commands)
        {
            var verb = Verb(command);
            subcommands.Add(verb);
            description.Append("\n· ").Append(verb).Append(" — ").Append(McpText.ForMcp(command.Brief));
            AppendParameters(arguments, verb, command);
        }
        description.Append("\nCall ").Append(HelpToolName).Append(" for the full manual of any of these before using it the first time.");

        return new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = description.ToString(),
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["subcommand"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = subcommands,
                        ["description"] = "Which of this group's commands to run.",
                    },
                    ["arguments"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["description"] = arguments.ToString(),
                    },
                },
                ["required"] = new JsonArray { "subcommand" },
            },
            // 如实标注：客户端的批准 UI 靠它决定问不问人（这个入口自己没法问，见 McpServer）。
            // read 只读；edit 可能改用户的数据、且不都是可撤销的，故标 destructive；
            // sandbox 两者都不标——它既不是只读，也不碰用户的任何东西。
            ["annotations"] = Annotations(tool.Kind),
        };
    }

    static JsonObject Annotations(CommandKind kind)
    {
        var annotations = new JsonObject();
        if (kind == CommandKind.Read)
        {
            annotations["readOnlyHint"] = true;
        }
        else if (kind == CommandKind.Edit)
        {
            annotations["destructiveHint"] = true;
            annotations["readOnlyHint"] = false;
        }
        return annotations;
    }

    static string Headline(Tool tool) => tool.Kind switch
    {
        CommandKind.Read => string.Format("TuneLab's \"{0}\" commands that only read — nothing changes. Pick one with \"subcommand\":", tool.Group),
        CommandKind.Edit => string.Format(
            "TuneLab's \"{0}\" commands that CHANGE something (the user's project, their app configuration, or a file on disk). "
            + "They need the user's authorization, which is set inside TuneLab and cannot be raised from here. Pick one with \"subcommand\":", tool.Group),
        _ => string.Format(
            "TuneLab's \"{0}\" commands, which run in a throwaway project — they never touch the user's own work, so they need no authorization. "
            + "Pick one with \"subcommand\":", tool.Group),
    };

    // 参数签名进 arguments 的描述：不同 subcommand 的参数不同，一个静态 schema 描述不了这件事
    // （形状由 §9.3 定死为 {subcommand, arguments}），故把每条的参数如实写进说明——**这是调用所必需的**，
    // 与"什么时候该用它"那类说明不同，不能推给 tunelab_help。
    static void AppendParameters(StringBuilder sb, string verb, ICommand command)
    {
        sb.Append("\n· ").Append(verb).Append(": ");
        var schema = System.Text.Json.JsonDocument.Parse(command.ParametersJsonSchema);
        if (!schema.RootElement.TryGetProperty("properties", out var properties) || !properties.EnumerateObject().Any())
        {
            sb.Append("takes no parameters.");
            return;
        }

        var required = schema.RootElement.TryGetProperty("required", out var r)
            ? r.EnumerateArray().Select(x => x.GetString()).ToHashSet()
            : [];
        foreach (var property in properties.EnumerateObject())
        {
            sb.Append("\n    ").Append(property.Name).Append(" (").Append(string.Join("|", Types(property.Value)));
            if (required.Contains(property.Name))
                sb.Append(", required");
            sb.Append(')');
            if (property.Value.TryGetProperty("description", out var description))
                sb.Append(" — ").Append(McpText.ForMcp(description.GetString() ?? ""));
        }
    }

    static IEnumerable<string> Types(System.Text.Json.JsonElement declared)
    {
        if (!declared.TryGetProperty("type", out var type))
            return ["string"];
        return type.ValueKind == System.Text.Json.JsonValueKind.Array
            ? type.EnumerateArray().Select(t => t.GetString() ?? "string").ToList()
            : [type.GetString() ?? "string"];
    }

    // 渐进式披露的出口：tools/list 里每条命令只有一行摘要，完整说明（什么时候用它、它拒绝做什么、
    // 结果怎么读）在这里按需取。它不连宿主——文档是程序自带的。
    static JsonObject HelpTool()
    {
        var commands = new JsonArray();
        foreach (var command in CommandRegistry.All)
            commands.Add(command.Path);

        return new JsonObject
        {
            ["name"] = HelpToolName,
            ["description"] =
                "The full manual for ONE TuneLab command: what it is for, what it refuses to do and why, and every parameter in detail. "
                + "The tool list only carries a one-line summary of each command, so read this before using a command for the first time — guessing at a command that edits the user's work is how you make a mess of it. "
                + "This one answers without TuneLab running.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["command"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = commands,
                        ["description"] = "The command's path, e.g. \"project export\" — the same words as the tool's group and its \"subcommand\".",
                    },
                },
                ["required"] = new JsonArray { "command" },
                ["additionalProperties"] = false,
            },
            ["annotations"] = new JsonObject { ["readOnlyHint"] = true },
        };
    }

    // tunelab_help 的答案：完整说明 + 参数明细 + 这条命令从哪个工具的哪个 subcommand 调。
    public static string Help(ICommand command)
    {
        var tool = All.First(t => t.Commands.Contains(command));
        var sb = new StringBuilder();
        sb.Append(command.Path).Append("  —  call it as ").Append(tool.Name)
          .Append(" with subcommand \"").Append(Verb(command)).Append("\"");
        if (command.Kind != CommandKind.Read)
            sb.Append("  [").Append(command.Kind.ToString().ToLowerInvariant()).Append(']');
        sb.Append("\n\n").Append(McpText.ForMcp(command.Documentation)).Append("\n\nParameters:");

        var schema = System.Text.Json.JsonDocument.Parse(command.ParametersJsonSchema);
        if (!schema.RootElement.TryGetProperty("properties", out var properties) || !properties.EnumerateObject().Any())
        {
            sb.Append(" none.");
            return sb.ToString();
        }

        var required = schema.RootElement.TryGetProperty("required", out var r)
            ? r.EnumerateArray().Select(x => x.GetString()).ToHashSet()
            : [];
        foreach (var property in properties.EnumerateObject())
        {
            sb.Append("\n  ").Append(property.Name).Append(" <").Append(string.Join("|", Types(property.Value))).Append('>');
            if (required.Contains(property.Name))
                sb.Append("  (required)");
            if (property.Value.TryGetProperty("description", out var description))
                sb.Append("\n      ").Append(McpText.ForMcp(description.GetString() ?? ""));
        }
        return sb.ToString();
    }
}
