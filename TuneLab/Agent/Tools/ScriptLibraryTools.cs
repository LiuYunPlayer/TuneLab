using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Data;
using TuneLab.Scripting;

namespace TuneLab.Agent;

// 脚本库管理工具组：让模型把用户描述的功能写成一个【工具脚本】（定义 getScriptInfo + main）存进脚本库，
// 即自动注册进对应菜单（global / note / part / partContent），用户日后直接点菜单复用——无需每次让 agent 重跑。
// 保存只持久化源码、不执行（安全）；保存工具脚本前先预校验 getScriptInfo 可解析，并回报注册到了哪个菜单。
// 与 run_script（运行一次）互补：要"可复用的功能/命令"用 save_script，要"现在做一次"用 run_script。

// 保存（新建或覆盖）一个脚本到库。覆盖已存脚本 = 破坏用户外部文件（历史管理器救不回）→ 过授权闸门；新建不拦。
internal sealed class SaveScriptTool(IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language, Func<AgentAuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm = null) : IAgentTool
{
    public string Name => "save_script";

    public string Description =>
        "Save a REUSABLE script tool into the user's script library so it appears in TuneLab's menus for one-click reuse later. " +
        "Use this when the user wants a feature/command they can run again (\"add a menu item/button that …\", \"make me a tool to …\"), instead of run_script which runs once. " +
        "To become a menu tool the script must define getScriptInfo() (name/category/context) and main() — call get_script_api for the exact convention and which menu each context maps to. " +
        "Saving does NOT run the script. If a script with the same name exists, OVERWRITING it needs the user's authorization (it replaces their file, which can't be undone) — the result tells you what happened. " +
        "A script without getScriptInfo is saved as a plain run-once script (Script side panel only).";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Library name = file name without .js; reused as the identifier. Pick a short stable slug." },
            "code": { "type": "string", "description": "Full JavaScript source. Define getScriptInfo() + main() to make it a menu tool (see get_script_api)." }
          },
          "required": ["name", "code"],
          "additionalProperties": false
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string name, code;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            name = doc.RootElement.GetString("name");
            code = doc.RootElement.GetString("code");
        }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        name = ScriptLibrary.SanitizeName((name ?? "").Trim());
        if (string.IsNullOrWhiteSpace(name)) return "Error: \"name\" is empty or has no valid characters.";
        if (string.IsNullOrWhiteSpace(code)) return "Error: \"code\" is empty.";

        // 预校验：若声明了 getScriptInfo，先确认它能 eval 出元数据，避免保存破损的工具脚本（且先于授权，不为坏脚本弹卡片）。
        var (info, error) = ScriptTools.InspectSource(name, code, project, currentPart, quantization, language);
        if (error != null)
            return "Error: getScriptInfo failed to evaluate — " + error + "\nFix the script and call save_script again. Nothing was saved.";

        bool existed = ScriptLibrary.Exists(name);
        string prefix = "";
        // 覆盖已存脚本 = 破坏用户外部文件 → 过授权闸门；新建是加性、不拦。
        if (existed)
        {
            var (proceed, message) = await ToolAuthorization.AuthorizeAsync(
                new AgentAuthorizationRequest(AgentWriteKind.ScriptOverwrite, 0, name), confirm, cancellationToken);
            if (!proceed)
                return message;
            prefix = message;
        }

        try { ScriptLibrary.Save(name, code); }
        catch (Exception ex) { return "Error: failed to save — " + ex.Message; }

        var sb = new StringBuilder(prefix);
        sb.Append(existed ? "Updated" : "Saved").Append(" script \"").Append(name).Append("\". ");
        if (info != null)
            sb.Append(string.Format("Registered as menu tool \"{0}\" in {1}.", info.DisplayName, ContextLabel(info.Context)));
        else
            sb.Append("It has no getScriptInfo(), so it is a plain run-once script (Script side panel only; not in menus).");
        return sb.ToString();
    }

    static string ContextLabel(ScriptToolContext c) => c switch
    {
        ScriptToolContext.Note => "the piano-roll note right-click menu",
        ScriptToolContext.Part => "the arrangement part right-click menu",
        ScriptToolContext.PartContent => "the piano-roll blank right-click menu",
        ScriptToolContext.Track => "the track-header right-click menu",
        ScriptToolContext.TrackContent => "the arrangement blank-lane right-click menu",
        _ => "the top Scripts menu",
    };
}

// 列出库内全部脚本，标出哪些是菜单工具（显示名 + 挂载 context）、哪些是普通脚本。
internal sealed class ListScriptsTool(IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language) : IAgentTool
{
    public string Name => "list_scripts";

    public string Description =>
        "List the user's saved scripts in the library, marking each as a menu tool (with its display name and which menu/context) or a plain script, " +
        "and flagging those that take inputs. Use before editing a script, running one with run_saved_script, or to avoid name clashes. " +
        "For a script marked (takes inputs), call get_script_inputs to see its parameters before run_saved_script.";

    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var names = ScriptLibrary.List();
        if (names.Count == 0) return Task.FromResult("The script library is empty.");

        var tools = ScriptTools.Discover(project, currentPart, quantization, language).ToDictionary(t => t.ScriptName);
        var sb = new StringBuilder();
        sb.Append(names.Count).Append(" script(s):");
        foreach (var n in names)
        {
            sb.Append("\n- ").Append(n);
            if (tools.TryGetValue(n, out var t))
            {
                sb.Append(string.Format("  [tool \"{0}\", context={1}]", t.DisplayName, t.Context.ToString().ToLowerInvariant()));
                if (t.HasInputs)
                    sb.Append(" (takes inputs)");
            }
            else
                sb.Append("  [plain]");
        }
        return Task.FromResult(sb.ToString());
    }
}

// 读出某脚本的完整源码（编辑前用）。
internal sealed class ReadScriptTool : IAgentTool
{
    public string Name => "read_script";

    public string Description => "Return the full source of a saved script by its library name. Use before editing an existing script.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": { "name": { "type": "string", "description": "Library name (without .js)." } },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string name;
        try { using var doc = JsonDocument.Parse(argumentsJson); name = doc.RootElement.GetString("name"); }
        catch (Exception ex) { return Task.FromResult("Error: invalid arguments — " + ex.Message); }

        name = ScriptLibrary.SanitizeName((name ?? "").Trim());
        if (string.IsNullOrWhiteSpace(name) || !ScriptLibrary.Exists(name))
            return Task.FromResult("Error: no script named \"" + name + "\". Call list_scripts to see available names.");
        try { return Task.FromResult(ScriptLibrary.Read(name)); }
        catch (Exception ex) { return Task.FromResult("Error: " + ex.Message); }
    }
}

// 删除库内脚本（同时从菜单移除）。删文件 = 破坏用户外部产物（历史管理器救不回）→ 恒过授权闸门。
internal sealed class DeleteScriptTool(Func<AgentAuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm = null) : IAgentTool
{
    public string Name => "delete_script";

    public string Description =>
        "Delete a saved script from the library by name (also removes it from the menus). " +
        "This deletes the user's file and CANNOT be undone, so it needs the user's authorization — the result tells you whether it was deleted.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": { "name": { "type": "string", "description": "Library name (without .js)." } },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string name;
        try { using var doc = JsonDocument.Parse(argumentsJson); name = doc.RootElement.GetString("name"); }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        name = ScriptLibrary.SanitizeName((name ?? "").Trim());
        if (string.IsNullOrWhiteSpace(name) || !ScriptLibrary.Exists(name))
            return "Error: no script named \"" + name + "\". Call list_scripts to see available names.";

        // 删除是破坏性外部文件操作 → 过授权闸门（Auto 直删 / Confirm 卡片裁决 / ReadOnly 不删+建议）。
        var (proceed, message) = await ToolAuthorization.AuthorizeAsync(
            new AgentAuthorizationRequest(AgentWriteKind.ScriptDelete, 0, name), confirm, cancellationToken);
        if (!proceed)
            return message;

        try { ScriptLibrary.Delete(name); return message + "Deleted script \"" + name + "\"."; }
        catch (Exception ex) { return "Error: " + ex.Message; }
    }
}
