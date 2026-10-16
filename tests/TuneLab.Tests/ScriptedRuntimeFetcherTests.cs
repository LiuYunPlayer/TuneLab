using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions;
using TuneLab.Extensions.Scripted;
using TuneLab.Utils;
using Xunit;

namespace TuneLab.Tests;

// 运行时自动部署（设计 §8.6）：下载→校验→解包→落成 type:"runtime" 资源包→③ 级探测命中。
// 全链喂预制 zip + 注入 downloader/extensionsRoot/register/probe，不碰真网络与真 %APPDATA%。
public class ScriptedRuntimeFetcherTests
{
    readonly string mRoot = Path.Combine(Path.GetTempPath(), "tl-fetch-" + Path.GetRandomFileName());

    // 格子 rid 跟测试进程实际平台走（runtimes 包 platforms 字段要与 IsPlatformAvailable 对得上）。
    static string RidNow => PlatformHelper.GetPlatform();

    static RuntimeFeedCell Cell(string layout, string sha256 = "", params string[] urls)
        => new("python", new Version(3, 12, 7), RidNow,
            urls.Length > 0 ? urls : ["https://example.invalid/python-3.12.7-embed-amd64.zip"], sha256, layout);

    static Task<string> FirstProbe(IReadOnlyList<string> urls, CancellationToken _) => Task.FromResult(urls[0]);

    // 造一个 python-embed 形（根即 python.exe）的 zip。
    string MakeEmbedZip()
    {
        var zip = Path.Combine(mRoot, "embed.zip");
        Directory.CreateDirectory(mRoot);
        using (var fs = File.Create(zip))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "python.exe", "python313.dll", "python313.zip", "Lib\\site-packages" })
            {
                var entry = archive.CreateEntry(name);
                using var w = entry.Open();
                w.Write(Encoding.UTF8.GetBytes("payload:" + name));
            }
        }
        return zip;
    }

    string MakeNodeZip()
    {
        var zip = Path.Combine(mRoot, "node.zip");
        Directory.CreateDirectory(mRoot);
        using (var fs = File.Create(zip))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "node-v20.17.0-win-x64/node.exe", "node-v20.17.0-win-x64/README.md", "node-v20.17.0-win-x64/node_modules/npm/bin" })
            {
                var entry = archive.CreateEntry(name);
                using var w = entry.Open();
                w.Write(Encoding.UTF8.GetBytes("payload:" + name));
            }
        }
        return zip;
    }

    static ScriptedRuntimeFetcher.DownloadAsyncDelegate CopyDownloader(string source)
        => (url, dest, _, _) => { File.Copy(source, dest); return Task.CompletedTask; };

    [Fact]
    public async Task EmbedZipBecomesRuntimePackageThatLocatorResolves()
    {
        var zip = MakeEmbedZip();
        var exts = Path.Combine(mRoot, "exts");
        var registered = new List<string>();
        var result = await ScriptedRuntimeFetcher.InstallAsync(
            Cell("root"), null, CancellationToken.None, CopyDownloader(zip), exts, registered.Add, FirstProbe, null);
        Assert.True(result.Ok, result.Message);

        var dir = Path.Combine(exts, $"runtime-python-3.12.7-{RidNow}");
        Assert.Equal(dir, registered.Single());
        var manifest = File.ReadAllText(Path.Combine(dir, "manifest.json"));
        Assert.Contains("\"type\": \"runtime\"", manifest);
        Assert.Contains("\"flavor\": \"python\"", manifest);
        Assert.True(File.Exists(Path.Combine(dir, "runtime", "python.exe")));
        Assert.True(File.Exists(Path.Combine(dir, "runtime", "python313.dll")));

        // ③ 级命中：假扩展根 + 假 prober（键=落地的 exe 路径）。
        var prober = new FakeProber();
        prober.Add(Path.Combine(dir, "runtime", "python.exe"), "python", "3.12.7");
        var locator = new RuntimeLocator(prober, () => "", () => "", () => "", _ => null, () => exts);
        Assert.True(RuntimeSpec.TryParse("python>=3.10", out var spec, out _));
        Assert.True(locator.TryResolve(spec, out var info, out var trail), trail);
        Assert.Equal("3.12.7", info.Version);
    }

    [Fact]
    public async Task PrefixLayoutFlattensSingleTopDir()
    {
        var zip = MakeNodeZip();
        var exts = Path.Combine(mRoot, "exts2");
        var cell = new RuntimeFeedCell("node", new Version(20, 17, 0), RidNow, ["https://example.invalid/node.zip"], "", "prefix");
        var result = await ScriptedRuntimeFetcher.InstallAsync(cell, null, CancellationToken.None, CopyDownloader(zip), exts, _ => { }, FirstProbe, null);
        Assert.True(result.Ok, result.Message);
        Assert.True(File.Exists(Path.Combine(exts, $"runtime-node-20.17.0-{RidNow}", "runtime", "node.exe")));
    }

    [Fact]
    public async Task PinnedChecksumMismatchRefusesAndKeepsSystemClean()
    {
        var zip = MakeEmbedZip();
        var exts = Path.Combine(mRoot, "exts3");
        var result = await ScriptedRuntimeFetcher.InstallAsync(
            Cell("root", sha256: "DEADBEEF"), null, CancellationToken.None, CopyDownloader(zip), exts, _ => { }, FirstProbe, null);
        Assert.False(result.Ok);
        Assert.Contains("checksum", result.Message);
        Assert.False(Directory.Exists(Path.Combine(exts, $"runtime-python-3.12.7-{RidNow}")));
    }

    [Fact]
    public async Task PinnedChecksumMatchInstalls()
    {
        var zip = MakeEmbedZip();
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
        var exts = Path.Combine(mRoot, "exts4");
        var result = await ScriptedRuntimeFetcher.InstallAsync(
            Cell("root", sha256: sha), null, CancellationToken.None, CopyDownloader(zip), exts, _ => { }, FirstProbe, null);
        Assert.True(result.Ok, result.Message);
    }

    [Fact]
    public async Task UnreachableMirrorsFallThroughToFirstAndSurfaceDownloadError()
    {
        var exts = Path.Combine(mRoot, "exts5");
        var attempts = new List<string>();
        ScriptedRuntimeFetcher.DownloadAsyncDelegate recorder = (url, _, _, _) => { attempts.Add(url); return Task.FromException(new HttpRequestFail()); };
        var result = await ScriptedRuntimeFetcher.InstallAsync(
            Cell("root", "", "https://a.example.invalid/f.zip", "https://b.example.invalid/f.zip"),
            null, CancellationToken.None, recorder, exts, _ => { }, FirstProbe, null);
        Assert.False(result.Ok);
        Assert.Single(attempts);                       // 探活全灭 → 下载只试表内首选一次
        Assert.Contains("a.example.invalid", attempts[0]);
    }

    [Fact]
    public async Task OverrideBaseIsTriedBeforeTableMirrors()
    {
        var zip = MakeEmbedZip();
        var seen = new List<string>();
        ScriptedRuntimeFetcher.DownloadAsyncDelegate rec = (url, dest, _, _) => { seen.Add(url); File.Copy(zip, dest); return Task.CompletedTask; };
        var exts = Path.Combine(mRoot, "exts-ovr");
        var result = await ScriptedRuntimeFetcher.InstallAsync(
            Cell("root", "", "https://mirror.example/python/3.12.7/python-3.12.7-embed-amd64.zip",
                            "https://www.python.org/ftp/python/3.12.7/python-3.12.7-embed-amd64.zip"),
            null, CancellationToken.None, rec, exts, _ => { }, FirstProbe, "https://intranet.local/mirror/");
        Assert.True(result.Ok, result.Message);
        Assert.StartsWith("https://intranet.local/mirror/ftp/python/3.12.7/python-3.12.7-embed-amd64.zip", seen[0]);   // 官方相对路径挂覆写基址
    }

    [Fact]
    public void FeedMatchIsHonestWhenNoCellExists()
    {
        // 结构 Nullable 陷阱回归钉：无匹配格子时必须真 null（曾返回 default 空格子）。
        Assert.True(RuntimeSpec.TryParse("lua>=5.4", out var lua, out _));   // feed 无 lua 格子
        Assert.Null(RuntimeFeed.Match(lua));
    }

    [Fact]
    public void FeedCellsCoverCurrentPlatformWithCnMirrorsFirstAndOfficialLast()
    {
        Assert.True(RuntimeSpec.TryParse("python>=3.10,<4", out var py, out _));
        var cell = RuntimeFeed.Match(py);
        if (RidNow is "win-x64" or "win-arm64")
        {
            Assert.NotNull(cell);
            Assert.Equal("python", cell!.Value.Flavor);
            Assert.All(cell.Value.Urls, u => Assert.StartsWith("https://", u));
            Assert.Contains("huaweicloud", cell.Value.Urls[0]);              // 国内镜像在前
            Assert.Contains("python.org", cell.Value.Urls[^1]);              // 官方兜底在尾
        }
        else
            Assert.Null(cell);
    }
}

internal sealed class HttpRequestFail : Exception { }

// 自动部署调度环：在途去重 + 完成后回执（注入 executor，单测不触网）。
public class RuntimeInstallQueueTests
{
    [Fact]
    public async Task DedupsInFlightAndCallsOnDoneAfterCompletion()
    {
        var gate = new TaskCompletionSource<(bool, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dones = new List<(string, bool, string)>();
        var scheduled = RuntimeInstallQueue.Schedule("com.test.q-a", "C:\\pkg\\a", Parse("python>=3.10"),
            executor: _ => gate.Task, onDone: (d, ok, m) => dones.Add((d, ok, m)));
        Assert.True(scheduled);
        Assert.True(RuntimeInstallQueue.IsPending("com.test.q-a"));
        Assert.False(RuntimeInstallQueue.Schedule("com.test.q-a", "C:\\pkg\\a", Parse("python>=3.10"),
            executor: _ => Task.FromResult((true, "")), onDone: (d, ok, m) => dones.Add((d, ok, m))));   // 同包在途去重
        Assert.True(RuntimeInstallQueue.Schedule("com.test.q-b", "C:\\pkg\\b", Parse("python>=3.10"),
            executor: _ => Task.FromResult((false, "no mirror reachable")), onDone: (d, ok, m) => dones.Add((d, ok, m))));   // 不同包不受影响；M5c：失败带 message
        gate.SetResult((true, "installed"));
        var spin = Task.Run(() => { while (dones.Count < 2) Thread.SpinWait(1000); });
        Assert.Same(spin, await Task.WhenAny(spin, Task.Delay(5000)));
        Assert.Contains("C:\\pkg\\a", dones.Select(x => x.Item1));
        var failed = dones.First(x => x.Item1 == "C:\\pkg\\b");
        Assert.False(failed.Item2);
        Assert.Equal("no mirror reachable", failed.Item3);   // 失败原因透到回执
        Assert.False(RuntimeInstallQueue.IsPending("com.test.q-a"));
    }

    static RuntimeSpec Parse(string s)
    {
        Assert.True(RuntimeSpec.TryParse(s, out var spec, out _));
        return spec;
    }
}
