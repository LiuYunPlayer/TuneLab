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
        "so never tell the user you saved their project. This cannot export AUDIO (wav/mp3/...): rendering audio locks the UI for a long time, " +
        "so it's the user's call — set up what's needed and let them press export themselves.";

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
