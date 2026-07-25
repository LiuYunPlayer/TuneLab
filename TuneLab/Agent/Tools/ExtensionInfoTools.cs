using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions;
using TuneLab.I18N;
using TuneLab.UI;

namespace TuneLab.Agent;

// 环境感知（只读）——插件/扩展目录。让 agent 知道用户装了哪些扩展（格式/声库/乐器/效果/模型适配），
// 以据此指导用户、判断某能力是否可用。是「诉求 3（访问插件元数据含 readme）」的地基。直接读宿主 ExtensionManager
// 的结构化加载结果，不经门面；readme 因可能很长，作按需拉取的独立工具（渐进式披露，同 get_script_api 哲学）。

// list_extensions：枚举全部已装扩展 + 每条元数据（名/id/版本/作者/类别/加载状态/是否有 readme）。
internal sealed class ListExtensionsTool : IAgentTool
{
    public string Name => "list_extensions";

    public string Description =>
        "List the TuneLab extensions (plugins) the user has installed: each one's name, id, version, author, kind(s) " +
        "(format / voice / instrument / effect / agent-model), load status, and whether it ships a README. " +
        "Use to know what the user has installed and to guide them. For a plugin's full README (features/usage), call get_extension_readme with its id or name.";

    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var results = ExtensionManager.LoadResults;
        if (results.Count == 0)
            return Task.FromResult("No extensions are installed. TuneLab is running with only its built-in capabilities.");

        var lang = TranslationManager.CurrentLanguage.Value;
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
            if (!string.IsNullOrEmpty(r.Error))
                sb.Append("\n    note: ").Append(r.Error);
            if (ExtensionReadme.Resolve(r.DirectoryPath, lang) != null)
                sb.Append("\n    README available — call get_extension_readme(\"").Append(id == "(legacy, no id)" ? r.Name : id).Append("\").");
        }
        return Task.FromResult(sb.ToString());
    }
}

// get_extension_readme：读某扩展的 README（markdown 原文），按当前语言解析（README.<lang>.md → README.md）。
// 按需拉取——readme 可能很长，只有模型确需细节时才调。
internal sealed class GetExtensionReadmeTool : IAgentTool
{
    public string Name => "get_extension_readme";

    public string Description =>
        "Return the README (markdown) of an installed extension, found by its id or name (see list_extensions). " +
        "Use to learn a plugin's features/usage before advising the user. It can be long, so call it only when you need the details.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": { "name": { "type": "string", "description": "The extension's id (preferred) or display name, as shown by list_extensions." } },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    // README 回灌上限（防超长文档淹没上下文）；超出截断并注明。
    const int MaxReadmeChars = 20000;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string query;
        try { using var doc = JsonDocument.Parse(argumentsJson); query = doc.RootElement.GetString("name"); }
        catch (Exception ex) { return Task.FromResult("Error: invalid arguments — " + ex.Message); }

        query = (query ?? "").Trim();
        if (query.Length == 0)
            return Task.FromResult("Error: \"name\" is empty.");

        ExtensionLoadResult? match = null;
        foreach (var r in ExtensionManager.LoadResults)
        {
            if (string.Equals(r.Id, query, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Name, query, StringComparison.OrdinalIgnoreCase))
            {
                match = r;
                break;
            }
        }
        if (match == null)
            return Task.FromResult("Error: no installed extension matches \"" + query + "\". Call list_extensions to see available ids/names.");

        var lang = TranslationManager.CurrentLanguage.Value;
        var path = ExtensionReadme.Resolve(match.DirectoryPath, lang);
        if (path == null)
            return Task.FromResult(string.Format("Extension \"{0}\" has no README file.", match.Name));

        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) { return Task.FromResult("Error: failed to read README — " + ex.Message); }

        if (text.Length > MaxReadmeChars)
            text = text.Substring(0, MaxReadmeChars) + "\n\n… (README truncated; " + (text.Length - MaxReadmeChars) + " more characters)";
        return Task.FromResult(string.Format("README for \"{0}\":\n\n{1}", match.Name, text));
    }
}
