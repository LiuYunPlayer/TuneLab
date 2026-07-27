using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Data;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Formats.TLP;

namespace TuneLab.Agent;

// 把当前工程【导出成一个文件】——`project.importTracks(path)` 的对偶（那个读入文件，这个写出文件）。
//
// 为什么是工具面而不是 tl 脚本原语（与"工程编辑恒走 run_script"的护栏不冲突）：
//  · 护栏约束的是【工程状态的修改】，而导出不改工程状态一分一毫，与 save_script/delete_script（同样写外部文件、
//    同样不碰工程数据）同类——那两件本来就在工具面，故这是循例而非例外。
//  · 授权闸门是 async（要等用户点确认卡片），而脚本经 Jint 同步跑在 UI 线程，中途阻塞等卡片会自死锁。工具天然容得下。
//  将来若用户真需要在脚本里导出（如"每轨各存一个 midi"），再加 tl 原语并配"脚本内登记意图 → 脚本成功结束后统一
//  过闸门执行、脚本出错则一并丢弃（文件从未写）"的延迟写机制，与本工具加性并存。
//
// 【只做工程/MIDI 等格式文件，不做音频导出】：音频导出要跑完整合成+混音+编码，期间界面必须锁住（根因是渲染要求
// 数据全程不变，不是 UI 偷懒）——那与"agent 边导出边继续干活"根本矛盾，且"要不要现在把机器占住几分钟"是用户的
// 人在环决定，同播放/试听的裁定。故音频导出的正解是 agent 备好参数、最后一下由用户按，不在本工具里。
internal sealed class ExportProjectTool(IProject project, Func<AgentAuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm = null) : IAgentTool
{
    public string Name => "export_project";

    public string Description =>
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

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string? path;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            path = doc.RootElement.GetString("path");
        }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        path = (path ?? "").Trim().Trim('"');
        if (string.IsNullOrEmpty(path)) return "Error: \"path\" is required.";

        // 路径合法性先行（不为坏请求打扰用户 —— 同 save_script 把预校验放在授权之前）。
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) { return string.Format("Error: \"{0}\" is not a usable file path — {1}", path, ex.Message); }
        if (Directory.Exists(fullPath))
            return string.Format("Error: \"{0}\" is a folder, not a file path. Give the full path including the file name and extension.", fullPath);

        var format = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(format))
            return string.Format("Error: \"{0}\" has no file extension, so there's no format to export as. Supported: {1}.", fullPath, SupportedList());
        if (!FormatsManager.GetAllExportFormats().Contains(format))
            return string.Format("Error: cannot export to \".{0}\" — no installed format provides it. Supported: {1}.", format, SupportedList());

        var folder = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return string.Format("Error: the folder \"{0}\" does not exist. Create it first or pick an existing folder (this tool won't create folders).", folder);

        // 序列化【先于】授权：FormatsManager 缓冲进 MemoryStream（原子写语义——失败时目标文件尚未开写），
        // 故序列化失败可以直接报错、不必先弹一次卡片再让用户白确认一场。
        var file = new NativeProjectFile
        {
            Project = project.GetInfo(),
            Editor = new EditorInfo { PlayheadPos = PlayheadTick() },
            Export = project.GetExportConfig(),
        };
        // native(.tlp/.tlpx) 走 SerializeNative 带上 editor/export 元数据保真；foreign(.mid 等) 由它内部
        // 自动降级到纯 musical Serialize——故两类统一走这一条，与「另存为」同一路径。
        if (!FormatsManager.SerializeNative(file, format, out var stream, out var error))
            return string.Format("Error: failed to serialize the project as \".{0}\" — {1}. Nothing was written.", format, error);

        bool overwrite = File.Exists(fullPath);
        var displayName = FormatsManager.GetDisplayName(format);
        // 恒过闸门：导出路径是任意的（能写到用户磁盘任何地方），且历史记录管理器只保工程数据、救不回外部文件。
        var (proceed, message) = await ToolAuthorization.AuthorizeAsync(
            new AgentAuthorizationRequest(
                overwrite ? AgentWriteKind.ProjectExportOverwrite : AgentWriteKind.ProjectExport,
                0, fullPath, displayName),
            confirm, cancellationToken);
        if (!proceed)
        {
            stream.Dispose();
            return message;
        }

        long bytes;
        try
        {
            using (stream)
            // 用户只同意了卡片上说的那件事：说"新建"就【只能】新建。闸门等待期用户可能自己在该路径放了文件，
            // 那时 Create 会静默替换一个我们从未取得替换许可的文件 → 故非覆盖档用 CreateNew，让它抛错而不是越权
            // （同 set_keybinding「冲突在落地那刻重查、未获许可就什么都不做」）。
            using (var fileStream = new FileStream(fullPath, overwrite ? FileMode.Create : FileMode.CreateNew))
            {
                stream.CopyTo(fileStream);
                bytes = fileStream.Length;
            }
        }
        catch (IOException) when (!overwrite && File.Exists(fullPath))
        {
            return string.Format(
                "A file appeared at \"{0}\" while waiting for authorization, and the user only approved writing a NEW file there, not replacing one. " +
                "Nothing was written. Ask again if they want to replace it.", fullPath);
        }
        catch (Exception ex) { return string.Format("Error: failed to write \"{0}\" — {1}", fullPath, ex.Message); }

        return message + string.Format(
            "Exported the project as {0} to \"{1}\" ({2}){3}. Note this was a copy: the user's project is still open with the same save file and unsaved changes as before.",
            displayName, fullPath, FormatSize(bytes), overwrite ? ", replacing the file that was there" : "");
    }

    // 播放头位置随 native 元数据一并写出（重开时落在同一处）。用 AudioEngine 现刻时间换算，与脚本面 tl.playhead() 同源，
    // 免得为一个字段依赖 Editor。
    double PlayheadTick()
    {
        try { return project.TempoManager.GetTick(AudioEngine.CurrentTime); }
        catch { return 0; }
    }

    static string SupportedList()
    {
        var formats = FormatsManager.GetAllExportFormats();
        return formats.Count == 0 ? "(none)" : string.Join(", ", formats.Select(f => "." + f));
    }

    static string FormatSize(long bytes)
        => bytes >= 1024 * 1024 ? string.Format("{0:0.0} MB", bytes / 1024.0 / 1024.0)
         : bytes >= 1024 ? string.Format("{0:0.0} KB", bytes / 1024.0)
         : bytes + " bytes";
}
