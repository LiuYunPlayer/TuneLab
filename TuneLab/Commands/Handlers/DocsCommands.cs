using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Docs;
using TuneLab.Scripting;

namespace TuneLab.Commands.Handlers;

// `docs` group：按需取文档（渐进式披露）。刻意不进普通 group——它们不是对工程或配置的操作，
// 而是"查阅"，产物是给人/模型读的文本。
//
// 【Data 形状的一处例外】其余 read 命令的 Data 是结构化事实（见 docs/command-surface.md §4.1），
// 文档类不是：产物本来就是文本（markdown 章节拆成 JSON 不增加任何信息）。故这里 Data 以 text 为主、
// 另附元数据；唯一结构化的是手册检索的命中列表——它的源头（ManualLibrary.Search）本就是三元组。

// run_script 的 API 参考：完整 API/规则/示例放这里，调用方决定要写脚本时取一次，
// 避免把整份速查表常驻每次请求的 prompt。
internal sealed class DocsScriptApiCommand : ICommand
{
    public string Path => "docs script-api";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "get_script_api";
    // 正文是编译进来的常量，与工程、设置、扩展一概无关。
    public bool NeedsHost => false;

    public string Brief => "Full `tl` API reference for script run";

    public string Documentation =>
        "Return the full API reference for run_script (the `tl` action surface): every method with signatures, the handle/tick/commit rules, " +
        "handle fields, and worked examples. Call this once before writing your first run_script program in a conversation.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    public Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
        => Task.FromResult(CommandResult.Ok(new JsonObject { ["text"] = ScriptApiReference.Text }));

    public string Render(JsonNode? data, CommandArgs args)
        => data is JsonObject obj ? obj["text"]?.GetValue<string>() ?? string.Empty : string.Empty;
}

// 随包用户手册的按需查阅：无参给目录、query 检索、section 取整章。
// 回答界面用法必须**以手册为依据**而不是凭印象——手册与软件同版本，模型的记忆不是。
//
// 与自省类命令的分工：`setting list` / `keybinding list` 报的是「此刻这台机器上是什么值、在哪一行」，
// 手册讲的是「这个功能是什么、怎么配合别的功能用」。
internal sealed class DocsManualCommand : ICommand
{
    public string Path => "docs manual";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "get_manual";
    // 正文是随包的资源文件（Resources/Manual），就在可执行文件旁边。
    // 【一处已知差异】哪一语言版由界面语言选（ManualLibrary）：就地答时那个值还没被宿主设过，
    // 于是回退到进程的 UI 文化。回报的表头本来就点明"这是回退版本、照用户的语言回答"，故不误导；
    // 真要与宿主一字不差，连桥问它。
    public bool NeedsHost => false;

    public string Brief => "Look up the bundled user manual (toc / search / one chapter)";

    public string Documentation =>
        "Look up the bundled TuneLab user manual - the authoritative source for how the editor is operated " +
        "(UI areas, tools and their mouse gestures, sidebar pages, settings, files, extensions). " +
        "Call with no arguments for the table of contents, with `query` to search, or with `section` to read one chapter in full. " +
        "Use this before answering any \"how do I ...\" / \"where is ...\" question about the editor instead of relying on memory. " +
        "For live per-machine values (current shortcut bindings, current setting values) use the list_* tools instead.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "section": {
              "type": "string",
              "description": "Chapter id (or a title/subheading substring) to read in full, e.g. \"parameters\". Omit to get the table of contents."
            },
            "query": {
              "type": "string",
              "description": "Keyword to search across the manual. Returns matching lines with the chapter they live in."
            }
          },
          "additionalProperties": false
        }
        """;

    public Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        // 手册没随包不是错误：如实报"没有"，让调用方改用自省类命令（同搬家前的行为，故不走 CommandError——
        // 那会给 agent 侧的文本套上 "Error: " 前缀，等于改了行为）。
        if (!ManualLibrary.IsAvailable)
            return Task.FromResult(CommandResult.Ok(new JsonObject { ["available"] = false }));

        var section = args.Json.GetStringOrNull("section");
        var query = args.Json.GetStringOrNull("query");

        var data = new JsonObject
        {
            ["available"] = true,
            ["language"] = ManualLibrary.Language,
            ["isCurrentLanguage"] = ManualLibrary.IsCurrentLanguage,
        };

        if (!string.IsNullOrWhiteSpace(section))
        {
            data["mode"] = "section";
            data["section"] = section;
            var found = ManualLibrary.Find(section!);
            data["found"] = found != null;
            // 没找到时给目录，让调用方直接看到有哪些章可选（同搬家前）。
            data["text"] = found != null ? ManualLibrary.ForModel(found) : ManualLibrary.BuildToc();
            if (found != null)
            {
                data["id"] = found.Id;
                data["title"] = found.Title;
            }
            return Task.FromResult(CommandResult.Ok(data));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            data["mode"] = "search";
            data["query"] = query;
            var hits = ManualLibrary.Search(query!);
            data["found"] = hits.Count > 0;
            if (hits.Count > 0)
            {
                var array = new JsonArray();
                foreach (var hit in hits)
                    array.Add(new JsonObject
                    {
                        ["section"] = hit.SectionId,
                        ["title"] = hit.SectionTitle,
                        ["line"] = hit.Line,
                    });
                data["hits"] = array;
            }
            else
            {
                data["text"] = ManualLibrary.BuildToc();
            }
            return Task.FromResult(CommandResult.Ok(data));
        }

        data["mode"] = "toc";
        data["text"] = ManualLibrary.BuildToc();
        return Task.FromResult(CommandResult.Ok(data));
    }

    // 措辞与搬家前逐字一致（含"未命中时不带 Header"这个细节——原实现的 ReadSection 未命中分支就没有它）。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        if (!(obj["available"]?.GetValue<bool>() ?? false))
            return "The user manual is not bundled with this build (Resources/Manual is missing). "
                + "Answer from the introspection tools instead, and say that the manual is unavailable.";

        var mode = obj["mode"]?.GetValue<string>();
        bool found = obj["found"]?.GetValue<bool>() ?? true;
        string text = obj["text"]?.GetValue<string>() ?? string.Empty;
        var sb = new StringBuilder();

        if (mode == "section")
        {
            if (!found)
            {
                sb.Append("No chapter matches \"").Append(obj["section"]?.GetValue<string>()).AppendLine("\". Available chapters:");
                sb.Append(text);
                return sb.ToString();
            }
            return Header(obj) + "\n" + text;
        }

        if (mode == "search")
        {
            sb.Append(Header(obj));
            var queryText = obj["query"]?.GetValue<string>();
            if (!found)
            {
                sb.Append("No line matches \"").Append(queryText).AppendLine("\". Chapters available:");
                sb.Append(text);
                return sb.ToString();
            }

            sb.Append("Lines matching \"").Append(queryText).AppendLine("\" (read the whole chapter with section=<id> for context):");
            sb.AppendLine();
            foreach (var group in obj["hits"]!.AsArray().GroupBy(h => h!["section"]!.GetValue<string>()))
            {
                sb.Append("[").Append(group.Key).Append("] ").AppendLine(group.First()!["title"]!.GetValue<string>());
                foreach (var hit in group)
                    sb.Append("  ").AppendLine(hit!["line"]!.GetValue<string>());
            }
            return sb.ToString();
        }

        sb.Append(Header(obj));
        sb.AppendLine("Chapters (id — title (subsections)). Read one with section=<id>, or search with query=<keyword>:");
        sb.AppendLine();
        sb.Append(text);
        return sb.ToString();
    }

    static string Header(JsonObject obj)
    {
        var sb = new StringBuilder();
        sb.Append("TuneLab user manual (language: ").Append(obj["language"]?.GetValue<string>()).Append(')');
        if (!(obj["isCurrentLanguage"]?.GetValue<bool>() ?? true))
            sb.Append(" - no edition in the current UI language, so this is a fallback edition; "
                + "answer the user in their own language regardless of the manual's language");
        sb.AppendLine(".");
        return sb.ToString();
    }
}
