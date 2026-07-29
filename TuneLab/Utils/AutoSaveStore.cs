using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TuneLab.Foundation;

namespace TuneLab.Utils;

// 一次自动保存的元数据，落在与工程文件同名的 .json sidecar 里。
//
// 【文件名只给人看，宿主永不解析它】——展示名、产生时刻、原工程路径一律从这里读。此前这些语义被编码在
// 文件名与文件系统属性上，两者都不是可信来源：按固定长度切时间戳前缀取工程名，格式一变就错位、名字短于
// 前缀还会越界；按 FileInfo.CreationTime 排序轮换，那是文件系统属性，复制 / 同步 / 跨卷搬运都会改写它，
// 与"这份自动保存是什么时候产生的"并非同一件事。文件名仍按「时间_工程名」生成，纯粹为了用户在文件管理器
// 里翻 History 时能一眼分辨——它是装饰，不是数据。
//
// Schema 演进走加法式、零迁移：旧文件无新字段即取默认值。
internal sealed class AutoSaveMeta
{
    public int SchemaVersion { get; set; } = 1;
    // 配对自证：本 sidecar 对应的工程文件名（仅文件名，不含目录）。读时必须与实际文件名相符才采信本元数据，
    // 让配对从"文件名巧合"变成内容自证。
    public string AutoSaveFile { get; set; } = string.Empty;
    // 产生时刻：排序（恢复取最新、History 轮换删最旧）与展示的唯一来源。
    public long SavedAtUnixMs { get; set; }
    // 展示名（恢复时显示用），不再从文件名切。
    public string ProjectName { get; set; } = string.Empty;
    // 自动保存那一刻工程的保存路径；工程从未保存过则为 null。
    // 它让恢复能在【保持未保存态】的同时拿到基准目录（相对音频引用得以解析），并让「存回原位」有落脚点。
    public string? OriginalPath { get; set; }
}

// 找到的一份自动保存。Meta == null 表示降级：sidecar 缺失或配对校验不通过，
// 此时当作没有元数据处理（不解析相对引用、不提供存回原位、展示名退回文件名）。
internal sealed class AutoSaveRecord(string filePath, AutoSaveMeta? meta)
{
    public string FilePath { get; } = filePath;
    public AutoSaveMeta? Meta { get; } = meta;
}

// 自动保存的落盘容器：根目录放【当前哨兵】（存在即上次异常退出），History 子目录放多版本历史。
// 沿用既有判据（哨兵即自动保存文件本身），只是把元数据从文件名移进 sidecar。
//
// root 由调用方给出，便于将来把落点换成别处而无需改动本类。
internal sealed class AutoSaveStore(string root)
{
    // 省略 null 字段：OriginalPath 在"从未保存过"时无值，不写 null 让文件干净些；
    // 反序列化把缺失字段当默认值，向后兼容不受影响。
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Root => root;
    public string HistoryFolder => Path.Combine(root, "History");

    // 写一次自动保存。三条协议缺一不可，顺序与实现细节见各处注释。
    // writeProject：把序列化好的工程数据写进给定流（由调用方决定格式）。
    public void Write(Action<Stream> writeProject, string projectName, string? originalPath, int historyMaxCount)
    {
        PathManager.MakeSureExist(root);

        var meta = new AutoSaveMeta
        {
            SavedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ProjectName = projectName,
            OriginalPath = string.IsNullOrEmpty(originalPath) ? null : originalPath,
        };

        var baseName = NextBaseName(projectName);
        var projectPath = ProjectPath(root, baseName);
        var sidecarPath = SidecarPath(root, baseName);
        meta.AutoSaveFile = Path.GetFileName(projectPath);

        // 【协议二】先写 sidecar，再写工程文件。这个顺序保证「任何存在的工程文件都必然已经有它的 sidecar」：
        // 崩在两次写之间只会留下一个指向不存在文件的孤儿 sidecar（读时因找不到工程文件而忽略，无害）；
        // 反过来若先写工程文件，则会留下无元数据的工程文件，恢复只能降级。
        SaveFile.WriteAllText(sidecarPath, JsonSerializer.Serialize(meta, Options));
        SaveFile.Write(projectPath, writeProject);

        // 清除上一对（按"对"清除，而非按单个文件名判断）。崩在这一步之前会留下"新旧两对完整文件"，
        // 恢复按 SavedAtUnixMs 取最新那对即正确。
        foreach (var file in EnumerateFiles(root))
        {
            if (file == projectPath || file == sidecarPath)
                continue;
            TryDelete(file);
        }

        CopyToHistory(baseName, historyMaxCount);
    }

    // 取当前哨兵：根下最新的一份自动保存。没有则 null（= 上次正常退出）。
    public AutoSaveRecord? FindLatest() => Enumerate(root).FirstOrDefault();

    // 清除哨兵（正常退出 / 保存后调用）。只动根下的文件，不动 History。
    // 顺带清掉孤儿 sidecar（协议二在两次写之间被打断时留下的那种）。
    public void ClearSentinel()
    {
        if (!Directory.Exists(root))
            return;

        foreach (var file in EnumerateFiles(root))
            TryDelete(file);
    }

    // ── 内部实现 ──

    // 【协议一】文件名必须唯一，绝不覆盖同名。撞名会造成就地覆盖：sidecar 已换成新工程的内容、工程文件
    // 还是旧工程的内容，此时崩溃就产生一对"配得上却不同源"的文件——那是唯一能绕过协议二/三的失效模式
    // （AutoSaveFile 自证也识别不出来，因为两边写的是同一个名字）。
    // 时间戳取到毫秒，再撞则加序号。注意此处唯一性只为"互不踩踏"，顺序不由文件名承载（见 SavedAtUnixMs）。
    string NextBaseName(string projectName)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        // 展示名可能本身就是个文件名（"MySong.tlpx"）：文件名这一层去掉扩展名免得出现 "MySong.tlpx.tlpx"。
        // sidecar 里存的仍是原样展示名，两者互不影响——文件名只是装饰。
        var name = Sanitize(Path.GetFileNameWithoutExtension(projectName));
        var baseName = string.IsNullOrEmpty(name) ? stamp : stamp + "_" + name;
        var candidate = baseName;
        for (int n = 2; File.Exists(ProjectPath(root, candidate)) || File.Exists(SidecarPath(root, candidate)); n++)
            candidate = baseName + "_" + n;

        return candidate;
    }

    // 文件名是装饰品，去掉平台非法字符即可，不追求可逆。
    static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Where(c => !invalid.Contains(c)).ToArray();
        return new string(chars).Trim();
    }

    static string ProjectPath(string folder, string baseName)
        => Path.Combine(folder, baseName + "." + ConstantDefine.DefaultProjectExtension);

    static string SidecarPath(string folder, string baseName) => Path.Combine(folder, baseName + ".json");

    // 一份自动保存的工程文件候选。除当前的 tlpx 外也认 tlp——旧构建留下的哨兵仍应能被恢复。
    static bool IsProjectFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension == "." + ConstantDefine.DefaultProjectExtension || extension == ".tlp";
    }

    static IEnumerable<string> EnumerateFiles(string folder)
        => Directory.Exists(folder) ? Directory.EnumerateFiles(folder) : [];

    // 枚举一个目录里的自动保存，按 SavedAtUnixMs 倒序（新→旧）。
    // 【降级】无有效元数据的条目用文件最后写入时间兜底参与排序——只作排序兜底，不当语义来源。
    static List<AutoSaveRecord> Enumerate(string folder)
    {
        var records = new List<(AutoSaveRecord Record, long SortKey)>();
        foreach (var path in EnumerateFiles(folder))
        {
            if (!IsProjectFile(path))
                continue;

            var meta = ReadMeta(path);
            records.Add((new AutoSaveRecord(path, meta), meta?.SavedAtUnixMs ?? FallbackSortKey(path)));
        }

        return records.OrderByDescending(item => item.SortKey).Select(item => item.Record).ToList();
    }

    static long FallbackSortKey(string path)
    {
        try { return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToUnixTimeMilliseconds(); }
        catch { return 0; }
    }

    // 【协议三】只有当同名 sidecar 存在、且其 AutoSaveFile 等于该工程文件的实际文件名时才采信元数据；
    // 否则返回 null 走降级。读坏同样当作无元数据。
    static AutoSaveMeta? ReadMeta(string projectPath)
    {
        var sidecarPath = Path.ChangeExtension(projectPath, ".json");
        if (!File.Exists(sidecarPath))
            return null;

        AutoSaveMeta? meta;
        try
        {
            meta = JsonSerializer.Deserialize<AutoSaveMeta>(File.ReadAllText(sidecarPath));
        }
        catch (Exception ex)
        {
            Log.Warning("Skip corrupt auto-save metadata " + sidecarPath + ": " + ex.Message);
            return null;
        }

        if (meta == null)
            return null;

        if (meta.AutoSaveFile != Path.GetFileName(projectPath))
        {
            Log.Warning("Auto-save metadata " + sidecarPath + " does not match its project file; ignoring it.");
            return null;
        }

        return meta;
    }

    // 整对复制进 History，再按数量轮换（删最旧的整对）。History 只供人工翻查，失败仅记日志。
    void CopyToHistory(string baseName, int maxCount)
    {
        try
        {
            PathManager.MakeSureExist(HistoryFolder);
            File.Copy(SidecarPath(root, baseName), SidecarPath(HistoryFolder, baseName), true);
            File.Copy(ProjectPath(root, baseName), ProjectPath(HistoryFolder, baseName), true);

            foreach (var record in Enumerate(HistoryFolder).Skip(Math.Max(1, maxCount)))
            {
                TryDelete(record.FilePath);
                TryDelete(Path.ChangeExtension(record.FilePath, ".json"));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to manage auto-save history: " + ex);
        }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { Log.Warning("Failed to delete auto-save file " + path + ": " + ex.Message); }
    }
}
