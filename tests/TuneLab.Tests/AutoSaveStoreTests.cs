using System.Text;
using System.Text.Json;
using TuneLab.Utils;
using Xunit;

namespace TuneLab.Tests;

// 自动保存容器的协议回归：元数据只从 sidecar 读、文件名与文件系统时间一律不可信。
//
// 这里锚定的核心事实是「配得上却不同源」这种错配不可能发生——它会让恢复出来的工程带上另一个工程的原路径，
// 而「存回原位」据此覆盖文件，属数据丢失级后果。三条协议（唯一命名 / 先写 sidecar / 读时自证）各有一组用例。
public class AutoSaveStoreTests : IDisposable
{
    readonly string mRoot;

    public AutoSaveStoreTests()
    {
        mRoot = Path.Combine(Path.GetTempPath(), "TuneLab.AutoSaveStoreTests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(mRoot, true); } catch { }
    }

    AutoSaveStore NewStore() => new(mRoot);

    static void WriteProject(AutoSaveStore store, string projectName, string? originalPath, int historyMaxCount = 5, string body = "project")
        => store.Write(stream => stream.Write(Encoding.UTF8.GetBytes(body)), projectName, originalPath, historyMaxCount);

    string[] ProjectFiles(string folder)
        => Directory.Exists(folder)
            ? Directory.GetFiles(folder).Where(f => Path.GetExtension(f) == ".tlpx").OrderBy(f => f).ToArray()
            : [];

    // ── 往返 ──

    [Fact]
    public void WriteThenFindLatest_RoundTripsMetadata()
    {
        var store = NewStore();
        WriteProject(store, "MySong.tlpx", @"D:\songs\MySong.tlpx");

        var record = store.FindLatest();
        Assert.NotNull(record);
        Assert.NotNull(record!.Meta);
        Assert.Equal("MySong.tlpx", record.Meta!.ProjectName);
        Assert.Equal(@"D:\songs\MySong.tlpx", record.Meta.OriginalPath);
        Assert.Equal(Path.GetFileName(record.FilePath), record.Meta.AutoSaveFile);
        Assert.True(record.Meta.SavedAtUnixMs > 0);
        Assert.Equal("project", File.ReadAllText(record.FilePath));
    }

    // 工程从未保存过 → OriginalPath 为 null（不是空串），据此走降级、不猜路径。
    // 展示名也留空：元数据里【绝不存本地化文本】（"未命名工程"随界面语言而变），恢复侧按当前语言渲染。
    // 于是文件名里只剩时间戳，也不会把非 ASCII 文本带进文件名。
    [Fact]
    public void NeverSavedProject_HasNoOriginalPathAndNoLocalizedName()
    {
        var store = NewStore();
        WriteProject(store, projectName: string.Empty, originalPath: null);

        var record = store.FindLatest()!;
        Assert.Null(record.Meta!.OriginalPath);
        Assert.Equal(string.Empty, record.Meta.ProjectName);
        // 文件名 = 纯时间戳（无名字部分、无尾随分隔符）
        var name = Path.GetFileNameWithoutExtension(record.FilePath);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}-\d{3}$", name);
    }

    // 展示名可能带扩展名；文件名那一层要去掉它（免得出现 MySong.tlpx.tlpx），但 sidecar 里存的是原样展示名。
    [Fact]
    public void FileNameStripsExtension_ButMetadataKeepsDisplayNameVerbatim()
    {
        var store = NewStore();
        WriteProject(store, "MySong.tlpx", null);

        var record = store.FindLatest()!;
        Assert.Equal("MySong.tlpx", record.Meta!.ProjectName);
        Assert.DoesNotContain(".tlpx.tlpx", Path.GetFileName(record.FilePath));
    }

    // ── 协议一：唯一命名，绝不覆盖同名 ──

    // 连续两次自动保存（同一秒内）必须产出两对独立文件。若撞名就地覆盖，就可能留下
    // 「sidecar 是新工程、工程文件是旧工程」的一对。
    [Fact]
    public void RapidConsecutiveWrites_NeverOverwriteEachOther()
    {
        var store = NewStore();
        WriteProject(store, "A", @"D:\a.tlpx", body: "A-body");
        WriteProject(store, "A", @"D:\a.tlpx", body: "B-body");

        // 根下只留最新一对（旧的被按对清除），但 History 应保有两份独立文件。
        Assert.Single(ProjectFiles(mRoot));
        Assert.Equal(2, ProjectFiles(store.HistoryFolder).Length);
        Assert.Equal("B-body", File.ReadAllText(store.FindLatest()!.FilePath));
    }

    // ── 协议二：先写 sidecar，再写工程文件 ──

    // 崩在两次写之间 → 只留孤儿 sidecar。它必须被忽略，且恢复要落回上一对完整文件（而不是报错或取到半截）。
    [Fact]
    public void OrphanSidecar_IsIgnored_AndFallsBackToPreviousPair()
    {
        var store = NewStore();
        WriteProject(store, "Good", @"D:\good.tlpx", body: "good");

        // 模拟"sidecar 已写、工程文件还没写"就断电
        var orphan = Path.Combine(mRoot, "9999-01-01_00-00-00-000_Orphan.json");
        File.WriteAllText(orphan, JsonSerializer.Serialize(new AutoSaveMeta
        {
            AutoSaveFile = "9999-01-01_00-00-00-000_Orphan.tlpx",
            SavedAtUnixMs = long.MaxValue / 2,   // 时刻远晚于那一对，若被采信就会顶掉它
            ProjectName = "Orphan",
            OriginalPath = @"D:\orphan.tlpx",
        }));

        var record = store.FindLatest();
        Assert.NotNull(record);
        Assert.Equal("good", File.ReadAllText(record!.FilePath));
        Assert.Equal("Good", record.Meta!.ProjectName);
    }

    // ── 协议三：读时校验配对，配不上就降级 ──

    [Fact]
    public void MissingSidecar_DegradesToNoMetadata()
    {
        var store = NewStore();
        WriteProject(store, "MySong.tlpx", @"D:\songs\MySong.tlpx");
        File.Delete(Path.ChangeExtension(store.FindLatest()!.FilePath, ".json"));

        var record = store.FindLatest();
        Assert.NotNull(record);
        Assert.Null(record!.Meta);   // 降级：不解析相对引用、不提供存回原位
    }

    // 这一条是防错配的正面用例：sidecar 指向别的工程文件时，绝不能把它的 OriginalPath 用在当前这份上。
    [Fact]
    public void SidecarPointingAtAnotherFile_IsRejected()
    {
        var store = NewStore();
        WriteProject(store, "MySong.tlpx", @"D:\songs\MySong.tlpx");

        var sidecarPath = Path.ChangeExtension(store.FindLatest()!.FilePath, ".json");
        var meta = JsonSerializer.Deserialize<AutoSaveMeta>(File.ReadAllText(sidecarPath))!;
        meta.AutoSaveFile = "someone-elses-project.tlpx";
        File.WriteAllText(sidecarPath, JsonSerializer.Serialize(meta));

        Assert.Null(store.FindLatest()!.Meta);
    }

    [Fact]
    public void CorruptSidecar_DegradesInsteadOfThrowing()
    {
        var store = NewStore();
        WriteProject(store, "MySong.tlpx", @"D:\songs\MySong.tlpx");
        File.WriteAllText(Path.ChangeExtension(store.FindLatest()!.FilePath, ".json"), "{ not json");

        Assert.Null(store.FindLatest()!.Meta);
    }

    // ── 排序：只认 SavedAtUnixMs，不认文件名、不认文件系统时间 ──

    // 手工摆一对"文件名排序靠后、但产生时刻更早"的文件：若实现去解析文件名或看 mtime，就会取错。
    [Fact]
    public void FindLatest_OrdersBySavedAtUnixMs_NotByFileNameOrFileTime()
    {
        var store = NewStore();
        PlacePair("2000-01-01_00-00-00-000_Older.tlpx", savedAtUnixMs: 9_000, body: "newest-content");
        PlacePair("2099-12-31_23-59-59-999_Newer.tlpx", savedAtUnixMs: 1_000, body: "oldest-content");

        // 让"文件名靠后"的那个同时拥有更新的 mtime，把两种错误来源一起排除掉。
        File.SetLastWriteTimeUtc(Path.Combine(mRoot, "2099-12-31_23-59-59-999_Newer.tlpx"), DateTime.UtcNow);

        var record = store.FindLatest();
        Assert.Equal("newest-content", File.ReadAllText(record!.FilePath));
        Assert.Equal(9_000, record.Meta!.SavedAtUnixMs);
    }

    // ── 清除哨兵 ──

    [Fact]
    public void ClearSentinel_ClearsRootPairOnly_AndKeepsHistory()
    {
        var store = NewStore();
        WriteProject(store, "MySong.tlpx", @"D:\songs\MySong.tlpx");

        store.ClearSentinel();

        Assert.Empty(Directory.GetFiles(mRoot));                  // 连孤儿 sidecar 一并清掉
        Assert.Null(store.FindLatest());                          // 无哨兵 = 上次正常退出
        Assert.Single(ProjectFiles(store.HistoryFolder));         // History 不受影响
    }

    // ── History 轮换：按数量整对淘汰 ──

    [Fact]
    public void History_RotatesInPairs_ByCount()
    {
        var store = NewStore();
        for (int i = 0; i < 5; i++)
            WriteProject(store, "MySong.tlpx", @"D:\songs\MySong.tlpx", historyMaxCount: 3, body: "body-" + i);

        var projects = ProjectFiles(store.HistoryFolder);
        Assert.Equal(3, projects.Length);
        // 整对淘汰：留下的每个工程文件都还配着自己的 sidecar，不留半对。
        foreach (var path in projects)
            Assert.True(File.Exists(Path.ChangeExtension(path, ".json")));
        // 留下的是最新三份。
        Assert.Contains("body-4", projects.Select(File.ReadAllText));
        Assert.DoesNotContain("body-0", projects.Select(File.ReadAllText));
    }

    void PlacePair(string projectFileName, long savedAtUnixMs, string body)
    {
        File.WriteAllText(Path.Combine(mRoot, projectFileName), body);
        File.WriteAllText(Path.Combine(mRoot, Path.ChangeExtension(projectFileName, ".json")),
            JsonSerializer.Serialize(new AutoSaveMeta
            {
                AutoSaveFile = projectFileName,
                SavedAtUnixMs = savedAtUnixMs,
                ProjectName = Path.GetFileNameWithoutExtension(projectFileName),
                OriginalPath = null,
            }));
    }
}
