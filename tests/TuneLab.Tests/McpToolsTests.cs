using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuneLab.Cli.Mcp;
using TuneLab.Commands;
using Xunit;

namespace TuneLab.Tests;

// MCP 工具面的封条。它是命令面的第三个入口（agent 工具声明 / CLI 命令树 / 这里），由 CommandRegistry
// 合成而不是另抄一张表，故这里测的不是"表对不对"，而是**合成规则**与那几条对外承诺：
//  ① 工具名对外部 agent 可见，改名等于让它照着不存在的名字调用（同 AgentToolNames 的理由）；
//  ② 每条命令恰好出现在一个工具的一个 subcommand 下——漏掉一条就是外部够不着，重复一条就是两套叫法；
//  ③ annotation 如实：客户端的批准 UI 全靠它决定问不问人；
//  ④ 参数说明必须在 tools/list 里（那是调用所必需的），而**完整说明不在**（渐进式披露）；
//  ⑤ 文本里不许残留 agent 工具面的名字——那些名字在 MCP 上根本不存在。
public class McpToolsTests
{
    // MCP 工具面的全集。加一个 group、或给某个 group 加第一条 edit 命令，都会在这里显形——
    // 那是一次对外部 agent 可见的表面变更，该由人认领。
    static readonly string[] ToolNames =
    [
        "app_read", "editor_read", "project_read", "project_edit", "script_read", "script_edit",
        "preset_read", "preset_edit", "sandbox_run", "docs_read", "setting_read", "setting_edit", "keybinding_read", "keybinding_edit",
        "action_read", "action_edit", "source_read", "effect_read", "extension_read", "extension_edit",
        "tunelab_help",
    ];

    static JsonArray Tools => McpTools.List();

    // tools/list 里的**全部文本**（描述、参数说明、枚举值…），拼成一串。
    // 【不用 ToJsonString】那一份会把 ' 与 — 之类转成 \uXXXX 转义，于是"这句话在不在里面"永远答错。
    static string ListingText()
    {
        var text = new System.Text.StringBuilder();
        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var kvp in o)
                        Walk(kvp.Value);
                    break;
                case JsonArray a:
                    foreach (var item in a)
                        Walk(item);
                    break;
                case JsonValue v when v.TryGetValue<string>(out var s):
                    text.Append(s).Append('\n');
                    break;
            }
        }
        Walk(Tools);
        return text.ToString();
    }

    [Fact]
    public void TheToolSurfaceIsTheOneExternalAgentsAlreadyKnow()
    {
        Assert.Equal(ToolNames, Tools.Select(t => t!["name"]!.GetValue<string>()).ToArray());
    }

    // 按 group × 类别合成的判据：命令数远多于工具数（那正是不一命令一工具的理由），而每条命令
    // 都够得着。
    [Fact]
    public void EveryCommandIsReachableThroughExactlyOneToolAndSubcommand()
    {
        var reached = McpTools.All.SelectMany(t => t.Commands).ToList();

        Assert.Equal(CommandRegistry.All.Count, reached.Count);
        Assert.Equal(CommandRegistry.All.OrderBy(c => c.Path), reached.OrderBy(c => c.Path));
        Assert.True(McpTools.All.Count < CommandRegistry.All.Count);
    }

    // 一个工具里的 subcommand 枚举 = 那一组该类别的全部 verb，一个不多一个不少。
    [Fact]
    public void TheSubcommandEnumIsExactlyThatGroupsVerbs()
    {
        foreach (var node in Tools)
        {
            var tool = node!.AsObject();
            var name = tool["name"]!.GetValue<string>();
            if (name == "tunelab_help")
                continue;

            Assert.True(McpTools.TryGet(name, out var declared));
            var listed = tool["inputSchema"]!["properties"]!["subcommand"]!["enum"]!.AsArray()
                .Select(v => v!.GetValue<string>()).ToArray();
            Assert.Equal(declared.Commands.Select(McpTools.Verb).ToArray(), listed);
        }
    }

    // 标注如实：read 只读、edit 会改东西、sandbox 两者都不标（它既不是只读，也不碰用户的任何东西——
    // 压进任何一边都会让客户端的批准 UI 依据一句假话）。
    [Fact]
    public void AnnotationsSayWhatTheToolActuallyDoes()
    {
        foreach (var node in Tools)
        {
            var tool = node!.AsObject();
            var annotations = tool["annotations"]!.AsObject();
            var name = tool["name"]!.GetValue<string>();

            if (name == "sandbox_run")
            {
                Assert.Empty(annotations);
                continue;
            }
            if (name.EndsWith("_read") || name == "tunelab_help")
            {
                Assert.True(annotations["readOnlyHint"]!.GetValue<bool>());
                Assert.Null(annotations["destructiveHint"]);
                continue;
            }

            Assert.EndsWith("_edit", name);
            Assert.True(annotations["destructiveHint"]!.GetValue<bool>());
            Assert.False(annotations["readOnlyHint"]!.GetValue<bool>());
        }
    }

    // 参数名【必须】在 tools/list 里：arguments 是个自由 object（形状由 §9.3 定死），agent 不知道字段名
    // 就没法调用。这一条与下一条是一对：参数进 tools/list，完整说明不进。
    [Fact]
    public void EveryParameterNameIsInTheToolsListing()
    {
        foreach (var node in Tools)
        {
            var tool = node!.AsObject();
            var name = tool["name"]!.GetValue<string>();
            if (name == "tunelab_help")
                continue;

            var arguments = tool["inputSchema"]!["properties"]!["arguments"]!["description"]!.GetValue<string>();
            Assert.True(McpTools.TryGet(name, out var declared));
            foreach (var command in declared.Commands)
            {
                using var schema = JsonDocument.Parse(command.ParametersJsonSchema);
                if (!schema.RootElement.TryGetProperty("properties", out var properties))
                    continue;
                foreach (var property in properties.EnumerateObject())
                    Assert.Contains(property.Name, arguments);
            }
        }
    }

    // 渐进式披露：tools/list 带的是一行摘要，不是 24K 字符的全文——那会让每次会话先吃掉几千 token，
    // 正是"按 group 合成"想省下的那部分。全文走 tunelab_help。
    [Fact]
    public void TheListingCarriesTheBriefAndNotTheWholeManual()
    {
        var listing = ListingText();
        foreach (var command in CommandRegistry.All)
        {
            Assert.Contains(McpText.ForMcp(command.Brief), listing);
            // 文档的第一句（截到第一个句号）足以认出全文有没有被搬进来。
            var opening = command.Documentation.Split('.')[0];
            if (opening.Length > 40)
                Assert.DoesNotContain(opening, listing);
        }
    }

    // tunelab_help 覆盖每一条命令，并且告诉调用方【这条命令从哪个工具的哪个 subcommand 调】——
    // 否则读完全文还是不知道怎么发起这次调用。
    [Fact]
    public void HelpCoversEveryCommandAndSaysHowToCallIt()
    {
        var listed = Tools.Last()!["inputSchema"]!["properties"]!["command"]!["enum"]!.AsArray()
            .Select(v => v!.GetValue<string>()).ToArray();
        Assert.Equal(CommandRegistry.All.Select(c => c.Path).ToArray(), listed);

        foreach (var command in CommandRegistry.All)
        {
            var help = McpTools.Help(command);
            var tool = McpTools.All.First(t => t.Commands.Contains(command));
            Assert.Contains("call it as " + tool.Name + " with subcommand \"" + McpTools.Verb(command) + "\"", help);
            Assert.Contains(McpText.ForMcp(command.Documentation), help);
        }
    }

    // 换名要彻底：agent 工具面上的名字（list_settings…）在 MCP 上根本不存在，照原样留在文本里
    // 等于让 agent 去调一个 tools/list 里查不到的工具。
    [Fact]
    public void NoAgentToolNameSurvivesIntoTheMcpSurface()
    {
        var surface = ListingText() + string.Join("\n", CommandRegistry.All.Select(McpTools.Help));
        foreach (var agentToolName in CommandRegistry.PathsByAgentToolName.Keys)
            Assert.DoesNotContain(agentToolName, surface);
    }

    // 换名换成的是 MCP 自己的叫法（工具名 + subcommand），不是 CLI 的 "tunelab setting list"。
    [Fact]
    public void RenamingPointsAtThisEntryPointsOwnNames()
    {
        Assert.Equal("setting_read with subcommand \"list\"", McpText.ForMcp("list_settings"));
        Assert.Equal("Call script_edit with subcommand \"run\" (code) first.", McpText.ForMcp("Call run_script(code) first."));
    }
}
