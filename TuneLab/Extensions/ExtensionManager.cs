using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using TuneLab.Foundation.Utils;
using TuneLab.Utils;

using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Voices;
namespace TuneLab.Extensions;

// 扩展统一加载管线：发现 → 读 manifest 判代际 → 校验 → V1 per-folder ALC 加载 / Legacy fallback → 实例化。
// 取代原先 Format/Voice 各自重复解析 description.json、各自 Assembly.LoadFrom 的分散结构。
internal static class ExtensionManager
{
    // host 提供的 SDK ABI 地板版本（V1）。插件 sdk-version 须 <= 此值方可加载。
    public static readonly Version SdkVersion = new(1, 0);

    // 结构化加载结果，供 sidebar 直接消费（取代字符串猜测）。
    public static IReadOnlyList<ExtensionLoadResult> LoadResults => mLoadResults;

    // Compat.Legacy 接入点：设置后接管 Legacy 包加载，返回 true 表示已处理。
    // 第三参 typeSink 由 hook 在注册成功时回填真实类别（"format"/"voice"），供 sidebar
    // 展示精确类型而非笼统 "Legacy"——hook 在 Compat 内部才知道老插件实际实现了哪些接口。
    public static Func<string, ExtensionDescription?, ICollection<string>, bool>? LegacyLoadHook { get; set; }

    public static IReadOnlyList<string> PendingUninstalls => mPendingUninstalls;
    public static bool RestartAfterUninstall { get; set; }

    public static void LoadExtensions()
    {
        PathManager.MakeSureExist(PathManager.ExtensionsFolder);
        FormatsManager.LoadBuiltIn();
        VoicesManager.LoadBuiltIn();
        foreach (var dir in Directory.GetDirectories(PathManager.ExtensionsFolder))
        {
            Load(dir);
        }
    }

    public static void Destroy()
    {
        VoicesManager.Destroy();
    }

    // 加载单个插件包目录；结果累积进 LoadResults（供 sidebar 实时刷新）。失败不崩主程序。
    public static void Load(string path)
    {
        var folderName = Path.GetFileName(path);

        // ── 发现：读 description.json（一次，集中）──
        ExtensionDescription? description = null;
        var descriptionPath = Path.Combine(path, "description.json");
        if (File.Exists(descriptionPath))
        {
            try
            {
                using var stream = File.OpenRead(descriptionPath);
                description = JsonSerializer.Deserialize<ExtensionDescription>(stream);
            }
            catch (Exception ex)
            {
                // 有文件但坏了：按 V1 报错（而非误判 Legacy 去盲扫），优雅降级。
                Log.Error(string.Format("Failed to parse description of {0}: {1}", folderName, ex));
                mLoadResults.Add(new ExtensionLoadResult
                {
                    DirectoryPath = path,
                    Name = folderName,
                    Generation = ExtensionGeneration.V1,
                    Status = ExtensionLoadStatus.Failed,
                    Error = "Invalid description.json: " + ex.Message,
                });
                return;
            }
        }

        // ── 判代际：含 id ⇒ V1；否则 Legacy ──
        if (description != null && description.IsV1)
            LoadV1(path, description);
        else
            LoadLegacy(path, description, folderName);
    }

    static void LoadV1(string path, ExtensionDescription description)
    {
        var result = new ExtensionLoadResult
        {
            DirectoryPath = path,
            Id = description.id,
            Name = description.name,
            Version = description.version,
            Author = description.author,
            Description = description.description,
            IconPath = ResolveIconPath(path, description.icon),
            Generation = ExtensionGeneration.V1,
        };
        mLoadResults.Add(result);

        // ── 校验：sdk-version 兼容门（代码包声明；资源包可省略）──
        if (!string.IsNullOrEmpty(description.sdkVersion))
        {
            if (!Version.TryParse(description.sdkVersion, out var required))
            {
                result.Status = ExtensionLoadStatus.Failed;
                result.Error = string.Format("Invalid sdk-version '{0}'", description.sdkVersion);
                Log.Error(string.Format("Extension {0}: invalid sdk-version '{1}'", description.name, description.sdkVersion));
                return;
            }
            if (required > SdkVersion)
            {
                result.Status = ExtensionLoadStatus.Skipped;
                result.Error = string.Format("Requires SDK {0}, host provides {1}", required, SdkVersion);
                Log.Warning(string.Format("Extension {0} skipped: requires SDK {1}, host provides {2}", description.name, required, SdkVersion));
                return;
            }
        }

        // ── 加载：per-folder ALC，遍历归一化后的各 extension ──
        PluginLoadContext? alc = null;
        int loaded = 0, failed = 0, skipped = 0;
        var skipReasons = new List<string>();

        foreach (var ext in description.EffectiveExtensions)
        {
            if (!ext.IsPlatformAvailable())
            {
                skipped++;
                skipReasons.Add(string.Format("{0}: platform not available", string.IsNullOrEmpty(ext.type) ? "extension" : ext.type));
                continue;
            }

            var kind = (ext.type ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(kind) && !result.Types.Contains(kind))
                result.Types.Add(kind);

            // 资源类（无代码）：登记即可，不加载程序集（由对应引擎运行时去发现目录内资源）。
            if (!IsCodeKind(kind))
            {
                loaded++;
                continue;
            }

            // effect：SDK.Effect 接口形状未定，暂不支持，优雅降级。
            if (kind == "effect")
            {
                skipped++;
                skipReasons.Add("effect extensions are not supported in this build yet");
                Log.Warning(string.Format("Extension {0}: effect extensions are not supported in this build yet.", description.name));
                continue;
            }

            try
            {
                // 解析要扫描的程序集（写了 assemblies 用写的；没写则扫目录全部 dll — 性能 fallback）。
                var assemblyFiles = ResolveAssemblyFiles(path, ext);
                if (assemblyFiles.Count == 0)
                {
                    failed++;
                    Log.Warning(string.Format("Extension {0}: no assemblies found for {1} extension.", description.name, kind));
                    continue;
                }

                alc ??= new PluginLoadContext(path, assemblyFiles[0]);

                var types = new List<Type>();
                foreach (var file in assemblyFiles)
                {
                    try
                    {
                        types.AddRange(alc.LoadFromAssemblyPath(file).GetTypes());
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(string.Format("Extension {0}: failed to load assembly {1}: {2}", description.name, Path.GetFileName(file), ex.Message));
                    }
                }

                RegisterByKind(kind, types.ToArray(), path);
                loaded++;
            }
            catch (Exception ex)
            {
                failed++;
                Log.Error(string.Format("Extension {0}: failed to load {1} extension: {2}", description.name, kind, ex));
            }
        }

        if (loaded > 0)
            result.Status = (failed == 0 && skipped == 0) ? ExtensionLoadStatus.Loaded : ExtensionLoadStatus.PartiallyLoaded;
        else
            result.Status = failed > 0 ? ExtensionLoadStatus.Failed : ExtensionLoadStatus.Skipped;

        // 跳过原因填入 Error（供侧边栏 tooltip 展示）；sdk-version 等已提前设置过的不覆盖。
        if ((result.Status == ExtensionLoadStatus.Skipped || result.Status == ExtensionLoadStatus.PartiallyLoaded)
            && string.IsNullOrEmpty(result.Error) && skipReasons.Count > 0)
            result.Error = string.Join("; ", skipReasons);
    }

    static void LoadLegacy(string path, ExtensionDescription? description, string folderName)
    {
        var result = new ExtensionLoadResult
        {
            DirectoryPath = path,
            Name = description?.name ?? folderName,
            Version = description?.version ?? "1.0.0",
            Author = description?.author ?? string.Empty,
            Description = description?.description ?? string.Empty,
            IconPath = ResolveIconPath(path, description?.icon),
            Generation = ExtensionGeneration.Legacy,
        };
        mLoadResults.Add(result);

        // 平台过滤（老 schema 也支持 platforms）。
        if (description != null && !description.IsPlatformAvailable())
        {
            result.Status = ExtensionLoadStatus.Skipped;
            result.Error = "Platform not supported.";
            Log.Warning(string.Format("Failed to load extension {0}: Platform not supported.", folderName));
            return;
        }

        // Compat.Legacy 实装后由它接管（合成面向当前 SDK 的适配器）。
        if (LegacyLoadHook != null)
        {
            try
            {
                // hook 把发现的真实类别（format/voice）回填进 result.Types，sidebar 据此展示精确类型。
                if (LegacyLoadHook(path, description, result.Types))
                {
                    result.Status = ExtensionLoadStatus.Loaded;
                    return;
                }
            }
            catch (Exception ex)
            {
                result.Status = ExtensionLoadStatus.Failed;
                result.Error = ex.Message;
                Log.Error(string.Format("Legacy extension {0} failed via compat layer: {1}", folderName, ex));
                return;
            }
        }

        // 无 compat hook：保留既有"盲扫尽力而为"行为（不回归）。真实 Legacy 插件链接旧程序集，
        // 找不到新 SDK attribute 会优雅失败；待 Compat.Legacy 接入 LegacyLoadHook 后接管。
        var assemblyFiles = (description != null && !description.assemblies.IsEmpty())
            ? description.assemblies.Select(a => Path.Combine(path, a)).Where(File.Exists)
            : Directory.GetFiles(path, "*.dll");

        bool any = false;
        foreach (var file in assemblyFiles)
        {
            try
            {
                var types = Assembly.LoadFrom(file).GetTypes();
                FormatsManager.RegisterFromTypes(types);
                VoicesManager.RegisterFromTypes(types, path);
                any = true;
            }
            catch { }
        }

        result.Status = any ? ExtensionLoadStatus.Loaded : ExtensionLoadStatus.Skipped;
        if (!any)
            result.Error = LegacyLoadHook != null
                ? "Legacy compatibility layer ran but found no compatible plugin in this package (see log)."
                : "Legacy compatibility layer not available.";
    }

    // 把 manifest 里的包内相对图标路径解析成绝对路径；为空或文件不存在则返回 null（sidebar 退回首字母占位）。
    static string? ResolveIconPath(string packagePath, string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return null;

        var full = Path.Combine(packagePath, icon);
        return File.Exists(full) ? full : null;
    }

    static bool IsCodeKind(string kind) => kind is "format" or "voice" or "effect";

    static List<string> ResolveAssemblyFiles(string path, ExtensionInfo ext)
    {
        if (!ext.assemblies.IsEmpty())
            return ext.assemblies.Select(a => Path.Combine(path, a)).Where(File.Exists).ToList();

        // 没写 assemblies：扫目录全部 dll（性能 fallback；逐文件容错由调用方处理）。
        return Directory.GetFiles(path, "*.dll").ToList();
    }

    static void RegisterByKind(string kind, Type[] types, string path)
    {
        switch (kind)
        {
            case "format": FormatsManager.RegisterFromTypes(types); break;
            case "voice": VoicesManager.RegisterFromTypes(types, path); break;
        }
    }

    public static void AddPendingUninstall(string extensionDirPath)
    {
        if (!mPendingUninstalls.Contains(extensionDirPath))
            mPendingUninstalls.Add(extensionDirPath);
    }

    // 撤销待卸载（用户在"稍后"标记后反悔）。
    public static void RemovePendingUninstall(string extensionDirPath)
    {
        mPendingUninstalls.Remove(extensionDirPath);
    }

    public static void LaunchPendingUninstalls()
    {
        if (mPendingUninstalls.Count == 0)
            return;

        string installer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "ExtensionInstaller.exe"
            : "ExtensionInstaller";
        var installerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, installer);
        List<string> args = [];
        if (RestartAfterUninstall)
            args.Add("-restart");
        args.Add("-uninstall");
        args.AddRange(mPendingUninstalls);
        ProcessHelper.CreateProcess(installerPath, args);
        mPendingUninstalls.Clear();
    }

    static readonly List<ExtensionLoadResult> mLoadResults = [];
    static readonly List<string> mPendingUninstalls = [];
}
