using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.Extensions.Instruments;
using TuneLab.Extensions.Voices;
using TuneLab.SDK;

namespace TuneLab.Commands.Handlers;

// part preset 的外部面：侧栏 Part 面板顶上那一行预设，外部也够得着。
//
// 【为什么是命令、不是动作】动作面无参，而这一族的每一件事都要参数——哪条预设、用在哪个 part、
// 存成什么名字。同 `project open` 的判据：界面上那个入口只能弹个列表让人挑，而"用这一条"要一个名字。
// 应用 preset 确实写工程数据（那本该归脚本面 `tl.*`），但预设的内容在**用户的配置目录里**，
// 脚本面读不到它，故这条通道无可替代——与 `script run-saved` 同一个理由（库里的东西要按名字取用）。
//
// 【一件事只做一遍】"怎么把一条预设用在 part 上""从 part 抓一条"归 TuneLab.Configs.PartPresets，
// 与侧栏那个入口共用；文件怎么放、名字怎么校验归 PresetConfigManager。这里只做外部面该做的：
// 定位、闸门、回报。
//
// 【今天只有 part preset】effect 的实例 / 链 preset 设计已定稿但实现暂缓（issue #141）。届时不必另开
// 一族命令：同一个 `Presets\` 文件夹、后缀名唯一声明种类，这几条加一个 kind 参数即可（加性）。
internal static class PresetSupport
{
    // 名字落到磁盘上的那一条（大小写不敏感，同 PresetConfigManager 的文件查找口径）。
    public static PartPreset? Find(string name, out IReadOnlyList<PartPreset> all)
    {
        var presets = PresetConfigManager.LoadPresets();
        all = presets;
        foreach (var preset in presets)
        {
            if (preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return preset;
        }
        return null;
    }

    public static string NotFound(string name, IReadOnlyList<PartPreset> all)
    {
        var sb = new StringBuilder();
        sb.Append(string.Format("There is no preset called \"{0}\".", name));
        if (all.Count == 0)
            sb.Append(" There are no presets saved at all — make one with save_preset.");
        else
        {
            sb.Append(" Saved presets: ");
            for (int i = 0; i < all.Count; i++)
                sb.Append(i == 0 ? "" : ", ").Append('"').Append(all[i].Name).Append('"');
            sb.Append('.');
        }
        return sb.ToString();
    }

    // 按【1-based 轨号 + part 号】定位一个 midi part——与 `project status` / `get_editor_status` 报的号
    // 是同一套（换口径会让两条命令说的"第 2 轨"指不同的轨）。
    // 音频 part 没有声源/属性/自动化，preset 对它无意义，故如实拒绝而不是当成"没找到"。
    public static (IMidiPart? Part, string? Error) Locate(CommandContext ctx, int trackNumber, int partNumber)
    {
        if (ctx.Project is not { } project)
            return (null, "No project is open, so there is no part to act on.");

        var tracks = project.Tracks;
        if (trackNumber < 1 || trackNumber > tracks.Count)
            return (null, string.Format("There is no track {0} — the project has {1}. Call get_project_overview to see them.", trackNumber, tracks.Count));

        var parts = new List<IPart>(tracks[trackNumber - 1].Parts);
        if (partNumber < 1 || partNumber > parts.Count)
            return (null, string.Format("Track {0} has no part {1} — it has {2}. Call get_project_overview to see them.", trackNumber, partNumber, parts.Count));

        if (parts[partNumber - 1] is not IMidiPart midiPart)
            return (null, string.Format("Part {0} of track {1} is an audio part: it has no sound source, properties or automation, so a preset does not apply to it.", partNumber, trackNumber));

        return (midiPart, null);
    }

    public static string Where(int trackNumber, int partNumber)
        => string.Format("part {0} of track {1}", partNumber, trackNumber);

    // 声源的人话名字（引擎显示名 + 声库 id），与 list_sound_sources 同一口径。
    public static string SourceText(SoundSourceInfo source)
    {
        if (string.IsNullOrEmpty(source.Type))
            return "(no sound source)";

        var engine = source.Kind == SourceKind.Voice
            ? VoicesManager.GetDisplayName(source.Type)
            : InstrumentsManager.GetDisplayName(source.Type);
        return string.IsNullOrEmpty(source.Id) ? engine : engine + " / " + source.Id;
    }
}

internal sealed class PresetListCommand : ICommand
{
    public string Path => "preset list";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "list_presets";

    public string Brief => "List the saved part presets and what each one holds";

    public string Documentation =>
        "List the user's saved part presets — the ones in the preset row at the top of the Part panel. "
        + "A part preset is a snapshot of a part's SOUND: its sound source, the part-level properties, and the default value of each automation track. "
        + "It deliberately does NOT hold any timeline content — no curves, no notes, no phonemes — so applying one changes how a part sounds, not what it sings. "
        + "\nEach entry gives its name, the sound source it carries, and how many properties and automation defaults are in it. "
        + "Apply one with apply_preset, make one from a part with save_preset. Read-only.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    public Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var presets = new JsonArray();
        foreach (var preset in PresetConfigManager.LoadPresets())
        {
            presets.Add(new JsonObject
            {
                ["name"] = preset.Name,
                ["source"] = new JsonObject
                {
                    ["kind"] = preset.Source.Kind == SourceKind.Voice ? "voice" : "instrument",
                    ["type"] = preset.Source.Type,
                    ["id"] = preset.Source.Id,
                    ["label"] = PresetSupport.SourceText(preset.Source),
                },
                ["properties"] = preset.Properties.Map.Count,
                ["automations"] = preset.Automations.Count,
            });
        }
        return Task.FromResult(CommandResult.Ok(new JsonObject { ["presets"] = presets }));
    }

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var presets = obj["presets"]!.AsArray();
        if (presets.Count == 0)
            return "No part presets are saved. Make one from a part with save_preset.";

        var sb = new StringBuilder();
        sb.Append(presets.Count).Append(" part preset(s). Each one holds a sound source + part properties + automation defaults (no curves, no notes).");
        sb.Append("\nFormat: \"<name>\" — <sound source>, <n> properties, <n> automation defaults");
        foreach (var node in presets)
        {
            var preset = node!.AsObject();
            sb.Append("\n- \"").Append(preset["name"]!.GetValue<string>()).Append("\" — ")
              .Append(preset["source"]!["label"]!.GetValue<string>())
              .Append(", ").Append(preset["properties"]!.GetValue<int>()).Append(" properties")
              .Append(", ").Append(preset["automations"]!.GetValue<int>()).Append(" automation defaults");
        }
        return sb.ToString();
    }
}

internal sealed class PresetApplyCommand : ICommand
{
    public string Path => "preset apply";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "apply_preset";

    public string Brief => "Apply a saved preset to one part (or reset it to defaults)";

    public string Documentation =>
        "Apply one saved part preset to one MIDI part: it sets the part's sound source, resets the part properties to that source's defaults and then writes the preset's values over them, and sets each automation track's DEFAULT value. "
        + "Leave \"name\" out to do what the preset row's \"None\" does instead: reset the part's parameters to its sound source's own defaults. "
        + "That one does NOT change the sound source — there is no such thing as a default sound source to go back to, and the source is something the user picks separately; to undo an earlier apply, undo it. "
        + "\nIt does NOT touch timeline content — the notes, the pitch line, the automation curves and the phonemes all stay exactly as they are; only the values they are drawn against change. "
        + "It lands as ONE undoable change, so the user can take it back with Ctrl+Z. "
        + "\nOnly what the CURRENT sound source declares is touched. A part that has been through other engines still carries their property keys, on purpose (switch back and the values are still there), and nothing here clears them — so on a part whose source declares no properties at all, resetting genuinely changes nothing, and the reply says so. "
        + "\nThe part is named by its 1-based track and part number, the same numbers get_project_overview and get_editor_status report.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "track": { "type": "integer", "description": "1-based track number, as reported by get_project_overview." },
            "part": { "type": "integer", "description": "1-based part number within that track." },
            "name": { "type": "string", "description": "Preset name exactly as listed by list_presets. Leave it out to reset the part to its sound source's defaults instead." }
          },
          "required": ["track", "part"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        int trackNumber = args.Json.GetIntOrNull("track") ?? 0;
        int partNumber = args.Json.GetIntOrNull("part") ?? 0;
        var name = (args.Json.GetStringOrNull("name") ?? "").Trim();

        var (part, locateError) = await ctx.OnMainThread(() => PresetSupport.Locate(ctx, trackNumber, partNumber));
        if (part == null)
            return CommandResult.Fail("no_such_part", locateError!);

        PartPreset? preset = null;
        if (name.Length != 0)
        {
            preset = PresetSupport.Find(name, out var all);
            if (preset == null)
                return CommandResult.Fail("not_found", PresetSupport.NotFound(name, all));
        }

        var where = PresetSupport.Where(trackNumber, partNumber);
        var (proceed, message) = await ctx.Authorize(
            new AuthorizationRequest(WriteKind.PresetApply, 0, preset?.Name, SecondaryTarget: where), cancellationToken);
        if (!proceed)
            return CommandResult.Ok(new JsonObject { ["outcome"] = "refused", ["note"] = message });

        var applied = await ctx.OnMainThread(() => PartPresets.Apply([part], preset));

        return CommandResult.Ok(new JsonObject
        {
            ["outcome"] = "applied",
            ["name"] = preset?.Name,
            ["where"] = where,
            ["source"] = preset == null ? null : PresetSupport.SourceText(preset.Source),
            // 实际动了多少（当前声源声明的范围）。0 是有意义的答案，不是缺省值——见 Render。
            ["properties"] = applied.Properties,
            ["automations"] = applied.Automations,
            ["note"] = string.IsNullOrEmpty(message) ? null : message,
        });
    }

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var note = obj["note"]?.GetValue<string>() ?? string.Empty;
        if (obj["outcome"]!.GetValue<string>() == "refused")
            return note;

        var where = obj["where"]!.GetValue<string>();
        int properties = obj["properties"]?.GetValue<int>() ?? 0;
        int automations = obj["automations"]?.GetValue<int>() ?? 0;
        var untouched = " The notes, curves and phonemes are untouched; Ctrl+Z takes it back.";

        if (obj["name"]?.GetValue<string>() is not { } name)
        {
            // 声源一个属性都不声明时（换过引擎的 part 上很常见）"已重置"就是句空话——如实说，
            // 并点出它本来也不动声源，免得读成"重置坏了"。
            return note + (properties == 0 && automations == 0
                ? string.Format("Reset {0}: nothing changed — its sound source declares no part properties or automation tracks to reset. (This never changes the sound source itself, and property keys left over from other engines stay as they are.)", where)
                : string.Format("Reset {0} to its sound source's defaults: {1} propert{2}, {3} automation default{4}. The sound source itself is unchanged.{5}",
                    where, properties, properties == 1 ? "y" : "ies", automations, automations == 1 ? "" : "s", untouched));
        }

        return note + string.Format("Applied the preset \"{0}\" ({1}) to {2}: {3} propert{4}, {5} automation default{6}.{7}",
            name, obj["source"]!.GetValue<string>(), where,
            properties, properties == 1 ? "y" : "ies", automations, automations == 1 ? "" : "s", untouched);
    }
}

internal sealed class PresetSaveCommand : ICommand
{
    public string Path => "preset save";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "save_preset";

    public string Brief => "Save one part's sound as a reusable preset";

    public string Documentation =>
        "Snapshot one MIDI part's SOUND into a named preset the user can reuse from the Part panel: its sound source, its part properties, and each automation track's default value. "
        + "Timeline content is deliberately left out (no curves, no notes, no phonemes). Values the part never set explicitly are written into the snapshot at the engine's CURRENT defaults, on purpose — a default is part of how it sounds, and the preset should keep sounding the same if the engine changes its defaults later. "
        + "\nThe name becomes the file name in the user's presets folder, so it has to be a valid file name; an existing preset with the same name is replaced, which needs the user's authorization. "
        + "\nThe part is named by its 1-based track and part number, the same numbers get_project_overview reports.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "track": { "type": "integer", "description": "1-based track number, as reported by get_project_overview." },
            "part": { "type": "integer", "description": "1-based part number within that track." },
            "name": { "type": "string", "description": "Name for the preset (it becomes the file name, so no < > : \" / \\ | ? * and at most 100 characters)." }
          },
          "required": ["track", "part", "name"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        int trackNumber = args.Json.GetIntOrNull("track") ?? 0;
        int partNumber = args.Json.GetIntOrNull("part") ?? 0;
        var name = (args.Json.GetString("name") ?? "").Trim();

        // 名字校验与界面同一份（文件名即 preset 名，故必须是合法文件名，且跨平台一致）。
        if (PresetConfigManager.GetPresetNameError(name) is { } nameError)
            return CommandResult.Fail("invalid_name", nameError);

        var (part, locateError) = await ctx.OnMainThread(() => PresetSupport.Locate(ctx, trackNumber, partNumber));
        if (part == null)
            return CommandResult.Fail("no_such_part", locateError!);

        // 覆盖已有的那一条才过闸门（同 save_script：新建一条是加东西，替换掉一条是删东西）。
        bool overwrite = PresetSupport.Find(name, out _) != null;
        var note = string.Empty;
        if (overwrite)
        {
            var (proceed, message) = await ctx.Authorize(new AuthorizationRequest(WriteKind.PresetOverwrite, 0, name), cancellationToken);
            if (!proceed)
                return CommandResult.Ok(new JsonObject { ["outcome"] = "refused", ["note"] = message });
            note = message;
        }

        var preset = await ctx.OnMainThread(() => PartPresets.Capture(part, name));
        try { PresetConfigManager.SavePreset(preset); }
        catch (Exception ex) { return CommandResult.Fail("save_failed", ex.Message); }

        return CommandResult.Ok(new JsonObject
        {
            ["outcome"] = overwrite ? "replaced" : "created",
            ["name"] = name,
            ["where"] = PresetSupport.Where(trackNumber, partNumber),
            ["source"] = PresetSupport.SourceText(preset.Source),
            ["properties"] = preset.Properties.Map.Count,
            ["automations"] = preset.Automations.Count,
            ["note"] = string.IsNullOrEmpty(note) ? null : note,
        });
    }

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var note = obj["note"]?.GetValue<string>() ?? string.Empty;
        var outcome = obj["outcome"]!.GetValue<string>();
        if (outcome == "refused")
            return note;

        return note + string.Format("{0} the preset \"{1}\" from {2}: {3}, {4} properties, {5} automation defaults. It holds no curves, notes or phonemes.",
            outcome == "replaced" ? "Replaced" : "Saved",
            obj["name"]!.GetValue<string>(),
            obj["where"]!.GetValue<string>(),
            obj["source"]!.GetValue<string>(),
            obj["properties"]!.GetValue<int>(),
            obj["automations"]!.GetValue<int>());
    }
}

internal sealed class PresetDeleteCommand : ICommand
{
    public string Path => "preset delete";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "delete_preset";

    public string Brief => "Delete a saved part preset";

    public string Documentation =>
        "Delete one saved part preset by name. This deletes the user's file and CANNOT be undone (the undo history only covers the project), so it always needs their authorization. "
        + "Parts that were made with it are not affected — a preset is a starting point, not a live link.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": { "name": { "type": "string", "description": "Preset name exactly as listed by list_presets." } },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var name = (args.Json.GetString("name") ?? "").Trim();
        var preset = PresetSupport.Find(name, out var all);
        if (preset == null)
            return CommandResult.Fail("not_found", PresetSupport.NotFound(name, all));

        var (proceed, message) = await ctx.Authorize(new AuthorizationRequest(WriteKind.PresetDelete, 0, preset.Name), cancellationToken);
        if (!proceed)
            return CommandResult.Ok(new JsonObject { ["outcome"] = "refused", ["note"] = message });

        try { PresetConfigManager.DeletePreset(preset.Name); }
        catch (Exception ex) { return CommandResult.Fail("delete_failed", ex.Message); }

        return CommandResult.Ok(new JsonObject
        {
            ["outcome"] = "deleted",
            ["name"] = preset.Name,
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
            : note + string.Format("Deleted the preset \"{0}\".", obj["name"]!.GetValue<string>());
    }
}

internal sealed class PresetRenameCommand : ICommand
{
    public string Path => "preset rename";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "rename_preset";

    public string Brief => "Rename a saved part preset";

    public string Documentation =>
        "Rename one saved part preset. The name IS the file name, so this renames the user's file — it needs their authorization. "
        + "\nUnlike the Part panel, this refuses when the new name is already taken instead of offering to replace that preset: overwriting is not something to do on a guess, and there is nobody here to ask. Delete the other one first if that is really what you want.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "The preset to rename, exactly as listed by list_presets." },
            "newName": { "type": "string", "description": "The new name (it becomes the file name, so no < > : \" / \\ | ? * and at most 100 characters)." }
          },
          "required": ["name", "newName"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var name = (args.Json.GetString("name") ?? "").Trim();
        var newName = (args.Json.GetString("newName") ?? "").Trim();

        var preset = PresetSupport.Find(name, out var all);
        if (preset == null)
            return CommandResult.Fail("not_found", PresetSupport.NotFound(name, all));

        if (PresetConfigManager.GetPresetNameError(newName) is { } nameError)
            return CommandResult.Fail("invalid_name", nameError);

        // 改成同一个名字（仅大小写不同也算）是无操作，如实说、不假装做过一次。
        if (preset.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
            return CommandResult.Fail("same_name", string.Format("\"{0}\" is already called that. Nothing to do.", preset.Name));

        if (PresetSupport.Find(newName, out _) != null)
            return CommandResult.Fail("name_taken", string.Format(
                "There is already a preset called \"{0}\", and renaming onto it would replace it. Delete that one first, or pick another name. Nothing happened.", newName));

        var (proceed, message) = await ctx.Authorize(
            new AuthorizationRequest(WriteKind.PresetRename, 0, preset.Name, newName), cancellationToken);
        if (!proceed)
            return CommandResult.Ok(new JsonObject { ["outcome"] = "refused", ["note"] = message });

        try { PresetConfigManager.RenamePreset(preset.Name, newName); }
        catch (Exception ex) { return CommandResult.Fail("rename_failed", ex.Message); }

        return CommandResult.Ok(new JsonObject
        {
            ["outcome"] = "renamed",
            ["name"] = preset.Name,
            ["newName"] = newName,
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
            : note + string.Format("Renamed the preset \"{0}\" to \"{1}\".", obj["name"]!.GetValue<string>(), obj["newName"]!.GetValue<string>());
    }
}
