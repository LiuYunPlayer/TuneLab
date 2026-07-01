using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Utils;

public class UpdateInfo
{
    public Version? version;
    public string? url;
    public string? description;
    public DateTime publishedAt;
}

internal static class AppUpdateManager
{
    private static readonly string storageFile = Path.Combine(PathManager.ConfigsFolder, "UpdateIgnoreVersion.txt");

    // 更新检查的服务端地址。默认正式地址；设环境变量 TUNELAB_API_BASE 可指向本地/预发布做测试。
    private static string ApiBase =>
        Environment.GetEnvironmentVariable("TUNELAB_API_BASE") is { Length: > 0 } b ? b : "https://api.tunelab.app";

    public static async Task<UpdateInfo?> CheckForUpdate(bool ignoreVersion = true)
    {
        var queryParams = new Dictionary<string, object>
            {
                { "platform", PlatformHelper.GetPlatform() }
            };

        var response = await new HttpClient(ApiBase).GetAsync("/api/app/get-update", queryParams);

        if (!response.IsSuccessful)
        {
            throw new Exception(response.ErrorMessage);
        }

        var data = Newtonsoft.Json.JsonConvert.DeserializeObject<UpdateInfo>(response.Content);

        if (data == null)
        {
            throw new Exception("CheckUpdateFailed");
        }

        // 服务端未给版本号（字段缺失/解析失败）视为无更新，避免后续以 null 版本比较/落盘触发 NRE。
        if (data.version == null || data.version <= AppInfo.Version)
        {
            return null;
        }

        // 读忽略版本（文件不存在即未忽略过任何版本，无需预先创建）。
        if (ignoreVersion && File.Exists(storageFile))
        {
            try
            {
                var ignored = File.ReadAllText(storageFile);
                if (Version.TryParse(ignored, out var ignoredVersion) && ignoredVersion == data.version)
                    return null;
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to read update ignore file: {ex.Message}");
            }
        }

        return data;
    }

    public static void SaveIgnoreVersion(Version version)
    {
        try
        {
            Directory.CreateDirectory(PathManager.ConfigsFolder);
            File.WriteAllText(storageFile, version.ToString());
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save update ignore version: {ex.Message}");
        }
    }

    /// <summary>
    /// 下载整包安装器到临时目录，按 Content-Length 回报进度（0–1）。返回下载到的文件路径。
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(string url, IProgress<double>? progress, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "TuneLab.Update");
        Directory.CreateDirectory(dir);
        var fileName = TryGetFileName(url) ?? "TuneLab-Setup.exe";
        var destPath = Path.Combine(dir, fileName);

        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        long? total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);

        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total is > 0)
                progress?.Report((double)done / total.Value);
        }

        return destPath;
    }

    /// <summary>
    /// 拉起下载好的安装器进入静默更新模式：覆盖当前安装目录并重启 TuneLab。
    /// 调用方随后应退出本进程以释放文件锁（安装器会等锁释放）。
    /// </summary>
    public static void LaunchInstallerUpdate(string installerPath)
    {
        // 去掉结尾分隔符：BaseDirectory 以 '\' 结尾，朴素加引号时结尾 \" 会把闭合引号转义掉，
        // 导致安装器收到的目标路径尾部混入一个 " 而建目录失败。
        var installDir = AppDomain.CurrentDomain.BaseDirectory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        ProcessHelper.CreateProcess(installerPath, ["-update", installDir]);
    }

    static string? TryGetFileName(string url)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).AbsolutePath);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }
}
