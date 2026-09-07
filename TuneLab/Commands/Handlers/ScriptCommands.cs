using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.Scripting;
using TuneLab.Utils;

namespace TuneLab.Commands.Handlers;

// 脚本库的三条读命令（列表 / 读源码 / 读入参 schema）。写那半边（run / run-saved / save / delete）
// 要授权闸门，留待 edit 批次。
//
// 【为什么要编辑器态】列表与入参都要 eval 脚本自己的声明函数（getScriptInfo / getInputConfig），
// 而那些函数会读"用户此刻在看什么"——按当前 part 或选中范围给默认值是脚本作者的常规写法。
// 故这两条经 ctx.EditorState 取（见 IEditorStateAccess）；headless 下它为 null，脚本读到的就是"没有"，
// 与用户没选任何东西时一致——不猜一个 part 出来。
// 列出库内全部脚本，标出哪些是菜单工具（显示名 + 挂载 context）、哪些是普通脚本。
internal sealed class ScriptListCommand : ICommand
{
    public string Path => "script list";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "list_scripts";

    public string Brief => "List saved scripts, marking menu tools and their inputs";

    public string Documentation =>
        "List the user's saved scripts in the library, marking each as a menu tool (with its display name and which menu/context) or a plain script, " +
        "and flagging those that take inputs. Use before editing a script, running one with run_saved_script, or to avoid name clashes. " +
        "For a script marked (takes inputs), call get_script_inputs to see its parameters before run_saved_script.";

    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var names = ScriptLibrary.List();
        if (names.Count == 0)
            return CommandResult.Ok(new JsonObject { ["scripts"] = new JsonArray() });

        // 判定"是不是菜单工具"要 eval 每个脚本的 getScriptInfo，那需要工程上下文。没开工程时如实说，
        // 不退化成"只报文件名"——那份清单看着像全的，实则每一条的工具标注都缺，比报错更误导。
        if (ctx.Project is not { } project)
            return CommandResult.Fail("no_project",
                "no project is open, so the scripts cannot be inspected (deciding whether a script is a menu tool means evaluating its getScriptInfo, which needs a project). Open a project and call again.");

        // Discover 会 eval 脚本、读工程，且与宿主菜单共用一份静态缓存 → 与菜单同在主线程上跑。
        var tools = await ctx.OnMainThread(() => ScriptTools
            .Discover(project, ctx.CurrentPart(), ctx.Quantization(), ctx.Language)
            .ToDictionary(t => t.ScriptName));

        var scripts = new JsonArray();
        foreach (var name in names)
        {
            var script = new JsonObject { ["name"] = name };
            script["tool"] = tools.TryGetValue(name, out var tool) ? new JsonObject
            {
                ["displayName"] = tool.DisplayName,
                ["context"] = tool.Context.ToString().ToLowerInvariant(),
                ["hasInputs"] = tool.HasInputs,
            } : null;
            scripts.Add(script);
        }
        return CommandResult.Ok(new JsonObject { ["scripts"] = scripts });
    }

    // 措辞与搬家前逐字一致。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var scripts = obj["scripts"]!.AsArray();
        if (scripts.Count == 0)
            return "The script library is empty.";

        var sb = new StringBuilder();
        sb.Append(scripts.Count).Append(" script(s):");
        foreach (var node in scripts)
        {
            var script = node!.AsObject();
            sb.Append("\n- ").Append(script["name"]!.GetValue<string>());
            if (script["tool"] is JsonObject tool)
            {
                sb.Append(string.Format("  [tool \"{0}\", context={1}]",
                    tool["displayName"]!.GetValue<string>(), tool["context"]!.GetValue<string>()));
                if (tool["hasInputs"]!.GetValue<bool>())
                    sb.Append(" (takes inputs)");
            }
            else
                sb.Append("  [plain]");
        }
        return sb.ToString();
    }
}

// 读出某脚本的完整源码（编辑前用）。零依赖：不碰工程、不碰编辑器态、不 eval。
internal sealed class ScriptReadCommand : ICommand
{
    public string Path => "script read";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "read_script";

    public string Brief => "Return the full source of one saved script";

    public string Documentation => "Return the full source of a saved script by its library name. Use before editing an existing script.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": { "name": { "type": "string", "description": "Library name (without .js)." } },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    public Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var name = ScriptLibrary.SanitizeName((args.Json.GetString("name") ?? "").Trim());
        if (string.IsNullOrWhiteSpace(name) || !ScriptLibrary.Exists(name))
            return Task.FromResult(CommandResult.Fail("not_found", SavedScriptSupport.NotFound(name)));

        try
        {
            // 产物就是源码本身，故 Data 只是 { name, code } 而非拆过的什么东西（判据同 docs *：
            // 产物的事实形态是文本）。
            return Task.FromResult(CommandResult.Ok(new JsonObject { ["name"] = name, ["code"] = ScriptLibrary.Read(name) }));
        }
        catch (Exception ex) { return Task.FromResult(CommandResult.Fail("read_failed", ex.Message)); }
    }

    // 搬家前直接把源码原样回灌，不加任何抬头——保持一致（多一行抬头就会混进模型改写后的代码里）。
    public string Render(JsonNode? data, CommandArgs args)
        => data is JsonObject obj ? obj["code"]!.GetValue<string>() : string.Empty;
}

// 某脚本的入参 schema（名/类型/默认/范围·选项）+ 用户上次输入值。只读，不跑脚本动作
// （只 eval 顶层调 getInputConfig，约定无副作用、误改原子回退）。
internal sealed class ScriptInputsCommand : ICommand
{
    public string Path => "script inputs";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "get_script_inputs";

    public string Brief => "Show one script's input fields and the user's last values";

    public string Documentation =>
        "Return the input schema of a saved script (each field's name, type, default, range/options) plus the user's LAST entered values. " +
        "Call this before run_saved_script when list_scripts marks a script as taking inputs, so you know what to pass. " +
        "A script that takes no inputs reports so — just run it with run_saved_script and no inputs.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": { "name": { "type": "string", "description": "Library name of the script (without .js)." } },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var name = ScriptLibrary.SanitizeName((args.Json.GetString("name") ?? "").Trim());
        if (string.IsNullOrWhiteSpace(name) || !ScriptLibrary.Exists(name))
            return CommandResult.Fail("not_found", SavedScriptSupport.NotFound(name));

        if (ctx.Project is not { } project)
            return CommandResult.Fail("no_project",
                "no project is open, so the script's inputs cannot be evaluated (getInputConfig runs against the project). Open a project and call again.");

        string code;
        try { code = ScriptLibrary.Read(name); }
        catch (Exception ex) { return CommandResult.Fail("read_failed", ex.Message); }

        var currentPart = ctx.CurrentPart();
        var quantization = ctx.Quantization();
        var (scriptId, hasInputs) = SavedScriptSupport.Inspect(name, code, project, currentPart, quantization, ctx.Language);
        if (!hasInputs)
            return CommandResult.Ok(NoInputs(name));

        var lastValues = ScriptInputMemory.Load(scriptId);

        // getInputConfig 读工程上下文（选中音符等），在主线程求值；误改在 GetInputConfig 内原子回退。
        // DescribeSchema 也在主线程内完成——自定义 scale/format 的 config 会回调 Jint 引擎（.Scale.ToValue / .Format），
        // 而 Jint 引擎非线程安全、须在其创建线程调用；built-in config 纯 C# 无此约束，一并放里无碍。
        return await ctx.OnMainThread(() =>
        {
            var (schema, error) = ScriptRunner.GetInputConfig(project, currentPart, quantization, ctx.Language,
                ctx.Selection(), ctx.PianoSelection(), code, lastValues, cancellationToken);
            if (error != null)
                return CommandResult.Fail("eval_failed", string.Format("getInputConfig failed to evaluate for \"{0}\" — {1}", name, error));
            if (schema == null)
                return CommandResult.Ok(NoInputs(name));
            return CommandResult.Ok(new JsonObject
            {
                ["name"] = name,
                ["hasInputs"] = true,
                // schema 留成一段文本（判据见 SavedScriptSupport.DescribeSchema）。
                ["schemaText"] = SavedScriptSupport.DescribeSchema(name, schema, lastValues),
            });
        });
    }

    static JsonObject NoInputs(string name) => new() { ["name"] = name, ["hasInputs"] = false };

    // 措辞与搬家前逐字一致。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;
        return obj["hasInputs"]!.GetValue<bool>()
            ? obj["schemaText"]!.GetValue<string>()
            : string.Format("Script \"{0}\" takes no inputs. Run it with run_saved_script and no `inputs`.", obj["name"]!.GetValue<string>());
    }
}

// 保存（新建或覆盖）一个脚本到库：让调用方把用户描述的功能写成一个【工具脚本】（定义 getScriptInfo + main）
// 存进脚本库，即自动注册进对应菜单（global / note / part / partContent…），用户日后直接点菜单复用。
// 保存只持久化源码、不执行（安全）；保存前先预校验 getScriptInfo 可解析，并回报注册到了哪个菜单。
//
// 覆盖已存脚本 = 破坏用户外部文件（历史管理器救不回）→ 过闸门；新建是加性、不拦。
internal sealed class ScriptSaveCommand : ICommand
{
    public string Path => "script save";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "save_script";

    public string Brief => "Save a reusable script into the library (and its menu)";

    public string Documentation =>
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

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var name = ScriptLibrary.SanitizeName((args.Json.GetString("name") ?? "").Trim());
        var code = args.Json.GetString("code") ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return CommandResult.Fail("empty_name", "\"name\" is empty or has no valid characters.");
        if (string.IsNullOrWhiteSpace(code))
            return CommandResult.Fail("empty_code", "\"code\" is empty.");
        if (ctx.Project is not { } project)
            return CommandResult.Fail("no_project",
                "no project is open, so the script cannot be checked before saving (getScriptInfo is evaluated against a project). Open a project and call again.");

        // 预校验：若声明了 getScriptInfo，先确认它能 eval 出元数据，避免保存破损的工具脚本
        // （且先于授权，不为坏脚本打扰用户）。eval 脚本 → 与 `script list` 同在主线程。
        var (info, error) = await ctx.OnMainThread(() => ScriptTools.InspectSource(name, code, project,
            ctx.CurrentPart(), ctx.Quantization(), ctx.Language));
        if (error != null)
            return CommandResult.Fail("bad_script_info", "getScriptInfo failed to evaluate — " + error + "\nFix the script and call save_script again. Nothing was saved.");

        bool existed = ScriptLibrary.Exists(name);
        string note = "";
        if (existed)
        {
            var (proceed, message) = await ctx.Authorize(new AuthorizationRequest(WriteKind.ScriptOverwrite, 0, name), cancellationToken);
            if (!proceed)
                return CommandResult.Ok(new JsonObject { ["name"] = name, ["outcome"] = "refused", ["existed"] = true, ["note"] = message });
            note = message;
        }

        try { ScriptLibrary.Save(name, code); }
        catch (Exception ex) { return CommandResult.Fail("save_failed", "failed to save — " + ex.Message); }

        return CommandResult.Ok(new JsonObject
        {
            ["name"] = name,
            ["outcome"] = "applied",
            ["existed"] = existed,
            ["tool"] = info == null ? null : new JsonObject
            {
                ["displayName"] = info.DisplayName,
                ["context"] = info.Context.ToString(),
            },
            ["note"] = string.IsNullOrEmpty(note) ? null : note,
        });
    }

    // 措辞与搬家前逐字一致。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;
        if (obj["outcome"]!.GetValue<string>() == "refused")
            return obj["note"]!.GetValue<string>();

        var sb = new StringBuilder(obj["note"]?.GetValue<string>() ?? string.Empty);
        sb.Append(obj["existed"]!.GetValue<bool>() ? "Updated" : "Saved")
          .Append(" script \"").Append(obj["name"]!.GetValue<string>()).Append("\". ");
        if (obj["tool"] is JsonObject tool)
            sb.Append(string.Format("Registered as menu tool \"{0}\" in {1}.",
                tool["displayName"]!.GetValue<string>(), ContextLabel(tool["context"]!.GetValue<string>())));
        else
            sb.Append("It has no getScriptInfo(), so it is a plain run-once script (Script side panel only; not in menus).");
        return sb.ToString();
    }

    static string ContextLabel(string context) => context switch
    {
        nameof(ScriptToolContext.Note) => "the piano-roll note right-click menu",
        nameof(ScriptToolContext.Part) => "the arrangement part right-click menu",
        nameof(ScriptToolContext.PartContent) => "the piano-roll blank right-click menu",
        nameof(ScriptToolContext.Track) => "the track-header right-click menu",
        nameof(ScriptToolContext.TrackContent) => "the arrangement blank-lane right-click menu",
        _ => "the top Scripts menu",
    };
}

// 删除库内脚本（同时从菜单移除）。删文件 = 破坏用户外部产物（历史管理器救不回）→ 恒过闸门。
internal sealed class ScriptDeleteCommand : ICommand
{
    public string Path => "script delete";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "delete_script";

    public string Brief => "Delete a saved script from the library";

    public string Documentation =>
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

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var name = ScriptLibrary.SanitizeName((args.Json.GetString("name") ?? "").Trim());
        if (string.IsNullOrWhiteSpace(name) || !ScriptLibrary.Exists(name))
            return CommandResult.Fail("not_found", SavedScriptSupport.NotFound(name));

        // 删除是破坏性外部文件操作 → 过闸门（放开就直删 / 要确认就问 / 只读档不删+建议）。
        var (proceed, message) = await ctx.Authorize(new AuthorizationRequest(WriteKind.ScriptDelete, 0, name), cancellationToken);
        if (!proceed)
            return CommandResult.Ok(new JsonObject { ["name"] = name, ["outcome"] = "refused", ["note"] = message });

        try { ScriptLibrary.Delete(name); }
        catch (Exception ex) { return CommandResult.Fail("delete_failed", ex.Message); }

        return CommandResult.Ok(new JsonObject
        {
            ["name"] = name,
            ["outcome"] = "applied",
            ["note"] = string.IsNullOrEmpty(message) ? null : message,
        });
    }

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;
        var note = obj["note"]?.GetValue<string>() ?? string.Empty;
        return obj["outcome"]!.GetValue<string>() == "refused"
            ? note
            : note + "Deleted script \"" + obj["name"]!.GetValue<string>() + "\".";
    }
}

// 逃生口命令：让调用方写一段 JavaScript 表达复杂/批量/带循环条件的工程编辑（音乐编辑高度契合，
// 如"5-8 小节每音符升八度再加三度和声"=一个循环，一轮搞定、省下几十次往返）。
//
// 命令本身很薄：取 code，交给共享的写执行器（ScriptWriteExecutor）过闸门后运行。
// 脚本引擎、动作面 API、沙箱、整段=一次 Commit 的收口都在脚本模块里（TuneLab.Scripting）；
// 分级授权 + 预览 + 写守卫 wait-retry 在执行器里——与 `script run-saved` 共用同一写路径（单一动作面 SSOT）。
internal sealed class ScriptRunCommand : ICommand
{
    public string Path => "script run";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "run_script";

    public string Brief => "Run a JavaScript program that edits the project";

    public string Documentation =>
        "Run a short JavaScript program to edit the project via the global `tl` object. Use this for complex, bulk, computed, or conditional edits " +
        "that would otherwise take many tool calls — e.g. \"for every note in bars 5-8, raise it an octave and add a harmony a third above\" is one loop. " +
        "The whole script runs as ONE undoable change. " +
        "BEFORE writing your first script in a conversation, call get_script_api once to load the full API, the handle/tick rules, and examples — do not guess method names. " +
        "Key rules: object-style — `tl` is the EDITOR and the project hangs off tl.currentProject(), while tracks/parts/notes are handles with read/write fields (n.pitch += 1) " +
        "and methods (part.notes(), part.removeNote(n)) — create and delete ALWAYS hang off the parent, no handle has an x.remove(); " +
        "collection methods return plain arrays (for-of/index, not a linked list); positions are absolute ticks; pitch is MIDI; print(x) emits debug output. " +
        "A script that only prints and changes nothing is also how you INSPECT the project below the summary level — it reports that it produced no changes, and you get your print output back. " +
        "NOTE: depending on the user's authorization setting your edits may be applied only after the user confirms, or not applied at all (read-only) — the result message tells you what happened; relay it, don't assume the edit landed.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "code": { "type": "string", "description": "JavaScript source to run. Use the `tl` global to read/edit the project and print(...) for debugging output." }
          },
          "required": ["code"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var code = args.Json.GetString("code") ?? "";
        if (string.IsNullOrWhiteSpace(code))
            return CommandResult.Fail("empty_code", "\"code\" is empty.");
        if (ctx.Project is not { } project)
            return CommandResult.Fail("no_project", "no project is open, so there is nothing to edit.");

        // 内联脚本无入参（inputs=null）；命名脚本的入参路径在 `script run-saved`。共用同一授权闸门与收口。
        return await ScriptWriteExecutor.RunAsync(ctx, project, code, inputs: null, cancellationToken);
    }

    public string Render(JsonNode? data, CommandArgs args) => ScriptWriteExecutor.Render(data);
}

// 按库名读源码运行；inputs 可省——给了就覆盖在用户上次值之上再补默认，没给则用上次/默认。
// 走与 `script run` 相同的授权闸门。政策：代跑【不回写】用户的 ScriptInputMemory 上次值
// （用户上次值是用户的意图，调用方的选择留在它自己的历史里，不污染用户手动运行的记忆）。
internal sealed class ScriptRunSavedCommand : ICommand
{
    public string Path => "script run-saved";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "run_saved_script";

    public string Brief => "Run a saved script from the library by name";

    public string Documentation =>
        "Run a script already saved in the user's library, by name — like pressing its menu item for the user. " +
        "Use this to reuse a tool the user (or you) saved earlier instead of rewriting it with run_script. " +
        "`inputs` is optional: pass a map of input-name -> value for the fields you want to set (call get_script_inputs first to see them); " +
        "any field you omit falls back to the user's last value, else the config default. Omit `inputs` entirely to run with last/default values. " +
        "Runs as ONE undoable change through the SAME authorization gate as run_script (may be applied only after the user confirms, or not at all in read-only) — relay the result, don't assume it landed. " +
        "Your inputs are NOT saved as the user's last values.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Library name of the script to run (without .js)." },
            "inputs": { "type": "object", "description": "Optional map of input-name -> value overriding the user's last values. Omit to use last/default values." }
          },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        // 【参数形状先校验】排在"脚本存不存在"之前：形状错是调用方更根本的错，不该被存在性检查
        // 掩盖（同 `setting set` 把路径合法性放在授权之前）。
        PropertyObject? givenInputs = null;
        if (args.Json.TryGetProperty("inputs", out var inp) && inp.ValueKind != JsonValueKind.Null)
        {
            // 【给了但不是对象就报错】以前这里是"不是对象就当没给"，于是一个写坏的 inputs 会让脚本拿
            // 默认值照跑、回报仍是"跑成功了"——调用方（尤其是无人值守的跑批）无从知道自己传的值一个
            // 都没生效。null 仍当"没给"：模型常拿 null 表示"不传"。
            if (inp.ValueKind != JsonValueKind.Object)
                return CommandResult.Fail("bad_inputs", string.Format(
                    "\"inputs\" must be an object mapping input names to values (got {0}), so NOTHING was run. "
                    + "Call get_script_inputs to see the fields.", inp.ValueKind.ToString().ToLowerInvariant()));
            givenInputs = PropertyJsonUtils.ToPropertyObject(JObject.Parse(inp.GetRawText()));
        }

        var name = ScriptLibrary.SanitizeName((args.Json.GetString("name") ?? "").Trim());
        if (string.IsNullOrWhiteSpace(name) || !ScriptLibrary.Exists(name))
            return CommandResult.Fail("not_found", SavedScriptSupport.NotFound(name));
        if (ctx.Project is not { } project)
            return CommandResult.Fail("no_project", "no project is open, so there is nothing to edit.");

        string code;
        try { code = ScriptLibrary.Read(name); }
        catch (Exception ex) { return CommandResult.Fail("read_failed", ex.Message); }

        var currentPart = ctx.CurrentPart();
        var quantization = ctx.Quantization();
        var (scriptId, hasInputs) = SavedScriptSupport.Inspect(name, code, project, currentPart, quantization, ctx.Language);

        PropertyObject? inputs = null;
        // 非入参脚本（无 getInputConfig）：脚本忽略入参，直接跑，不去 eval getInputConfig（普通脚本那样做会跑其脚本体）。
        // 此时给了 inputs 也无处可用（脚本没声明任何字段），照跑。
        if (hasInputs)
        {
            var lastValues = ScriptInputMemory.Load(scriptId);

            // 入参 = 用户上次值 ← 调用方给的覆盖（稀疏叠加）。schema 依合并后的值现算（条件字段随之定），
            // 再补默认成全量喂 main。
            var merged = new Map<string, PropertyValue>();
            foreach (var kv in lastValues.Map)
                merged[kv.Key] = kv.Value;
            if (givenInputs != null)
                foreach (var kv in givenInputs.Map)
                    merged[kv.Key] = kv.Value;
            var mergedValues = new PropertyObject(merged);

            var (schema, error) = await ctx.OnMainThread(() =>
                ScriptRunner.GetInputConfig(project, currentPart, quantization, ctx.Language, ctx.Selection(), ctx.PianoSelection(), code, mergedValues, cancellationToken));
            if (error != null)
                return CommandResult.Fail("eval_failed", string.Format("getInputConfig failed to evaluate for \"{0}\" — {1}", name, error));
            if (schema != null)
                inputs = ScriptConfigs.FillDefaults(schema, mergedValues);
        }

        return await ScriptWriteExecutor.RunAsync(ctx, project, code, inputs, cancellationToken);
    }

    public string Render(JsonNode? data, CommandArgs args) => ScriptWriteExecutor.Render(data);
}
