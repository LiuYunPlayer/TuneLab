using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Configs;
using TuneLab.Foundation;
using TuneLab.Utils;
using NetHttpClient = System.Net.Http.HttpClient;

namespace TuneLab.Extensions.Scripted;

// 脚本运行时自动部署（设计文档 §8.6）：镜像表命中 → 竞速择最快可达镜像 → 下载 →（有钉则）验 sha256
// → 解包成 runtime 资源包布局（§8.4）→ 落进 Extensions 目录并主线程登记。
// 之后 RuntimeLocator 的 ③ 级（已装 runtime 包）自然命中，脚本条目得以懒起。
//
// 下载/落盘根/登记全可注入（Downloader/extensionsRoot/register），单测喂预制 zip 即可跑通
// "落盘→登记→定位命中"全链，不碰真网络（CI 无解释器/无外网）。
internal static class ScriptedRuntimeFetcher
{
    public delegate Task DownloadAsyncDelegate(string url, string destPath, IProgress<double>? progress, CancellationToken ct);

    public readonly record struct Result(bool Ok, string Message, string? PackageDir);

    public static Task<Result> FetchInstallAsync(RuntimeSpec spec, IProgress<double>? progress = null,
        CancellationToken ct = default, DownloadAsyncDelegate? downloader = null)
        => FetchInstallAsync(spec, progress, ct, downloader, null, null);

    // 完整入口：extensionsRoot/register 供测试注入（生产传 null → 真实 ExtensionsFolder + 主线程登记）。
    public static Task<Result> FetchInstallAsync(RuntimeSpec spec, IProgress<double>? progress,
        CancellationToken ct, DownloadAsyncDelegate? downloader, string? extensionsRootOverride, Action<string>? registerOverride)
    {
        var cell = RuntimeFeed.Match(spec);
        if (cell == null)
            return Task.FromResult(new Result(false, string.Format(CultureInfo.InvariantCulture,
                "no auto-installable runtime for {0} on this platform ({1}); install one manually or via a 'runtime' package",
                spec.Describe(), PlatformHelper.GetPlatform()), null));
        return InstallAsync(cell.Value, progress, ct, downloader, extensionsRootOverride, registerOverride);
    }

    // 供测试直接喂受控 cell（假 sha256 / 预制归档）的底层入口。probe/overrideBase 可注入
    // （单测不碰真网络、不依赖 SettingsRegistry）。
    public static Task<Result> InstallAsync(RuntimeFeedCell cell, IProgress<double>? progress,
        CancellationToken ct, DownloadAsyncDelegate? downloader, string? extensionsRootOverride = null, Action<string>? registerOverride = null)
        => InstallAsync(cell, progress, ct, downloader, extensionsRootOverride, registerOverride, null, null);

    public static async Task<Result> InstallAsync(RuntimeFeedCell cell, IProgress<double>? progress,
        CancellationToken ct, DownloadAsyncDelegate? downloader, string? extensionsRootOverride, Action<string>? registerOverride,
        Func<IReadOnlyList<string>, CancellationToken, Task<string>>? probeOverride, string? overrideBase)
    {
        var urls = OrderUrls(cell.Urls, overrideBase ?? SettingsRegistry.ScriptedRuntimeSource.Property.Value);
        if (urls.Count == 0)
            return new Result(false, "runtime feed cell has no URLs", null);
        var chosen = await (probeOverride ?? PickFastestAsync)(urls, ct).ConfigureAwait(false);

        var work = Path.Combine(Path.GetTempPath(), "tunelab-runtime", $"{cell.Flavor}-{cell.Version.ToString(3)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            var archive = Path.Combine(work, "archive" + ExtOf(chosen));
            Log.Info(string.Format(CultureInfo.InvariantCulture, "fetching runtime {0} {1} from {2}", cell.Flavor, cell.Version.ToString(3), chosen));
            await (downloader ?? DownloadAsync)(chosen, archive, progress, ct).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(cell.Sha256))
            {
                var actual = Sha256File(archive);
                if (!string.Equals(actual, cell.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(archive);
                    return new Result(false, string.Format(CultureInfo.InvariantCulture,
                        "downloaded runtime failed checksum (expected {0}, got {1})", cell.Sha256, actual), null);
                }
            }
            else
                Log.Warning(string.Format(CultureInfo.InvariantCulture,
                    "runtime {0} {1} is not sha256-pinned in the feed; trusting the source this once", cell.Flavor, cell.Version.ToString(3)));

            var installDir = LayOut(cell, archive, extensionsRootOverride ?? PathManager.ExtensionsFolder);
            if (installDir == null)
                return new Result(false, "failed to lay out the downloaded archive into a runtime package", null);

            (registerOverride ?? RegisterOnMainThread)(installDir);   // 就地登记；RuntimeLocator ③ 级随即命中
            Log.Info(string.Format(CultureInfo.InvariantCulture, "runtime package installed at {0}", installDir));
            return new Result(true, string.Format(CultureInfo.InvariantCulture, "installed {0} {1}", cell.Flavor, cell.Version.ToString(3)), installDir);
        }
        catch (OperationCanceledException)
        {
            return new Result(false, "cancelled", null);
        }
        catch (Exception ex)
        {
            Log.Error(string.Format(CultureInfo.InvariantCulture, "runtime fetch failed: {0}", ex));
            return new Result(false, ex.Message, null);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    // 镜像序：覆写源（显式参数或设置项）排最前（相对路径取自官方源=表内末位），
    // 其余按表内首选序（国内镜像在前、官方兜底在后）。
    static List<string> OrderUrls(IReadOnlyList<string> table, string? overrideBase)
    {
        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(overrideBase) && table.Count > 0)
        {
            var rel = new Uri(table[^1]).AbsolutePath.TrimStart('/');      // 相对路径取自官方源（表内末位）
            urls.Add(overrideBase.TrimEnd('/') + "/" + rel);
        }
        urls.AddRange(table);
        return urls;
    }

    // 竞速探活：HEAD 各候选（3s 超时），返回最先成功者；全失败回退首选（让真下载去报错/重试）。
    static async Task<string> PickFastestAsync(IReadOnlyList<string> urls, CancellationToken ct)
    {
        if (urls.Count == 1)
            return urls[0];
        using var http = new NetHttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var tasks = urls.Select(async u =>
        {
            try
            {
                using var _ = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, u),
                    HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                return (u, ok: true);
            }
            catch { return (u, ok: false); }
        }).ToList();
        var pending = tasks.ToList();
        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(finished);
            var r = await finished.ConfigureAwait(false);
            if (r.ok)
                return r.u;
        }
        return urls[0];
    }

    // 默认下载器（流式 + 进度；Content-Type 挡一道"把网页当安装包"的历史事故）。
    static async Task DownloadAsync(string url, string destPath, IProgress<double>? progress, CancellationToken ct)
    {
        using var http = new NetHttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var media = resp.Content.Headers.ContentType?.MediaType;
        if (media != null && media.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, "server returned '{0}' not an archive: {1}", media, url));
        long? total = resp.Content.Headers.ContentLength;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = File.Create(destPath))
        {
            var buf = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total is > 0)
                    progress?.Report((double)done / total.Value);
            }
        }
    }

    // 默认登记：Load 写宿主注册表，须在主线程（同 `extension install` 的 OnMainThread 约束）；
    // 从池线程调用时经 UI 线程投送并等完成——调用方本就在后台，阻塞无害且结果确定。
    static void RegisterOnMainThread(string dir)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ExtensionManager.Load(dir);
        else
            Commands.UiThreadDispatcher.Instance.InvokeAsync(() => { ExtensionManager.Load(dir); return true; })
                .GetAwaiter().GetResult();
    }

    // 解包 → `<extensionsRoot>/<name>/{manifest.json, runtime/...}`，返回目录；布局对齐 RuntimeLocator.ResolvePackageExe。
    static string? LayOut(RuntimeFeedCell cell, string archive, string extensionsRoot)
    {
        if (!archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return null;   // M3 只覆盖 .zip（python embed / node win dist）；unix tar 布局后续扩
        var extract = Path.Combine(Path.GetTempPath(), "tunelab-runtime-x", Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(archive, extract);
            var contentRoot = cell.Layout == "prefix" ? FlattenSingleTopDir(extract) : extract;
            var name = string.Format(CultureInfo.InvariantCulture, "runtime-{0}-{1}-{2}", cell.Flavor, cell.Version.ToString(3), cell.Rid);
            var target = Path.Combine(extensionsRoot, name);
            if (Directory.Exists(target))
                try { Directory.Delete(target, true); } catch { return null; }
            PathManager.MakeSureExist(target);
            CopyDir(contentRoot, Path.Combine(target, "runtime"));
            File.WriteAllText(Path.Combine(target, "manifest.json"), BuildManifestJson(cell, name));
            return target;
        }
        finally
        {
            try { Directory.Delete(extract, true); } catch { }
        }
    }

    static string FlattenSingleTopDir(string extract)
    {
        var dirs = Directory.GetDirectories(extract);
        if (dirs.Length == 1 && Directory.GetFiles(extract).Length == 0)
            return dirs[0];
        return extract;
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    static string BuildManifestJson(RuntimeFeedCell cell, string name)
    {
        var m = new Dictionary<string, object?>
        {
            ["id"] = string.Format(CultureInfo.InvariantCulture, "com.tunelab.runtime.{0}.{1}", cell.Rid, cell.Flavor),
            ["name"] = string.Format(CultureInfo.InvariantCulture, "TuneLab {0} Runtime ({1}, {2})", cell.Flavor, cell.Version.ToString(3), cell.Rid),
            ["version"] = "1.0.0",
            ["author"] = "TuneLab",
            ["description"] = "Auto-installed interpreter for scripted extensions.",
            ["type"] = "runtime",
            ["flavor"] = cell.Flavor,
            ["runtime-version"] = cell.Version.ToString(3),
            ["platforms"] = new[] { cell.Rid },
        };
        return JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true });
    }

    static string Sha256File(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    static string ExtOf(string url)
    {
        var p = new Uri(url).AbsolutePath;
        var i = p.LastIndexOf('.');
        return i < 0 ? ".zip" : p[i..];
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
