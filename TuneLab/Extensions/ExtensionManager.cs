using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using TuneLab.Foundation;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Utils;

using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Voices;
using TuneLab.Extensions.Instruments;
using TuneLab.Extensions.Effect;
using TuneLab.Extensions.Agent;
namespace TuneLab.Extensions;

// 扩展统一加载管线：发现 → 读 manifest 判代际 → 校验 → V1 per-folder ALC 加载 / Legacy fallback → 实例化。
// 取代原先 Format/Voice 各自重复解析 description.json、各自 Assembly.LoadFrom 的分散结构。
internal static class ExtensionManager
{
    // host 提供的 SDK ABI 地板版本（V1）。插件 sdk-version 须 <= 此值方可加载。
    public static readonly Version SdkVersion = new(1, 0);

    // 编进宿主的官方内置能力（无安装包）的包 id——用于扩展设置按包分桶。
    // 选含括号的保留标识：反向域名包 id 不可能长这样，撞键风险为零，且配置文件里一眼可辨「宿主内置」。
    // 包 id 取值：BuiltInPackageId = 编进宿主的内置；legacy 老包 = 其目录名（无 V1 manifest id，见 LegacyPackageId）；
    // V1 安装包 = 其反向域名 id。三者互不相撞，且都能反查到显示名。
    public const string BuiltInPackageId = "(built-in)";

    // 结构化加载结果，供 sidebar 直接消费（取代字符串猜测）。
    public static IReadOnlyList<ExtensionLoadResult> LoadResults => mLoadResults;

    // 包 id → 人类可读包名（供「Extension Routing」矩阵列出各候选包）。
    // 内建无 LoadResult、给固定标签；V1 包按 id 反查 manifest 名；查不到/legacy 空 id 回退到 id 本身（或 "Legacy"）。
    public static string GetPackageName(string packageId)
    {
        if (packageId == BuiltInPackageId)
            return "Built-In";
        foreach (var r in mLoadResults)
            if (r.Id == packageId)
                return string.IsNullOrEmpty(r.Name) ? packageId : r.Name;
        return string.IsNullOrEmpty(packageId) ? "Legacy" : packageId;
    }

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
        InstrumentsManager.LoadBuiltIn();
        EffectManager.LoadBuiltIn();
        AgentModelManager.LoadBuiltIn();
        foreach (var dir in Directory.GetDirectories(PathManager.ExtensionsFolder))
        {
            Load(dir);
        }

        // 全部能力注册完毕后，把已落盘的扩展设置回喂给声明了 IExtensionSettings 的 extension（早于任何 Init/会话）。
        ExtensionSettingsManager.ApplyPersisted();
    }

    public static void Destroy()
    {
        VoicesManager.Destroy();
        InstrumentsManager.Destroy();
        EffectManager.Destroy();
        AgentModelManager.Destroy();
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
        var lang = TranslationManager.CurrentLanguage.Value;
        var result = new ExtensionLoadResult
        {
            DirectoryPath = path,
            Id = description.id,
            Name = description.LocalizedName(lang),
            Version = description.version,
            Author = description.author,
            Description = description.LocalizedDescription(lang),
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
        var reasons = new List<string>();

        foreach (var ext in description.EffectiveExtensions)
        {
            if (!ext.IsPlatformAvailable())
            {
                skipped++;
                reasons.Add(string.Format("{0}: platform not available", string.IsNullOrEmpty(ext.type) ? "extension" : ext.type));
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

            try
            {
                // manifest 内联身份：直接定位条目声明的单个程序集（不再盲扫目录）。
                var assemblyFile = ResolveAssemblyFile(path, ext);
                if (assemblyFile == null)
                {
                    failed++;
                    var reason = string.Format("{0}: assembly '{1}' not found", IdentityLabel(ext, kind), ext.assembly ?? "(unspecified)");
                    reasons.Add(reason);
                    Log.Warning(string.Format("Extension {0}: {1}", description.name, reason));
                    continue;
                }

                // 加载期 ABI 校验：插件对不兼容的旧 SDK 编译（引用了已改名/删除的类型或成员）时，此处即拒，
                // 而非加载后用时才 TypeLoad/MissingMethod 惊崩。
                if (!ValidateSdkReferences(assemblyFile, out var abiMissing))
                {
                    failed++;
                    var reason = string.Format("{0}: built against an incompatible SDK ({1} no longer exists)", IdentityLabel(ext, kind), abiMissing);
                    reasons.Add(reason);
                    Log.Warning(string.Format("Extension {0}: {1}", description.name, reason));
                    continue;
                }

                alc ??= new PluginLoadContext(path, assemblyFile);
                var assembly = alc.LoadFromAssemblyPath(assemblyFile);

                // 按 manifest 声明的 class（命名空间.类名）精确取类型并实例化注册（不再反射扫 attribute）。
                if (RegisterEntry(description.id ?? string.Empty, kind, ext, assembly, lang, out var error))
                {
                    loaded++;
                }
                else
                {
                    failed++;
                    reasons.Add(string.Format("{0}: {1}", IdentityLabel(ext, kind), error));
                    Log.Error(string.Format("Extension {0}: {1}: {2}", description.name, IdentityLabel(ext, kind), error));
                }
            }
            catch (Exception ex)
            {
                failed++;
                reasons.Add(string.Format("{0}: {1}", IdentityLabel(ext, kind), ex.Message));
                Log.Error(string.Format("Extension {0}: failed to load {1}: {2}", description.name, IdentityLabel(ext, kind), ex));
            }
        }

        if (loaded > 0)
            result.Status = (failed == 0 && skipped == 0) ? ExtensionLoadStatus.Loaded : ExtensionLoadStatus.PartiallyLoaded;
        else
            result.Status = failed > 0 ? ExtensionLoadStatus.Failed : ExtensionLoadStatus.Skipped;

        // 失败/跳过原因填入 Error（供侧边栏 tooltip 展示）；sdk-version 等已提前设置过的不覆盖。
        if (result.Status != ExtensionLoadStatus.Loaded
            && string.IsNullOrEmpty(result.Error) && reasons.Count > 0)
            result.Error = string.Join("; ", reasons);
    }

    // 加载期 ABI 校验：扫插件程序集对 TuneLab.SDK 的 TypeRef / MemberRef，逐个在宿主当前加载的 SDK 程序集解析。
    // 有解析不了的（类型或成员被改名/删除）即说明它对不兼容的旧 SDK 编译——加载就拒，免得用时才 TypeLoad/MissingMethod 崩主程序。
    // 纯读元数据表（不执行插件代码、不解析 IL、不实例化）；SDK 表面缓存一次，单插件成本亚毫秒。legacy 插件不引 TuneLab.SDK、自然全过。
    // 仅按"名"校验、不比签名——足以拦改名/删除这类常见破坏；属性经 get_/set_ 访问器名一并覆盖。父为 TypeSpec（泛型实例）等则跳过（保守、不误杀）。
    static bool ValidateSdkReferences(string assemblyFile, out string? missing)
    {
        missing = null;
        try
        {
            using var stream = File.OpenRead(assemblyFile);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                return true;

            var mr = pe.GetMetadataReader();
            var (sdkTypes, sdkMembers) = sSdkSurface.Value;

            // TypeRef：仅校验直属 AssemblyRef = TuneLab.SDK 的顶层类型；命中者记下句柄供 MemberRef 复用。
            var sdkTypeRefs = new Dictionary<TypeReferenceHandle, string>();
            foreach (var handle in mr.TypeReferences)
            {
                var typeRef = mr.GetTypeReference(handle);
                if (typeRef.ResolutionScope.Kind != HandleKind.AssemblyReference)
                    continue;
                var asmRef = mr.GetAssemblyReference((AssemblyReferenceHandle)typeRef.ResolutionScope);
                if (!mr.GetString(asmRef.Name).Equals("TuneLab.SDK", StringComparison.Ordinal))
                    continue;

                var ns = mr.GetString(typeRef.Namespace);
                var name = mr.GetString(typeRef.Name);
                var fullName = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
                if (!sdkTypes.Contains(fullName))
                {
                    missing = "type " + fullName;
                    return false;
                }
                sdkTypeRefs[handle] = fullName;
            }

            // MemberRef：父为命中 SDK 的 TypeRef 时，按成员名校验存在。
            foreach (var handle in mr.MemberReferences)
            {
                var memberRef = mr.GetMemberReference(handle);
                if (memberRef.Parent.Kind != HandleKind.TypeReference)
                    continue;
                if (!sdkTypeRefs.TryGetValue((TypeReferenceHandle)memberRef.Parent, out var typeFull))
                    continue;
                var memberName = mr.GetString(memberRef.Name);
                if (sdkMembers.TryGetValue(typeFull, out var names) && !names.Contains(memberName))
                {
                    missing = "member " + typeFull + "." + memberName;
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            // 读元数据失败不拦（保守）：交由后续加载/调用兜底，避免误杀。
            Log.Warning(string.Format("SDK reference validation skipped for {0}: {1}", assemblyFile, ex.Message));
            return true;
        }
    }

    // 宿主当前 SDK 的全部类型全名集 + 各类型成员名集（含 get_/set_ 访问器与 .ctor）；构建一次缓存（SDK 程序集小、毫秒内）。
    static readonly Lazy<(HashSet<string> Types, Dictionary<string, HashSet<string>> Members)> sSdkSurface = new(() =>
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        var members = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        foreach (var type in typeof(IControllerConfig).Assembly.GetTypes())
        {
            var full = type.FullName;
            if (full == null)
                continue;
            types.Add(full);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in type.GetMembers(flags))
                names.Add(member.Name);
            members[full] = names;
        }
        return (types, members);
    });

    static void LoadLegacy(string path, ExtensionDescription? description, string folderName)
    {
        var lang = TranslationManager.CurrentLanguage.Value;
        var result = new ExtensionLoadResult
        {
            DirectoryPath = path,
            // legacy 无 V1 manifest id，用目录名当包 id（每安装唯一、稳定）：供冲突路由区分各 legacy 包 + 反查真实包名。
            Id = LegacyPackageId(path),
            Name = description?.LocalizedName(lang) ?? folderName,
            Version = description?.version ?? "1.0.0",
            Author = description?.author ?? string.Empty,
            Description = description?.LocalizedDescription(lang) ?? string.Empty,
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

        // 无 compat hook（或 hook 未接管）：宿主自身无法加载链接旧 SDK 的 Legacy 插件——
        // 真实 Legacy 须由 Compat.Legacy 经 LegacyLoadHook 合成面向当前 SDK 的适配器后接管。
        result.Status = ExtensionLoadStatus.Skipped;
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

    static bool IsCodeKind(string kind) => kind is "format" or "voice" or "instrument" or "effect" or "agent-model";

    // legacy 包的稳定包 id（无 V1 manifest id 时）：用目录名——每个安装唯一、跨会话稳定，
    // 供冲突消解区分多个 legacy 包并反查显示名。LegacyCompatLoader 注册与 LoadResult.Id 须用同一值。
    public static string LegacyPackageId(string packageDir) => Path.GetFileName(packageDir);

    // 条目身份标签（用于日志/错误前缀）：format 以扩展名、引擎类以 engine id 标识。
    static string IdentityLabel(ExtensionInfo ext, string kind)
        => kind == "format"
            ? string.Format("format '{0}'", ext.extension ?? "?")
            : string.Format("{0} '{1}'", kind, ext.engine ?? "?");

    // 条目声明的单个程序集（相对包目录）；未声明或文件不存在返回 null（调用方按失败处理）。
    static string? ResolveAssemblyFile(string path, ExtensionInfo ext)
    {
        if (string.IsNullOrEmpty(ext.assembly))
            return null;
        var file = Path.Combine(path, ext.assembly);
        return File.Exists(file) ? file : null;
    }

    // 按类别把 manifest 条目实例化并注册到对应 manager。失败回 false + error（不抛，调用方计 failed）。
    // displayName 按当前语言从 manifest 取（与 id 分离、仅供 UI 展示）；缺省回退到身份 id。
    // packageId 是包 manifest 的反向域名 id，下传给 manager 供扩展设置按包分桶（避免不同包同 engine id 设置串味）。
    static bool RegisterEntry(string packageId, string kind, ExtensionInfo ext, Assembly assembly, string lang, out string? error)
    {
        var displayName = ext.LocalizedName(lang);
        var candidates = ext.CandidateClasses;   // 候选入口类（新版 classes + 旧版 class/import/export 折叠）
        switch (kind)
        {
            case "voice":
                if (string.IsNullOrEmpty(ext.engine)) { error = "missing 'engine' id"; return false; }
                if (!TryScanCtor<IVoiceSynthesisEngine>(assembly, candidates, out var vctor, out error)) return false;
                VoicesManager.RegisterEngine(packageId, ext.engine, displayName, (IVoiceSynthesisEngine)vctor!.Invoke(null));
                return true;

            case "instrument":
                if (string.IsNullOrEmpty(ext.engine)) { error = "missing 'engine' id"; return false; }
                if (!TryScanCtor<IInstrumentSynthesisEngine>(assembly, candidates, out var ictor2, out error)) return false;
                InstrumentsManager.RegisterEngine(packageId, ext.engine, displayName, (IInstrumentSynthesisEngine)ictor2!.Invoke(null));
                return true;

            case "effect":
                if (string.IsNullOrEmpty(ext.engine)) { error = "missing 'engine' id"; return false; }
                if (!TryScanCtor<IEffectEngine>(assembly, candidates, out var ector, out error)) return false;
                EffectManager.RegisterEngine(packageId, ext.engine, displayName, (IEffectEngine)ector!.Invoke(null));
                return true;

            case "agent-model":
                if (string.IsNullOrEmpty(ext.engine)) { error = "missing 'engine' id"; return false; }
                if (!TryScanCtor<IAgentModelEngine>(assembly, candidates, out var actor, out error)) return false;
                AgentModelManager.RegisterEngine(packageId, ext.engine, displayName, (IAgentModelEngine)actor!.Invoke(null));
                return true;

            case "format":
                return RegisterFormatEntry(packageId, ext, assembly, candidates, displayName, out error);
        }
        error = "unknown extension type";
        return false;
    }

    // format 条目：扫候选类认领 IImportFormat / IExportFormat（各可缺其一，至少一个）。工厂延迟实例化（与旧行为一致），
    // 但类型/构造在加载期即扫描校验。同一个类可同时实现两接口（则导入导出都注册它）。
    static bool RegisterFormatEntry(string packageId, ExtensionInfo ext, Assembly assembly, string[] candidates, string displayName, out string? error)
    {
        if (string.IsNullOrEmpty(ext.extension)) { error = "missing 'extension'"; return false; }
        if (candidates.Length == 0) { error = "no entry 'classes' declared"; return false; }

        bool any = false;
        if (TryScanCtor<IImportFormat>(assembly, candidates, out var ictor, out _))
        {
            FormatsManager.RegisterImporter(packageId, ext.extension, displayName, () => (IImportFormat)ictor!.Invoke(null));
            any = true;
        }
        if (TryScanCtor<IExportFormat>(assembly, candidates, out var ector, out _))
        {
            FormatsManager.RegisterExporter(packageId, ext.extension, displayName, () => (IExportFormat)ector!.Invoke(null));
            any = true;
        }
        if (!any)
        {
            error = string.Format("no class implementing IImportFormat or IExportFormat among [{0}]", string.Join(", ", candidates));
            return false;
        }
        error = null;
        return true;
    }

    // 扫候选类清单，取首个实现 T 且有无参构造的类的构造器。无候选 / 无命中 / 命中但缺无参构造回 false + 可读 error。
    // 不实现 T 的候选不算错误（同一清单服务多个接口，逐个认领）——只在最终无命中时汇总原因。
    static bool TryScanCtor<T>(Assembly assembly, string[] candidates, out ConstructorInfo? ctor, out string? error)
    {
        ctor = null;
        error = null;
        if (candidates.Length == 0) { error = "no entry 'classes' declared"; return false; }

        var notes = new List<string>();
        foreach (var className in candidates)
        {
            var type = assembly.GetType(className);
            if (type == null) { notes.Add(string.Format("'{0}' not found", className)); continue; }
            if (!typeof(T).IsAssignableFrom(type)) continue;   // 不实现该接口：正常，扫下一个候选

            var c = type.GetConstructor(Type.EmptyTypes);
            if (c == null) { notes.Add(string.Format("'{0}' implements {1} but has no parameterless constructor", className, typeof(T).Name)); continue; }
            ctor = c;
            return true;
        }
        error = notes.Count > 0
            ? string.Join("; ", notes)
            : string.Format("no class implementing {0} among [{1}]", typeof(T).Name, string.Join(", ", candidates));
        return false;
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
