using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Data;
using TuneLab.Data.Synthesis;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Commands.Handlers;

// 唯一的只读「定向」命令：返回工程结构摘要（PPQ、tempo、拍号、各轨 1-based 编号/名/状态/part 数/音符数），
// 让调用方在动手前一眼看清轨/part 号与 PPQ。其余读取（音符明细、参数曲线、当前 part/播放线等）一律走
// `script run`（tl.currentPart()/notes()/samplePitch()/playhead()…）——本命令只兜住"动手前先看一眼"
// 与"用户随口问事实"两个场景。直接读 IProject，不经任何 facade。
internal sealed class ProjectStatusCommand : ICommand
{
    public string Path => "project status";
    public CommandKind Kind => CommandKind.Read;

    // 沿用搬家前的工具名，模型侧零感知（系统提示与其它工具的描述文本都在引用它）。
    public string AgentToolName => "get_project_overview";

    public string Brief => "PPQ, tempo, time signature and a per-track summary";

    public string Documentation =>
        "Get an overview of the current project: PPQ (ticks per quarter note), tempo, time signature, and every track with its 1-based number, " +
        "name, mute/solo, gain/pan, part count and note count. Call this first to orient before editing. " +
        "For note-level detail or any edit, write a script with run_script (call get_script_api once for the `tl` API). Track/part/note numbers are 1-based.";

    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    public Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var project = ctx.Project;
        if (project == null)
            return Task.FromResult(CommandResult.Fail("no_project", "No project is open, so there is nothing to report."));

        var tempos = new JsonArray();
        foreach (var tempo in project.TempoManager.Tempos)
            tempos.Add(new JsonObject { ["bpm"] = tempo.Bpm, ["pos"] = tempo.Pos });

        var timeSignatures = new JsonArray();
        foreach (var sig in project.TimeSignatureManager.TimeSignatures)
            timeSignatures.Add(new JsonObject
            {
                ["numerator"] = sig.Numerator,
                ["denominator"] = sig.Denominator,
                ["bar"] = sig.BarIndex + 1,   // 1-based，与呈现一致
            });

        var tracks = new JsonArray();
        var trackList = project.Tracks;
        for (int i = 0; i < trackList.Count; i++)
        {
            var track = trackList[i];
            var parts = track.Parts.ToList();
            tracks.Add(new JsonObject
            {
                ["number"] = i + 1,          // 1-based
                ["name"] = track.Name.Value,
                ["mute"] = track.IsMute.Value,
                ["solo"] = track.IsSolo.Value,
                ["gain"] = track.Gain.Value,
                ["pan"] = track.Pan.Value,
                ["parts"] = parts.Count,
                ["notes"] = parts.OfType<IMidiPart>().Sum(p => p.Notes.Count()),
            });
        }

        return Task.FromResult(CommandResult.Ok(new JsonObject
        {
            ["ppq"] = MusicTheory.RESOLUTION,
            ["tempos"] = tempos,
            ["timeSignatures"] = timeSignatures,
            ["tracks"] = tracks,
        }));
    }

    // 措辞与搬家前逐字一致——这是"内置 agent 行为不变"这个验收标准的一部分。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Project: PPQ={0} (ticks per quarter note). Positions/durations are in ticks.", obj["ppq"]!.GetValue<int>()));

        var tempos = obj["tempos"]!.AsArray();
        if (tempos.Count > 0)
        {
            sb.Append("Tempo: ");
            sb.Append(string.Join(", ", tempos.Select(t => string.Format(CultureInfo.InvariantCulture,
                "{0:0.##}bpm@tick{1:0}", t!["bpm"]!.GetValue<double>(), t["pos"]!.GetValue<double>()))));
            sb.AppendLine();
        }

        var timeSignatures = obj["timeSignatures"]!.AsArray();
        if (timeSignatures.Count > 0)
        {
            sb.Append("Time signature: ");
            sb.Append(string.Join(", ", timeSignatures.Select(s => string.Format(CultureInfo.InvariantCulture,
                "{0}/{1}@bar{2}", s!["numerator"]!.GetValue<int>(), s["denominator"]!.GetValue<int>(), s["bar"]!.GetValue<int>()))));
            sb.AppendLine();
        }

        var tracks = obj["tracks"]!.AsArray();
        sb.AppendLine(string.Format("Tracks ({0}):", tracks.Count));
        foreach (var track in tracks)
        {
            var flags = new List<string>();
            if (track!["mute"]!.GetValue<bool>()) flags.Add("mute");
            if (track["solo"]!.GetValue<bool>()) flags.Add("solo");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  Track {0}: \"{1}\"{2}, gain={3:0.#}dB, pan={4:0.##}, parts={5}, notes={6}",
                track["number"]!.GetValue<int>(), track["name"]!.GetValue<string>(),
                flags.Count > 0 ? " [" + string.Join(",", flags) + "]" : "",
                track["gain"]!.GetValue<double>(), track["pan"]!.GetValue<double>(),
                track["parts"]!.GetValue<int>(), track["notes"]!.GetValue<int>()));
        }
        return sb.ToString();
    }
}

// 把当前工程【导出成一个文件】——`project.importTracks(path)` 的对偶（那个读入文件，这个写出文件）。
//
// 为什么是命令而不是 tl 脚本原语（与"工程编辑恒走 script run"的护栏不冲突）：
//  · 护栏约束的是【工程状态的修改】，而导出不改工程状态一分一毫，与 `script save` / `script delete`
//    （同样写外部文件、同样不碰工程数据）同类——那两条本来就是命令，故这是循例而非例外。
//  · 授权闸门是 async（要等用户裁决），而脚本经 Jint 同步跑在主线程，中途阻塞等裁决会自死锁。命令天然容得下。
//  将来若用户真需要在脚本里导出（如"每轨各存一个 midi"），再加 tl 原语并配"脚本内登记意图 → 脚本成功结束后统一
//  过闸门执行、脚本出错则一并丢弃（文件从未写）"的延迟写机制，与本命令加性并存。
//
// 【只做工程/MIDI 等格式文件，不做音频导出】：音频导出要跑完整合成+混音+编码，期间界面必须锁住（根因是渲染要求
// 数据全程不变，不是 UI 偷懒）——那与"调用方边导出边继续干活"根本矛盾，且"要不要现在把机器占住几分钟"是用户的
// 人在环决定，同播放/试听的裁定。故音频导出的正解是备好参数、最后一下由用户按，不在本命令里。
// 打开一个工程文件，换掉用户此刻开着的那一份。覆盖率清单里 `pending:project-open` 那一条：
// 「打开」与「最近文件」要的是**一个路径参数**，那是命令的形状——动作面只能弹个选择器让人自己挑
// （`file.open`），而带路径的这件事必须是命令。
//
// 三道闸，顺序有意：
//  ① 没有编辑器 → 这里做不到（headless 的工程在启动时就定了，见 IProjectFileAccess）；
//  ② **有未保存的改动 → 直接拒绝**，在问用户之前。把没保存的活儿换掉是撤销栈救不回的事，不该只靠
//     一次「要不要打开」的确认糊过去——用户在那张卡片上看到的是"打开哪个文件"，不是"丢掉你半小时的活儿"；
//  ③ 路径不存在 / 格式不支持 → 报错，同样不打扰用户。
// 过闸后恒过授权（ProjectOpen 档，措辞点明"关掉现在这份、撤销历史一并没了"）。
internal sealed class ProjectOpenCommand : ICommand
{
    public string Path => "project open";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "open_project";

    public string Brief => "Open a project file, replacing the one currently open";

    public string Documentation =>
        "Open a project FILE from disk into the running TuneLab, replacing the project the user has open right now (that project's undo history goes with it). "
        + "The path is a local file path; the format comes from the extension — tlp/tlpx plus whatever the installed format plugins import (mid/midi and others). "
        + "\nIt REFUSES, without bothering the user, when: there are unsaved changes in the current project (save first — run_action with \"file.save\", or ask the user to), the file does not exist, or nothing installed can read that extension. "
        + "Otherwise it always asks for the user's authorization: this closes what they are working on. "
        + "\nThere is no editor in a headless process, so this cannot work there — a headless run takes its project from --project at startup. "
        + "To read the project that is open, use get_project_status; to write a copy to disk, export_project (that one does NOT change which file the user's project is saved to, this one does).";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Absolute local path of the project file to open, e.g. C:\\Users\\me\\song.tlpx." }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var given = (args.Json.GetString("path") ?? "").Trim().Trim('"');
        if (given.Length == 0)
            return CommandResult.Fail("empty_path", "\"path\" is required.");

        if (ctx.ProjectFile is not { } file)
            return CommandResult.Fail("no_editor",
                "There is no editor in this process, so there is no document to swap. Opening a project replaces what the user is looking at — "
                + "that only exists in a running TuneLab window (attach to one), and a headless run takes its project from --project at startup.");

        string fullPath;
        try { fullPath = System.IO.Path.GetFullPath(given); }
        catch (Exception ex) { return CommandResult.Fail("bad_path", string.Format("\"{0}\" is not a usable file path — {1}", given, ex.Message)); }
        if (!File.Exists(fullPath))
            return CommandResult.Fail("missing_file", string.Format("there is no file at \"{0}\".", fullPath));

        var format = System.IO.Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();
        var importable = FormatsManager.GetAllImportFormats();
        if (!importable.Contains(format))
            return CommandResult.Fail("unsupported_format", string.Format(
                "nothing installed can read \".{0}\". Readable: {1}.", format,
                importable.Count == 0 ? "(none)" : string.Join(", ", importable.Select(f => "." + f))));

        // 未保存就到此为止（早于授权）：见类头 ②。
        if (!file.IsSaved)
            return CommandResult.Fail("unsaved_changes",
                "the project currently open has unsaved changes, and opening another one would throw them away — the undo history cannot bring that back. "
                + "Save it first (run_action with \"file.save\"), or ask the user to deal with it, then open again.");

        var (proceed, message) = await ctx.Authorize(new AuthorizationRequest(WriteKind.ProjectOpen, 0, fullPath), cancellationToken);
        if (!proceed)
            return CommandResult.Ok(new JsonObject { ["path"] = fullPath, ["outcome"] = "refused", ["note"] = message });

        // 落地那刻重查未保存：闸门等待期用户可能又动了工程（同 `keybinding set` 的"冲突在落地那刻重查"）。
        return CommandResult.Ok(await ctx.OnMainThread(() =>
        {
            if (!file.IsSaved)
                return new JsonObject { ["path"] = fullPath, ["outcome"] = "changed_meanwhile" };

            if (file.Open(fullPath) is { } error)
                return new JsonObject { ["path"] = fullPath, ["outcome"] = "failed", ["note"] = error };

            var project = ctx.Project;
            return new JsonObject
            {
                ["path"] = file.Path ?? fullPath,
                ["outcome"] = "applied",
                ["tracks"] = project?.Tracks.Count ?? 0,
                ["note"] = string.IsNullOrEmpty(message) ? null : message,
            };
        }));
    }

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var path = obj["path"]!.GetValue<string>();
        switch (obj["outcome"]!.GetValue<string>())
        {
            case "refused":
                return obj["note"]!.GetValue<string>();
            case "changed_meanwhile":
                return string.Format(
                    "Nothing was opened: the user edited their project while the request was waiting for authorization, so it now has unsaved changes. "
                    + "Save those first, then open \"{0}\" again.", path);
            case "failed":
                return string.Format("Could not open \"{0}\" — {1}. The project that was open is untouched.", path, obj["note"]?.GetValue<string>() ?? "unknown error");
            default:
                var sb = new StringBuilder();
                if (obj["note"]?.GetValue<string>() is { Length: > 0 } note)
                    sb.Append(note);
                sb.Append(string.Format("Opened \"{0}\" ({1} track(s)). It is now the project every command and script sees.",
                    path, obj["tracks"]?.GetValue<int>() ?? 0));
                return sb.ToString();
        }
    }
}

internal sealed class ProjectExportCommand : ICommand
{
    public string Path => "project export";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "export_project";

    public string Brief => "Export the current project to a file";

    public string Documentation =>
        "Export the CURRENT project to a file (the counterpart of importTracks, which reads one in). " +
        "The format comes from the file extension: tlp/tlpx = TuneLab project (full fidelity — sound sources, effects, automation, phonemes), " +
        "plus whatever installed format plugins provide. The error message lists the supported extensions if you get it wrong. " +
        "This writes a file anywhere on the user's disk, so it ALWAYS needs the user's authorization; if a file is already at that path it gets replaced. " +
        "IMPORTANT: this is 'export a copy', NOT 'save' — it does not change which file the user's project is saved to and does not clear their unsaved changes, " +
        "so never tell the user you saved their project. This writes the PROJECT, not audio: to render wav/mp3/flac/ogg use export_audio, " +
        "which waits for synthesis to finish first.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Absolute local file path to write, including the extension (which picks the format), e.g. C:\\Users\\me\\song.tlpx. The parent folder must already exist." }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var given = (args.Json.GetString("path") ?? "").Trim().Trim('"');
        if (given.Length == 0)
            return CommandResult.Fail("empty_path", "\"path\" is required.");
        if (ctx.Project is not { } project)
            return CommandResult.Fail("no_project", "no project is open, so there is nothing to export.");

        // 路径合法性先行（不为坏请求打扰用户 —— 同 `script save` 把预校验放在授权之前）。
        string fullPath;
        try { fullPath = System.IO.Path.GetFullPath(given); }
        catch (Exception ex) { return CommandResult.Fail("bad_path", string.Format("\"{0}\" is not a usable file path — {1}", given, ex.Message)); }
        if (Directory.Exists(fullPath))
            return CommandResult.Fail("path_is_folder", string.Format("\"{0}\" is a folder, not a file path. Give the full path including the file name and extension.", fullPath));

        var format = System.IO.Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(format))
            return CommandResult.Fail("no_extension", string.Format("\"{0}\" has no file extension, so there's no format to export as. Supported: {1}.", fullPath, SupportedList()));
        if (!FormatsManager.GetAllExportFormats().Contains(format))
            return CommandResult.Fail("unsupported_format", string.Format("cannot export to \".{0}\" — no installed format provides it. Supported: {1}.", format, SupportedList()));

        var folder = System.IO.Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return CommandResult.Fail("missing_folder", string.Format("the folder \"{0}\" does not exist. Create it first or pick an existing folder (this command won't create folders).", folder));

        // 序列化【先于】授权：FormatsManager 缓冲进 MemoryStream（原子写语义——失败时目标文件尚未开写），
        // 故序列化失败可以直接报错、不必先问一次再让用户白确认一场。
        var file = new NativeProjectFile
        {
            Project = project.GetInfo(),
            Editor = new EditorInfo { PlayheadPos = PlayheadTick(project) },
            Export = project.GetExportConfig(),
        };
        // native(.tlp/.tlpx) 走 SerializeNative 带上 editor/export 元数据保真；foreign(.mid 等) 由它内部
        // 自动降级到纯 musical Serialize——故两类统一走这一条，与「另存为」同一路径。
        if (!FormatsManager.SerializeNative(file, format, out var stream, out var error))
            return CommandResult.Fail("serialize_failed", string.Format("failed to serialize the project as \".{0}\" — {1}. Nothing was written.", format, error));

        bool overwrite = File.Exists(fullPath);
        var displayName = FormatsManager.GetDisplayName(format);
        // 恒过闸门：导出路径是任意的（能写到用户磁盘任何地方），且历史记录管理器只保工程数据、救不回外部文件。
        var (proceed, message) = await ctx.Authorize(
            new AuthorizationRequest(overwrite ? WriteKind.ProjectExportOverwrite : WriteKind.ProjectExport, 0, fullPath, displayName),
            cancellationToken);
        if (!proceed)
        {
            stream.Dispose();
            return CommandResult.Ok(new JsonObject
            {
                ["path"] = fullPath,
                ["format"] = format,
                ["formatName"] = displayName,
                ["outcome"] = "refused",
                ["overwrite"] = overwrite,
                ["note"] = message,
            });
        }

        long bytes;
        try
        {
            using (stream)
            // 用户只同意了卡片上说的那件事：说"新建"就【只能】新建。闸门等待期用户可能自己在该路径放了文件，
            // 那时 Create 会静默替换一个我们从未取得替换许可的文件 → 故非覆盖档用 CreateNew，让它抛错而不是越权
            // （同 `keybinding set`「冲突在落地那刻重查、未获许可就什么都不做」）。
            using (var fileStream = new FileStream(fullPath, overwrite ? FileMode.Create : FileMode.CreateNew))
            {
                stream.CopyTo(fileStream);
                bytes = fileStream.Length;
            }
        }
        catch (IOException) when (!overwrite && File.Exists(fullPath))
        {
            return CommandResult.Ok(new JsonObject
            {
                ["path"] = fullPath,
                ["format"] = format,
                ["formatName"] = displayName,
                ["outcome"] = "appeared_meanwhile",
                ["overwrite"] = false,
            });
        }
        catch (Exception ex) { return CommandResult.Fail("write_failed", string.Format("failed to write \"{0}\" — {1}", fullPath, ex.Message)); }

        return CommandResult.Ok(new JsonObject
        {
            ["path"] = fullPath,
            ["format"] = format,
            ["formatName"] = displayName,
            ["outcome"] = "applied",
            ["overwrite"] = overwrite,
            ["bytes"] = bytes,
            ["note"] = string.IsNullOrEmpty(message) ? null : message,
        });
    }

    // 播放头位置随 native 元数据一并写出（重开时落在同一处）。用 AudioEngine 现刻时间换算，与脚本面 tl.playhead() 同源，
    // 免得为一个字段依赖 Editor。
    static double PlayheadTick(IProject project)
    {
        try { return project.TempoManager.GetTick(AudioEngine.CurrentTime); }
        catch { return 0; }
    }

    static string SupportedList()
    {
        var formats = FormatsManager.GetAllExportFormats();
        return formats.Count == 0 ? "(none)" : string.Join(", ", formats.Select(f => "." + f));
    }

    // 措辞与搬家前逐字一致。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var path = obj["path"]!.GetValue<string>();
        switch (obj["outcome"]!.GetValue<string>())
        {
            case "refused":
                return obj["note"]!.GetValue<string>();
            case "appeared_meanwhile":
                return string.Format(
                    "A file appeared at \"{0}\" while waiting for authorization, and the user only approved writing a NEW file there, not replacing one. " +
                    "Nothing was written. Ask again if they want to replace it.", path);
            default:
                return (obj["note"]?.GetValue<string>() ?? string.Empty) + string.Format(
                    "Exported the project as {0} to \"{1}\" ({2}){3}. Note this was a copy: the user's project is still open with the same save file and unsaved changes as before.",
                    obj["formatName"]!.GetValue<string>(), path, FormatSize(obj["bytes"]!.GetValue<long>()),
                    obj["overwrite"]!.GetValue<bool>() ? ", replacing the file that was there" : "");
        }
    }

    static string FormatSize(long bytes)
        => bytes >= 1024 * 1024 ? string.Format("{0:0.0} MB", bytes / 1024.0 / 1024.0)
         : bytes >= 1024 ? string.Format("{0:0.0} KB", bytes / 1024.0)
         : bytes + " bytes";
}

// 保存那一族：**走用户按 Ctrl+S 的同一条下游**（写文件 + 把工程的保存路径挪过去 + 清未保存标记），
// 而不是另造一份"命令面自己维护的副本"。
//
// 【为什么不做副本】副本会造出两份真相：用户在标题栏上看到的仍是"未保存"，而调用方以为存过了。
// 那是假装成功换了个形式。副本语义已经有人做了——`project export` 就是它，且它的文档明写着
// 不要把导出说成保存。
//
// 【与动作面的关系】`file.save` 那条无参动作照旧（菜单与 Ctrl+S 走它）。这里两条是**带参数的形状**，
// 与 `project open` / `file.open` 并存是同一个先例：动作面无参，而"存到这个路径"要一个参数；
// 且命令面这条不弹任何框——菜单那条在没有保存目标时会转去弹文件选择器，而这里没人应答那个框。
internal sealed class ProjectSaveCommand : ICommand
{
    public string Path => "project save";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "save_project";

    public string Brief => "Save the project back to its own file";

    public string Documentation =>
        "Save the user's project back to the file it came from — exactly what Ctrl+S does: the file is overwritten, and the project stops being \"unsaved\". "
        + "This is a REAL save, not a copy: use export_project when you want to write a copy somewhere without touching where their project lives. "
        + "\nIt refuses when the project has never been saved (there is no path to save back to) — use save_project_as with a path for that. It also refuses in a headless process: saving is about the document a person has open in a window, and a headless run should write its result with export_project instead. "
        + "\nThe undo history does not cover files, so this always needs the user's authorization.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        if (ctx.ProjectFile is not { } file)
            return CommandResult.Fail("no_editor", ProjectSaveSupport.NoEditor);

        if (!file.HasSaveTarget)
            return CommandResult.Fail("never_saved",
                "this project has never been saved, so there is no file to save it back to. Use save_project_as with a path.");

        var target = file.Path!;
        // 已经是保存状态就不写盘：那样既不动用户的文件，也不假装"我保存了什么"。
        if (file.IsSaved)
            return CommandResult.Ok(new JsonObject { ["path"] = target, ["outcome"] = "already_saved" });

        var (proceed, message) = await ctx.Authorize(new AuthorizationRequest(WriteKind.ProjectSave, 0, target), cancellationToken);
        if (!proceed)
            return CommandResult.Ok(new JsonObject { ["path"] = target, ["outcome"] = "refused", ["note"] = message });

        return CommandResult.Ok(await ctx.OnMainThread(() => ProjectSaveSupport.Run(file, null, target, message)));
    }

    public string Render(JsonNode? data, CommandArgs args) => ProjectSaveSupport.Render(data, saveAs: false);
}

internal sealed class ProjectSaveAsCommand : ICommand
{
    public string Path => "project save-as";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "save_project_as";

    public string Brief => "Save the project to a new path and keep working there";

    public string Documentation =>
        "Save the user's project to a path you give — exactly what Save As does: the file is written, and **from then on that is the file their project saves to**. "
        + "That last part is the difference from export_project, which only writes a copy and leaves their project pointing at the old file; if a copy is what you want, use that one instead. "
        + "\nThe format is TuneLab's own project format, so give the path a .tlpx extension (that is what the editor writes). An existing file at that path is replaced, which the authorization card says out loud. It refuses in a headless process — see save_project. "
        + "\nThe undo history does not cover files, so this always needs the user's authorization.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Absolute local path to save to, e.g. C:\\Users\\me\\song.tlpx. The parent folder must already exist." }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var given = (args.Json.GetString("path") ?? "").Trim().Trim('"');
        if (given.Length == 0)
            return CommandResult.Fail("empty_path", "\"path\" is required.");

        if (ctx.ProjectFile is not { } file)
            return CommandResult.Fail("no_editor", ProjectSaveSupport.NoEditor);

        string fullPath;
        try { fullPath = System.IO.Path.GetFullPath(given); }
        catch (Exception ex) { return CommandResult.Fail("bad_path", string.Format("\"{0}\" is not a usable file path — {1}", given, ex.Message)); }

        var folder = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            return CommandResult.Fail("missing_folder", string.Format("there is no folder at \"{0}\". Create it first, or pick another path.", folder));

        bool overwrite = File.Exists(fullPath);
        var (proceed, message) = await ctx.Authorize(
            new AuthorizationRequest(overwrite ? WriteKind.ProjectSaveAsOverwrite : WriteKind.ProjectSaveAs, 0, fullPath), cancellationToken);
        if (!proceed)
            return CommandResult.Ok(new JsonObject { ["path"] = fullPath, ["outcome"] = "refused", ["note"] = message });

        return CommandResult.Ok(await ctx.OnMainThread(() => ProjectSaveSupport.Run(file, fullPath, fullPath, message, overwrite)));
    }

    public string Render(JsonNode? data, CommandArgs args) => ProjectSaveSupport.Render(data, saveAs: true);
}

// 两条保存命令共用的落地与措辞（一处真源：两边说的"保存"必须是同一件事）。
internal static class ProjectSaveSupport
{
    public const string NoEditor =
        "There is no editor in this process, so there is no document to save. Saving is about the project a person has open in a window (attach to one); "
        + "a headless run should write its result with export_project, which takes a path.";

    public static JsonNode Run(IProjectFileAccess file, string? path, string target, string note, bool overwrite = false)
    {
        if (file.Save(path) is { } error)
            return new JsonObject { ["path"] = target, ["outcome"] = "failed", ["note"] = error };

        return new JsonObject
        {
            ["path"] = file.Path ?? target,
            ["outcome"] = overwrite ? "replaced" : "saved",
            ["note"] = string.IsNullOrEmpty(note) ? null : note,
        };
    }

    public static string Render(JsonNode? data, bool saveAs)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var note = obj["note"]?.GetValue<string>() ?? string.Empty;
        var path = obj["path"]!.GetValue<string>();
        switch (obj["outcome"]!.GetValue<string>())
        {
            case "refused":
                return note;
            case "failed":
                return string.Format("Could NOT save to \"{0}\": {1}. The project is still unsaved.", path, note);
            case "already_saved":
                return string.Format("Nothing to do: the project has no unsaved changes, and it is already saved at \"{0}\".", path);
            default:
                return note + string.Format(
                    saveAs
                        ? "Saved the project as \"{0}\". From now on that is the file it saves to; the project is no longer unsaved."
                        : "Saved the project to \"{0}\". It is no longer unsaved.",
                    path);
        }
    }
}

// 渲染音频并落地。
//
// 【这是对"音频导出刻意不收"的翻案】当初判它不收的理由只有一条：渲染期界面锁住几分钟，占不占这台机器
// 是人在环的决定。但 headless 根本没有界面可锁，而 attach 下"人在环"恰恰是授权闸门该管的事——把代价
// 写进卡片，让用户自己决定，比替他决定"你不能这么做"要诚实。
//
// 【真正的难点不是渲染，是等】AudioEngine.ExportMaster 拉的是 AudioGraph 此刻的数据，不等任何人。
// 界面上的导出之所以行得通，是因为用户看着状态带自己等到全绿才按下去——那个"等"是人做的，不在代码里。
// 照搬到命令面就会静默产出静音段，而回报仍说导出成功。故这条命令自己把合成驱动到落定
//（SynthesisCompletion），再看链尾上到底是什么：
//   · 超时  → 什么都不写，如实报还剩多少（半截音频比没有音频更坏：它看起来是成品）；
//   · 有失败段 → 默认拒绝，因为那些范围会被导成【静音】；要 allowIncomplete 才导，且列出坏在哪；
//   · 有降级段 → 只警告（那是能听的 passthrough，不是无声）。
internal sealed class ProjectExportAudioCommand : ICommand
{
    // 默认等 5 分钟。这不是"渲染要多久"的估计（那取决于引擎与曲子长度），而是"卡住了多久算卡住"。
    const int DefaultTimeoutSeconds = 300;
    const int MinTimeoutSeconds = 10;
    const int MaxTimeoutSeconds = 3600;

    public string Path => "project export-audio";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "export_audio";

    public string Brief => "Render the project's audio and write it to a file";

    public string Documentation =>
        "Render the current project down to ONE audio file (the mixdown of every track, exactly what the user hears on play). "
        + "The format comes from the file extension: .wav, .mp3, .flac or .ogg. Sample rate, bit depth and bitrate come from the project's own export settings "
        + "(a script can read and change them: project.exportSampleRate, project.exportBitDepth, project.exportBitrate, project.masterExportChannels). "
        + "\nBEFORE writing anything this drives the whole project's synthesis to completion and waits for it, because the mix is only real once every part has finished — "
        + "exporting early would silently write silence where a part had not been synthesized yet. That wait is the expensive part: it runs every voice and effect, "
        + "which can take several minutes, and if the user has TuneLab open their window is locked up for that whole time. Say so before you call this. "
        + "\nIf synthesis does not finish in time, NOTHING is written and you are told how much was left — half a render is worse than none, because it looks finished. "
        + "\nIf any range failed to synthesize it would come out SILENT, so this refuses by default and names the ranges; pass allowIncomplete: true to export anyway. "
        + "Ranges where an effect failed and its unprocessed audio is being played instead are only reported as a warning — they do have sound, just not the intended one. "
        + "\nThis writes a file anywhere on the user's disk and the undo history does not cover files, so it ALWAYS needs the user's authorization. "
        + "Exporting audio does NOT save the user's project — use save_project for that, and export_project to write a copy of the project itself.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Absolute local file path to write, including the extension, which picks the format: .wav, .mp3, .flac or .ogg. e.g. C:\\Users\\me\\song.wav. The parent folder must already exist." },
            "timeoutSeconds": { "type": "integer", "description": "How long to wait for synthesis to finish before giving up and writing nothing. Default 300 (5 minutes), allowed 10-3600." },
            "allowIncomplete": { "type": "boolean", "description": "Export even if some ranges failed to synthesize and will therefore be SILENT in the file. Default false. Only pass true if the user has been told which ranges are broken and wants the file anyway." }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var given = (args.Json.GetString("path") ?? "").Trim().Trim('"');
        if (given.Length == 0)
            return CommandResult.Fail("empty_path", "\"path\" is required.");
        if (ctx.Project is not { } project)
            return CommandResult.Fail("no_project", "no project is open, so there is nothing to render.");

        int timeoutSeconds = Math.Clamp(args.Json.GetIntOrNull("timeoutSeconds") ?? DefaultTimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds);
        bool allowIncomplete = args.Json.GetBoolOrNull("allowIncomplete") ?? false;

        // 路径与格式先行（不为坏请求打扰用户，同 `project export`）。
        string fullPath;
        try { fullPath = System.IO.Path.GetFullPath(given); }
        catch (Exception ex) { return CommandResult.Fail("bad_path", string.Format("\"{0}\" is not a usable file path — {1}", given, ex.Message)); }
        if (Directory.Exists(fullPath))
            return CommandResult.Fail("path_is_folder", string.Format("\"{0}\" is a folder, not a file path. Give the full path including the file name and extension.", fullPath));

        var extension = System.IO.Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();
        if (!AudioExportFormatExtensions.TryParseId(extension, out var format))
            return CommandResult.Fail("unsupported_format", string.Format(
                "cannot render audio as \"{0}\" — the extension picks the format and it has to be one of: {1}. (Project files go through export_project instead.)",
                string.IsNullOrEmpty(extension) ? fullPath : "." + extension,
                string.Join(", ", AudioExportFormatExtensions.AllIds.Select(id => "." + id))));

        var folder = System.IO.Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return CommandResult.Fail("missing_folder", string.Format("the folder \"{0}\" does not exist. Create it first or pick an existing folder (this command won't create folders).", folder));

        // 编码参数取工程自己的导出设置（用户在导出侧栏里设的那一份），格式则由扩展名定——
        // 路径是这次调用说了算的东西，其余的是工程的属性。
        int sampleRate = Math.Max(project.ExportSampleRate, 1);
        bool isStereo = project.MasterExportChannels >= 2;
        // 位深按格式收敛到编码器真正会用的那一档（flac 只有 16/24，wav 还有 32），好让回报里报的
        // 就是文件里真正写下的——报一个编码器会悄悄改掉的数字等于说假话。
        int bitDepth = format switch
        {
            AudioExportFormat.Flac => project.ExportBitDepth == 24 ? 24 : 16,
            AudioExportFormat.Wav => project.ExportBitDepth is 24 or 32 ? project.ExportBitDepth : 16,
            _ => project.ExportBitDepth,
        };
        var settings = new AudioEncodeSettings { Format = format, BitDepth = bitDepth, Bitrate = project.ExportBitrate };
        var formatName = format.Id().ToUpperInvariant();

        // 空工程在【闸门之前】拦下（但排在路径/格式之后：那两样是调用方自己拼错了，先说）。
        // 混音的长度自带一秒尾，什么都不拦的话这条命令会一本正经地渲染出
        // 一秒静音并回报成功——那是最难被发现的一种谎。判据用工程里有没有 part，而不是混音有多长。
        if (!project.Tracks.Any(track => track.Parts.Count > 0))
            return CommandResult.Fail("nothing_to_render", "this project is empty (no track has any part in it), so there is nothing to render.");
        bool overwrite = File.Exists(fullPath);
        // 恒过闸门：路径任意、覆盖救不回，且这一条还要占住用户的机器好几分钟——那句话必须在卡片上
        //（见 WriteKind.ProjectExportAudio）。问在渲染【之前】：白等五分钟再被拒绝是最差的顺序。
        var (proceed, message) = await ctx.Authorize(
            new AuthorizationRequest(overwrite ? WriteKind.ProjectExportAudioOverwrite : WriteKind.ProjectExportAudio, 0, fullPath, formatName),
            cancellationToken);
        if (!proceed)
            return CommandResult.Ok(new JsonObject { ["path"] = fullPath, ["outcome"] = "refused", ["note"] = message });

        // ── 把合成驱动到落定。每一跳都上数据线程走一拍，跳与跳之间让出去，好让引擎的续体
        //    （编辑器里是 UI 线程的 Dispatcher，headless 里是驱动循环的泵）跑起来。
        var clock = System.Diagnostics.Stopwatch.StartNew();
        SynthesisTick tick;
        while (true)
        {
            tick = await ctx.OnMainThread(() => SynthesisCompletion.DriveOnce(project));
            if (tick.Done)
                break;
            if (clock.Elapsed.TotalSeconds >= timeoutSeconds)
                return CommandResult.Ok(new JsonObject
                {
                    ["path"] = fullPath,
                    ["outcome"] = "timeout",
                    ["seconds"] = timeoutSeconds,
                    ["busy"] = tick.Busy,
                    ["pending"] = tick.Pending,
                });

            try { await Task.Delay(50, cancellationToken); }
            catch (OperationCanceledException) { return CommandResult.Fail("cancelled", "cancelled while waiting for synthesis; nothing was written."); }
        }

        var facts = await ctx.OnMainThread(() => SynthesisCompletion.Inspect(project));
        if (facts.Silent.Count > 0 && !allowIncomplete)
            return CommandResult.Ok(new JsonObject
            {
                ["path"] = fullPath,
                ["outcome"] = "would_be_silent",
                ["silent"] = Flaws(facts.Silent),
            });

        // 时长取整个混音的长度（与界面上按导出键得到的同一口径：AudioGraph 的末端，自带一秒尾）。
        double duration = await ctx.OnMainThread(() => AudioEngine.EndTime);

        // 渲染 + 编码跑在后台线程上（纯计算，占住数据线程的话界面连进度框都画不出来），而**前面挡一个
        // 模态框**——授权卡片写着"期间用户的窗口会被锁住"，用户是据那句话决定要不要现在交出机器的，
        // 没有这个框那就是假话：混音是逐块算的，用户中途改一下工程就得到一个前后不同源的文件，
        // 回报还说导出成功。界面上那条导出从来没这问题，正是因为它前面挡着同一个框。
        // headless 没有窗口（ModalProgress 为 null），就地跑——那儿也没有第二双手会去改工程。
        try
        {
            var render = (IProgress<double> progress, CancellationToken token) =>
            {
                AudioEngine.ExportMaster(fullPath, isStereo, sampleRate, settings, progress, token);
                return true;
            };
            if (ctx.ModalProgress is { } modal)
                await modal.RunBehindModalAsync(render, cancellationToken);
            else
                await Task.Run(() => render(new Progress<double>(), cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return CommandResult.Fail("cancelled", string.Format("cancelled while rendering; \"{0}\" may be incomplete or missing.", fullPath));
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("write_failed", string.Format("failed to render \"{0}\" — {1}", fullPath, ex.Message));
        }

        long bytes = 0;
        try { bytes = new FileInfo(fullPath).Length; } catch { /* 大小只是回报里的一个数字，取不到就不报 */ }

        return CommandResult.Ok(new JsonObject
        {
            ["path"] = fullPath,
            ["outcome"] = "applied",
            ["format"] = format.Id(),
            ["formatName"] = formatName,
            ["sampleRate"] = sampleRate,
            ["channels"] = isStereo ? 2 : 1,
            ["bitDepth"] = format.IsLossy() ? null : bitDepth,
            ["bitrate"] = format.IsLossy() ? settings.Bitrate : null,
            ["seconds"] = Math.Round(duration, 3),
            ["bytes"] = bytes,
            ["overwrite"] = overwrite,
            ["waitedSeconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1),
            ["silent"] = facts.Silent.Count > 0 ? Flaws(facts.Silent) : null,
            ["degraded"] = facts.Degraded.Count > 0 ? Flaws(facts.Degraded) : null,
            ["note"] = string.IsNullOrEmpty(message) ? null : message,
        });
    }

    static JsonArray Flaws(IReadOnlyList<SynthesisFlaw> flaws)
    {
        var array = new JsonArray();
        foreach (var flaw in flaws)
            array.Add(new JsonObject
            {
                ["where"] = flaw.Where,
                ["startSeconds"] = Math.Round(flaw.StartTime, 3),
                ["endSeconds"] = Math.Round(flaw.EndTime, 3),
                ["message"] = string.IsNullOrEmpty(flaw.Message) ? null : flaw.Message,
            });
        return array;
    }

    static string DescribeFlaws(JsonNode? node)
    {
        if (node is not JsonArray array)
            return string.Empty;

        var text = new StringBuilder();
        foreach (var item in array)
        {
            if (item is not JsonObject flaw)
                continue;
            text.AppendFormat("\n  · {0}, {1:0.###}s–{2:0.###}s",
                flaw["where"]!.GetValue<string>(), flaw["startSeconds"]!.GetValue<double>(), flaw["endSeconds"]!.GetValue<double>());
            if (flaw["message"]?.GetValue<string>() is { Length: > 0 } why)
                text.Append(" — ").Append(why.Replace("\n", " "));
        }
        return text.ToString();
    }

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var path = obj["path"]!.GetValue<string>();
        switch (obj["outcome"]!.GetValue<string>())
        {
            case "refused":
                return obj["note"]?.GetValue<string>() ?? string.Empty;

            case "timeout":
                return string.Format(
                    "Synthesis did not finish within {0}s ({1} part(s) still rendering, {2} still waiting), so NOTHING was written to \"{3}\". "
                    + "The project is unchanged and whatever did get synthesized is still there — call this again to keep going, with a longer timeoutSeconds if the project is a big one.",
                    obj["seconds"]!.GetValue<int>(), obj["busy"]!.GetValue<int>(), obj["pending"]!.GetValue<int>(), path);

            case "would_be_silent":
                return string.Format(
                    "Did NOT export: synthesis finished, but these ranges failed and would come out SILENT in the file:{0}\n"
                    + "Nothing was written to \"{1}\". Tell the user what is broken; if they want the file with those gaps anyway, call again with allowIncomplete: true.",
                    DescribeFlaws(obj["silent"]), path);

            default:
                var text = new StringBuilder(obj["note"]?.GetValue<string>() ?? string.Empty);
                text.AppendFormat("Rendered the mix to \"{0}\" — {1}, {2}, {3} Hz, {4}{5}.",
                    path,
                    obj["formatName"]!.GetValue<string>(),
                    obj["channels"]!.GetValue<int>() >= 2 ? "stereo" : "mono",
                    obj["sampleRate"]!.GetValue<int>(),
                    obj["bitDepth"] is { } depth ? depth.GetValue<int>() + "-bit" : obj["bitrate"]!.GetValue<int>() + " kbps",
                    obj["overwrite"]!.GetValue<bool>() ? ", replacing the file that was there" : "");
                text.AppendFormat(" {0:0.#}s of audio, {1}. Synthesis took {2:0.#}s.",
                    obj["seconds"]!.GetValue<double>(), FormatSize(obj["bytes"]!.GetValue<long>()), obj["waitedSeconds"]!.GetValue<double>());

                if (obj["silent"] is { } silent)
                    text.AppendFormat("\nWARNING — these ranges failed to synthesize and are SILENT in the file (you asked for it with allowIncomplete):{0}", DescribeFlaws(silent));
                if (obj["degraded"] is { } degraded)
                    text.AppendFormat("\nNote — an effect failed on these ranges, so the file has the unprocessed audio there rather than the intended sound:{0}", DescribeFlaws(degraded));

                text.Append("\nThis did not save the user's project: it still has the same save file and unsaved changes as before.");
                return text.ToString();
        }
    }

    static string FormatSize(long bytes)
        => bytes >= 1024 * 1024 ? string.Format("{0:0.0} MB", bytes / 1024.0 / 1024.0)
         : bytes >= 1024 ? string.Format("{0:0.0} KB", bytes / 1024.0)
         : bytes + " bytes";
}
