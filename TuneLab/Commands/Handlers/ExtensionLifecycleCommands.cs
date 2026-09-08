using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions;
using TuneLab.Utils;

namespace TuneLab.Commands.Handlers;

// 扩展的**装 / 卸 / 撤销卸载**——覆盖率清单里 `pending:extension-lifecycle` 那两条（扩展侧栏的
// 「安装」与条目上的「取消卸载」）。此前命令面只有 `extension enable`（启停，不动文件）。
//
// 三条命令共用两条事实，都是宿主的硬约束、不是这里的设计选择：
//  · **装可以立刻生效**：解压 + Load 就能把新能力注册进来，音源引擎再急切 Init 一次即可用；
//  · **卸与重装必须等重启**：正在跑的进程锁着那些 dll。故卸载只是**标记**（重启时由外置安装器删掉），
//    而"已装同名包再装一次"这里直接拒绝——界面上那条路会重启整个应用，命令面不该替用户做这个决定。
internal static class ExtensionLifecycle
{
    // 包 id / 目录名 → 已装包的目录。V1 包用 manifest 的 id，legacy 包用目录名（与 list_extensions 一致）。
    public static string? ResolveDirectory(string packageId, out string displayName)
    {
        displayName = packageId;
        foreach (var result in ExtensionManager.LoadResults)
        {
            bool hit = result.Id == packageId
                || string.Equals(Path.GetFileName(result.DirectoryPath), packageId, StringComparison.OrdinalIgnoreCase);
            if (!hit)
                continue;

            displayName = string.IsNullOrEmpty(result.Name) ? packageId : result.Name;
            return result.DirectoryPath;
        }
        return null;
    }

    public static string InstalledList()
    {
        var ids = ExtensionManager.LoadResults
            .Select(r => r.Id ?? Path.GetFileName(r.DirectoryPath))
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();
        return ids.Count == 0 ? "(none)" : string.Join(", ", ids);
    }
}

internal sealed class ExtensionInstallCommand : ICommand
{
    public string Path => "extension install";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "install_extension";

    public string Brief => "Install an extension package (.tlx) into the running TuneLab";

    public string Documentation =>
        "Install a TuneLab extension package (a .tlx file the user already has on disk) — the same thing the Install button in the Extensions sidebar does. "
        + "It unpacks the package into the user's extensions folder and loads it right away, so a newly installed voice/instrument engine is usable without a restart (its engines are initialized as part of this). "
        + "\nIt does NOT download anything: give it a local path. It always needs the user's authorization — this puts third-party code on their machine and runs it. "
        + "\nIf a package with the same folder name is already installed it REFUSES: replacing one means restarting TuneLab (the running process holds its files open), and that is the user's call — point them at the Extensions sidebar, whose install flow offers exactly that restart. "
        + "\nAfter installing, list_extensions shows what loaded and list_sound_sources / list_effects show the new capabilities. If the package loads but reports an error, it stays installed and shows up as failed there — tell the user rather than trying again.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Absolute local path of the .tlx package file." }
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

        string fullPath;
        try { fullPath = System.IO.Path.GetFullPath(given); }
        catch (Exception ex) { return CommandResult.Fail("bad_path", string.Format("\"{0}\" is not a usable file path — {1}", given, ex.Message)); }
        if (!File.Exists(fullPath))
            return CommandResult.Fail("missing_file", string.Format("there is no file at \"{0}\".", fullPath));
        if (!string.Equals(System.IO.Path.GetExtension(fullPath), ".tlx", StringComparison.OrdinalIgnoreCase))
            return CommandResult.Fail("not_a_package", string.Format(
                "\"{0}\" is not a .tlx package. Extensions are distributed as .tlx files; a loose dll or a zip is not one.", fullPath));

        // 包名取自 manifest（与界面同一口径），读不出就退回文件名——目标目录名由它决定。
        var name = PackageName(fullPath, out var manifestError);
        var dir = System.IO.Path.Combine(PathManager.ExtensionsFolder, name);
        if (Directory.Exists(dir))
            return CommandResult.Fail("already_installed", string.Format(
                "\"{0}\" is already installed. Replacing it requires restarting TuneLab (this process is holding its files open), which is the user's call — "
                + "ask them to do it from the Extensions sidebar, or to uninstall it first and restart.", name));

        var (proceed, message) = await ctx.Authorize(new AuthorizationRequest(WriteKind.ExtensionInstall, 0, name, fullPath), cancellationToken);
        if (!proceed)
            return CommandResult.Ok(new JsonObject { ["name"] = name, ["outcome"] = "refused", ["note"] = message });

        // 解压 + 注册 + 急切 Init 全在主线程：Load 会往宿主注册表写，而合成管线在那条线程上跑。
        return CommandResult.Ok(await ctx.OnMainThread(() =>
        {
            try
            {
                ZipFileHelper.ExtractToDirectory(fullPath, dir);
            }
            catch (Exception ex)
            {
                return new JsonObject { ["name"] = name, ["outcome"] = "failed", ["note"] = "could not unpack it: " + ex.Message };
            }

            ExtensionManager.Load(dir);
            // 解压成功 ≠ 加载成功：坏 manifest 之类被 Load 优雅记成 Failed 而不抛，故据加载结果归类（同界面）。
            var result = ExtensionManager.LoadResults.LastOrDefault(r => r.DirectoryPath == dir);
            bool loadFailed = result != null && result.Status == ExtensionLoadStatus.Failed;

            // 新装的音源引擎要能立刻用（同界面装完那一步）；Init 失败与装没装上是两件事，故分开报。
            var initFailures = loadFailed ? [] : SoundSourceEngines.InitAll();

            var node = new JsonObject
            {
                ["name"] = string.IsNullOrEmpty(result?.Name) ? name : result!.Name,
                ["packageId"] = result?.Id,
                ["path"] = dir,
                ["outcome"] = loadFailed ? "installed_not_loading" : "applied",
                ["loadError"] = loadFailed ? result!.Error ?? "load failed" : null,
                ["capabilities"] = result == null ? null : new JsonArray(result.Types.Select(t => (JsonNode)t!).ToArray()),
                ["manifestNote"] = manifestError,
                ["note"] = string.IsNullOrEmpty(message) ? null : message,
            };
            if (initFailures.Count > 0)
                node["engineInitFailures"] = new JsonArray(initFailures.Select(f => (JsonNode)f!).ToArray());
            return node;
        }));
    }

    // manifest.json 的 name（容错：缺失/损坏不阻断安装，与界面一致——解压后由 Load 优雅记录状态）。
    static string PackageName(string tlxPath, out string? note)
    {
        note = null;
        try
        {
            using var archive = ZipFile.OpenRead(tlxPath);
            using var stream = archive.GetEntry("manifest.json")?.Open();
            if (stream == null)
            {
                note = "the package has no manifest.json, so its folder is named after the file";
                return System.IO.Path.GetFileNameWithoutExtension(tlxPath);
            }
            var description = JsonSerializer.Deserialize<PackageDescription>(stream);
            if (string.IsNullOrEmpty(description.name))
            {
                note = "the manifest declares no name, so the folder is named after the file";
                return System.IO.Path.GetFileNameWithoutExtension(tlxPath);
            }
            return description.name;
        }
        catch (Exception ex)
        {
            note = "could not read the manifest (" + ex.Message + "), so the folder is named after the file";
            return System.IO.Path.GetFileNameWithoutExtension(tlxPath);
        }
    }

    struct PackageDescription
    {
        public string name { get; set; }
    }

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var name = obj["name"]!.GetValue<string>();
        var note = obj["note"]?.GetValue<string>() ?? string.Empty;
        switch (obj["outcome"]!.GetValue<string>())
        {
            case "refused":
                return note;
            case "failed":
                return string.Format("Did not install \"{0}\" — {1}. Nothing was added.", name, obj["note"]?.GetValue<string>() ?? "unknown error");
            case "installed_not_loading":
                return string.Format(
                    "Unpacked \"{0}\" into the extensions folder, but it does NOT load: {1}. It stays installed and shows as failed in the Extensions sidebar — "
                    + "tell the user; installing it again will not help.", name, obj["loadError"]?.GetValue<string>() ?? "load failed");
            default:
                var text = note + string.Format("Installed \"{0}\" and loaded it — no restart needed.", name);
                if (obj["capabilities"] is JsonArray caps && caps.Count > 0)
                    text += " Capabilities: " + string.Join(", ", caps.Select(c => c!.GetValue<string>())) + ".";
                if (obj["manifestNote"]?.GetValue<string>() is { Length: > 0 } manifestNote)
                    text += " Note: " + manifestNote + ".";
                if (obj["engineInitFailures"] is JsonArray failures && failures.Count > 0)
                    text += "\nIt is installed, but these engines failed to start up: " + string.Join("; ", failures.Select(f => f!.GetValue<string>()));
                return text;
        }
    }
}

// 卸载 = **标记**，重启时才真删（运行中的进程锁着那些 dll）。故这条命令的回报必须把"要重启"说在明面上，
// 否则调用方会以为文件已经没了，转头去装同名包又撞上 already_installed。
internal sealed class ExtensionUninstallCommand : ICommand
{
    public string Path => "extension uninstall";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "uninstall_extension";

    public string Brief => "Mark an extension for uninstall (removed on restart)";

    public string Documentation =>
        "Uninstall an installed extension — the same thing the Uninstall button in the Extensions sidebar does. "
        + "\nIT DOES NOT DELETE ANYTHING RIGHT AWAY: the running process holds the package's files open, so this only MARKS it, and the files are removed when TuneLab next restarts. "
        + "Always tell the user that: until they restart, the extension keeps working. Undo the mark with cancel_extension_uninstall. "
        + "\nAnything referring to that package stops resolving after the restart (a project using its voice, a file of its format) — say so before doing it. "
        + "If the user only wants it out of the way, set_extension_enabled turns it off (or just one capability) while keeping it installed. Needs the user's authorization.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "packageId": { "type": "string", "description": "The installed package's id exactly as listed by list_extensions (legacy packages use their folder name)." }
          },
          "required": ["packageId"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
        => await ExtensionUninstallMarking.RunAsync(args, ctx, uninstall: true, cancellationToken);

    public string Render(JsonNode? data, CommandArgs args) => ExtensionUninstallMarking.Render(data, uninstall: true);
}

internal sealed class ExtensionCancelUninstallCommand : ICommand
{
    public string Path => "extension cancel-uninstall";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "cancel_extension_uninstall";

    public string Brief => "Take back a pending uninstall so the extension stays";

    public string Documentation =>
        "Take back a pending uninstall (the mark uninstall_extension leaves behind), so the extension stays installed after the restart. "
        + "Same as the \"Cancel Uninstall\" item on a package that is waiting to be removed in the Extensions sidebar. "
        + "\nIt is only meaningful before the restart that would carry the uninstall out; if that package is not marked, this says so and changes nothing.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "packageId": { "type": "string", "description": "The package's id exactly as listed by list_extensions (legacy packages use their folder name)." }
          },
          "required": ["packageId"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
        => await ExtensionUninstallMarking.RunAsync(args, ctx, uninstall: false, cancellationToken);

    public string Render(JsonNode? data, CommandArgs args) => ExtensionUninstallMarking.Render(data, uninstall: false);
}

// 「标记卸载」与「撤销标记」是同一件事的两个方向，故解析、判据与回报形状只写一份（同 set_extension_enabled
// 用一个 kind + enable/disable 两种措辞）。
internal static class ExtensionUninstallMarking
{
    public static async Task<CommandResult> RunAsync(CommandArgs args, CommandContext ctx, bool uninstall, CancellationToken cancellationToken)
    {
        var packageId = (args.Json.GetString("packageId") ?? "").Trim();
        if (packageId.Length == 0)
            return CommandResult.Fail("empty_id", "\"packageId\" is required. Call list_extensions to see the installed packages.");

        var plan = await ctx.OnMainThread(() =>
        {
            var dir = ExtensionLifecycle.ResolveDirectory(packageId, out var displayName);
            return (Directory: dir, DisplayName: displayName,
                    Pending: dir != null && ExtensionManager.PendingUninstalls.Contains(dir));
        });

        if (plan.Directory is not { } directory)
            return CommandResult.Fail("unknown_package", string.Format(
                "no installed package with id \"{0}\". Installed: {1}.", packageId, ExtensionLifecycle.InstalledList()));

        // 已经是目标状态就什么也不做（幂等）：标了再标、没标就撤，都不该问用户。
        if (plan.Pending == uninstall)
            return CommandResult.Ok(new JsonObject
            {
                ["packageId"] = packageId,
                ["name"] = plan.DisplayName,
                ["outcome"] = "unchanged",
                ["pending"] = plan.Pending,
            });

        var (proceed, message) = await ctx.Authorize(
            new AuthorizationRequest(WriteKind.ExtensionUninstall, 0, plan.DisplayName, uninstall ? "uninstall" : "keep"),
            cancellationToken);
        if (!proceed)
            return CommandResult.Ok(new JsonObject
            {
                ["packageId"] = packageId,
                ["name"] = plan.DisplayName,
                ["outcome"] = "refused",
                ["pending"] = plan.Pending,
                ["note"] = message,
            });

        await ctx.OnMainThread(() =>
        {
            if (uninstall)
                ExtensionManager.AddPendingUninstall(directory);
            else
                ExtensionManager.RemovePendingUninstall(directory);
            return 0;
        });

        return CommandResult.Ok(new JsonObject
        {
            ["packageId"] = packageId,
            ["name"] = plan.DisplayName,
            ["outcome"] = "applied",
            ["pending"] = uninstall,
            ["note"] = string.IsNullOrEmpty(message) ? null : message,
        });
    }

    public static string Render(JsonNode? data, bool uninstall)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var name = obj["name"]!.GetValue<string>();
        switch (obj["outcome"]!.GetValue<string>())
        {
            case "refused":
                return obj["note"]?.GetValue<string>() ?? string.Empty;
            case "unchanged":
                return uninstall
                    ? string.Format("\"{0}\" is already marked for uninstall; nothing to do. It goes away when TuneLab restarts.", name)
                    : string.Format("\"{0}\" is not marked for uninstall, so there was nothing to take back.", name);
            default:
                var note = obj["note"]?.GetValue<string>() ?? string.Empty;
                return note + (uninstall
                    ? string.Format(
                        "Marked \"{0}\" for uninstall. IT IS STILL INSTALLED AND STILL WORKING until TuneLab restarts — the files are removed then, because this process is holding them open. "
                        + "Tell the user to restart when they are ready; cancel_extension_uninstall takes the mark back.", name)
                    : string.Format("\"{0}\" will stay installed — the pending uninstall is off.", name));
        }
    }
}
