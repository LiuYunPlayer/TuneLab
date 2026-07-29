using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using TuneLab.Extensions;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Formats;

// 格式注册表（与各引擎 manager 同范式）：身份 = 文件扩展名，跨包可重名，多包同扩展名均并存，
// 活实现由 ExtensionRoutingStore 解析。【import/export 各算一条可路由身份】（routeKey kind = "format-import"/"format-export"），
// 故一个扩展名可分别为导入、导出选不同包的实现。工厂延迟实例化（与旧行为一致）。
//
// 【两个粒度】后缀是**路由**单位（逐后缀、且分导入导出）；条目（= 一个包的一个格式实现，可认多个后缀别名）
//   才是**声明与设置**单位——一份实现类、一份说明、一份扩展设置。扩展设置见本文件末尾的 FormatEntry。
internal static class FormatsManager
{
    // 内建格式显式注册（编进宿主、无 manifest.json，故不走 manifest，直接登记）。
    public static void LoadBuiltIn()
    {
        var pkg = ExtensionManager.BuiltInPackageId;
        RegisterBuiltIn(pkg, ["tlp"], "TuneLab Project", () => new TLP.TuneLabProject(), () => new TLP.TuneLabProject());
        RegisterBuiltIn(pkg, ["tlpx"], "TuneLab Project (CBOR)", () => new TLP.TuneLabProjectCbor(), () => new TLP.TuneLabProjectCbor());
        // mid / midi 是同一格式的两个后缀别名：一个条目认两个后缀（各自仍可被独立路由），共用同一个实现类。
        // 早年扩展名靠代码 attribute 标注、一个类只能标一个后缀，才不得不造 MidiWithExtension_mid/_midi
        // 那样的双胞胎类；V1 起后缀由 manifest（内建则由此处）给出，那个约束早已不存在。
        RegisterBuiltIn(pkg, ["mid", "midi"], "MIDI", () => new Midi.MidiFormat(), () => new Midi.MidiFormat());
    }

    // 内建格式一律是双向紧凑形态（同一个类读写），且都没有扩展设置。
    static void RegisterBuiltIn(string packageId, string[] suffixes, string displayName,
        Func<IImportFormat> importFactory, Func<IExportFormat> exportFactory)
    {
        RegisterFormat(packageId, KindBoth, suffixes, displayName, (suffixes, importFactory), (suffixes, exportFactory), false);
    }

    // format 条目的不可变身份（= 扩展设置的分桶键中段）：**`suffixes` 全部后缀按声明序拼接**。
    // 完整桶键是 "<type>:<EntryId>"，type 即条目声明的那三种之一 —— format / format-import / format-export。
    //
    // 为何不取首个后缀、也不另给作者一个显式 id 字段：设置属于那一份实现，而 manifest 恰恰声明了
    // 「这一份实现对这几个后缀都成立」——遇到其中任一后缀都初始化同一个类、也就该对应同一份设置。
    // 拼全清单是唯一对全部后缀一视同仁的做法（取首个则重排后缀就换桶，且凭什么是首个）。
    // 作者日后增删/重排后缀 ⇒ 拼接串变化 ⇒ **换桶，且不回退任何旧设置**，按全新条目对待。
    //
    // 声明序而非排序：manifest 怎么写就怎么拼，与 EffectiveSuffixes（剔空/去重/小写/保序）同口径，
    // 也与注册、路由、ExtensionEntryInfo.Identities 用的是同一份清单——几处对得上才不会各算各的键。
    //
    // 【拼的是 suffixes，不是方向子集】import-suffixes / export-suffixes 只收窄「哪些后缀走哪个方向」，
    // 不改变这份实现的身份，故**不进键**。于是作者事后放宽或收窄某一侧（如补上 .mid 的写出）不会换桶——
    // 那本就不该清空用户已填的设置；只有身份集 suffixes 变了才换。
    public static string EntryId(IReadOnlyList<string> suffixes) => string.Join(SuffixSeparator, suffixes);

    // 拼接用 '|'：它是 Windows 文件名的**禁用字符**，所以任何能在 Windows 上存在的文件、其扩展名都不可能含它
    // ——单射不靠转义、也不靠我们向"扩展名"这个不属于自己的命名空间强加规矩，而是白拿文件系统已有的约束。
    // 同一条原理早已在用：键的 "kind:identity" 之所以从没出过事，正因为 ':' 同样是 Windows 禁用字符。
    // （macOS/Linux 的文件名允许 '|'，故这条保证是 Windows 侧的。真撞上也只是同一个包内两个条目共用一份设置，
    //  且不可能跨包串味——外层还有 packageId 分桶——故不为这个概率写校验。）
    const char SuffixSeparator = '|';

    // 条目的三种 kind（= 桶键前段、启停键前段），与 ExtensionRouting 的 routeKey kind 同名同义。
    // 【它是推出来的，不是作者写的】manifest 的 type 恒为 "format"，方向由填了哪几个后缀字段决定
    // （见 DeriveKind）。作者只回答"能读哪些、能写哪些"，宿主据此定它占哪些能力位、落哪个桶。
    public const string KindBoth = "format";
    public const string KindImport = "format-import";
    public const string KindExport = "format-export";

    // 三者的共同判据。凡按"这个条目的身份是不是文件后缀"分支处都用它，而不是逐个比 KindBoth
    // ——漏一个就会让单向条目的身份退化成空，能力位、启停、详情窗齿轮一起失联。
    public static bool IsFormatKind(string kind) => kind is KindBoth or KindImport or KindExport;

    // 由"有没有这个方向"推出 kind。纯 manifest 文本的函数（哪几个后缀字段非空），不看类实现了什么接口
    // ——否则给类补个接口就会悄悄换掉它的桶、清空用户设置。
    public static string DeriveKind(bool hasImport, bool hasExport)
        => hasImport && hasExport ? KindBoth : hasImport ? KindImport : KindExport;

    // 注册一个 format 条目：内建（LoadBuiltIn）、V1（ExtensionManager 按 manifest 条目）、
    // Compat.Legacy（经 LegacyLoadHook → LegacyCompatLoader 包装老插件）三条路径共用。
    //
    // kind 是条目声明的 type（format / format-import / format-export），进设置桶键的前段。
    // suffixes 是该条目的**身份集**（不可变，路由 + 工程序列化引用 + 拼桶键），跨包可重名；displayName 仅供 UI 展示。
    // import / export 各可为 null（该条目不提供这个方向）；非 null 时带上**该方向实际认的后缀子集**
    //   ——紧凑形态可以读得宽写得窄（如读 mid/midi、只写 midi），子集不参与拼键（不改变实现的身份）。
    // declaresSettings 由调用方【静态判定】入口类是否实现 IExtensionSettings——不在这里靠实例化去探测，
    //   那会把"工厂延迟实例化"变成加载期急切实例化每个格式。
    public static void RegisterFormat(string packageId, string kind, IReadOnlyList<string> suffixes, string displayName,
        (IReadOnlyList<string> Suffixes, Func<IImportFormat> Factory)? import,
        (IReadOnlyList<string> Suffixes, Func<IExportFormat> Factory)? export,
        bool declaresSettings, string className = "")
    {
        var entry = GetOrAddEntry(packageId, kind, EntryId(suffixes), displayName,
            import?.Factory, export?.Factory, declaresSettings, className);

        // 工厂在这里【包一层】：new 出来立即回喂已落盘设置（见 FormatEntry.Configure）。
        // 一处覆盖全部调用点——四个 (De)Serialize 入口都经工厂，不必各自记得调。
        if (import is { } imp)
            foreach (var suffix in imp.Suffixes)
                RegisterImporter(packageId, suffix, displayName, () => entry.Configure(imp.Factory()));

        if (export is { } exp)
            foreach (var suffix in exp.Suffixes)
                RegisterExporter(packageId, suffix, displayName, () => entry.Configure(exp.Factory()));
    }

    // 逐后缀登记（RegisterFormat 内部用）：不同包同扩展名均并存（用户在矩阵选活实现）；
    // 同包同扩展名的同向(import)工厂只留首个。
    static void RegisterImporter(string packageId, string fileExtension, string displayName, Func<IImportFormat> factory)
    {
        var provider = GetOrAddProvider(fileExtension, packageId, displayName);
        if (provider.ImportFactory != null)
        {
            Log.Warning(string.Format("Format importer '{0}' already registered by package '{1}', duplicate ignored.", fileExtension, packageId));
            return;
        }
        provider.ImportFactory = factory;
    }

    static void RegisterExporter(string packageId, string fileExtension, string displayName, Func<IExportFormat> factory)
    {
        var provider = GetOrAddProvider(fileExtension, packageId, displayName);
        if (provider.ExportFactory != null)
        {
            Log.Warning(string.Format("Format exporter '{0}' already registered by package '{1}', duplicate ignored.", fileExtension, packageId));
            return;
        }
        provider.ExportFactory = factory;
    }

    static FormatProvider GetOrAddProvider(string fileExtension, string packageId, string displayName)
    {
        if (!mFormats.TryGetValue(fileExtension, out var list))
        {
            list = new List<FormatProvider>();
            mFormats.Add(fileExtension, list);
        }
        var provider = list.FirstOrDefault(p => p.PackageId == packageId);
        if (provider == null)
        {
            provider = new FormatProvider(packageId, displayName);
            list.Add(provider);
        }
        return provider;
    }

    // UI 展示名（活实现的本地化名）；优先活导入提供者，其次活导出，再次首个提供者；未登记回退到扩展名本身。
    public static string GetDisplayName(string fileExtension)
    {
        var status = ActiveImporter(fileExtension) ?? ActiveExporter(fileExtension)
            ?? (mFormats.TryGetValue(fileExtension, out var list) && list.Count > 0 ? list[0] : null);
        return status != null && !string.IsNullOrEmpty(status.DisplayName) ? status.DisplayName : fileExtension;
    }

    // 提供导入能力的扩展名（去重；多包提供同扩展名仅出现一次）。
    public static IReadOnlyList<string> GetAllImportFormats()
        => mFormats.Keys.Where(ext => mFormats[ext].Any(p => p.ImportFactory != null)).ToArray();

    public static IReadOnlyList<string> GetAllExportFormats()
        => mFormats.Keys.Where(ext => mFormats[ext].Any(p => p.ExportFactory != null)).ToArray();

    // 某扩展名提供该方向能力的全部提供者（packageId + 显示名，按注册序）——供「插件路由」矩阵枚举。
    public static IReadOnlyList<(string PackageId, string DisplayName)> GetImportProviders(string fileExtension)
        => mFormats.TryGetValue(fileExtension, out var list)
            ? list.Where(p => p.ImportFactory != null).Select(p => (p.PackageId, p.DisplayName)).ToArray()
            : Array.Empty<(string, string)>();

    public static IReadOnlyList<(string PackageId, string DisplayName)> GetExportProviders(string fileExtension)
        => mFormats.TryGetValue(fileExtension, out var list)
            ? list.Where(p => p.ExportFactory != null).Select(p => (p.PackageId, p.DisplayName)).ToArray()
            : Array.Empty<(string, string)>();

    public static bool Deserialize(string filePath, [NotNullWhen(true)] out ProjectInfo? projectInfo, [NotNullWhen(false)] out string? error)
    {
        projectInfo = null;
        error = null;

        try
        {
            var fileInfo = new FileInfo(filePath);

            var format = fileInfo.Extension.TrimStart('.');
            var provider = ActiveImporter(format);
            if (provider?.ImportFactory == null)
            {
                throw new Exception(string.Format("Format {0} is not support!", format));
            }

            var stream = File.OpenRead(filePath);
            IImportFormat importFormat = provider.ImportFactory.Invoke();
            projectInfo = importFormat.Deserialize(stream);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    public static bool Serialize(ProjectInfo info, string format, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? error)
    {
        stream = null;
        error = null;

        try
        {
            var provider = ActiveExporter(format);
            if (provider?.ExportFactory == null)
            {
                throw new Exception(string.Format("Format {0} is not support!", format));
            }

            IExportFormat exportFormat = provider.ExportFactory.Invoke();
            // 缓冲进 MemoryStream 再交调用方（其 CopyTo 目标文件）：保留"失败不落半截文件"的原子写语义——
            // 序列化抛错时目标文件尚未开写。插件只管往宿主给的流里写（见 IExportFormat.Serialize 契约）。
            var buffer = new MemoryStream();
            exportFormat.Serialize(buffer, info);
            buffer.Position = 0;
            stream = buffer;
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    // native-aware 打开：装载完整 NativeProjectFile（musical 工程 + 宿主私有 editor/export 元数据）。
    // importer 实现 INativeProjectFormat（native .tlp/.tlpx）时走 native 路径；否则（foreign）退化为普通 Deserialize，
    // 把 ProjectInfo 包进 NativeProjectFile（Editor/Export 默认）。
    // 【异名不重载】：out 参数类型不参与重载决议，若与 musical Deserialize 同名会让 `out var` 调用点二义，故独立命名。
    public static bool DeserializeNative(string filePath, [NotNullWhen(true)] out NativeProjectFile? file, [NotNullWhen(false)] out string? error)
    {
        file = null;
        error = null;

        try
        {
            var fileInfo = new FileInfo(filePath);

            var format = fileInfo.Extension.TrimStart('.');
            var provider = ActiveImporter(format);
            if (provider?.ImportFactory == null)
            {
                throw new Exception(string.Format("Format {0} is not support!", format));
            }

            using var stream = File.OpenRead(filePath);
            IImportFormat importFormat = provider.ImportFactory.Invoke();
            file = importFormat is INativeProjectFormat native
                ? native.DeserializeNative(stream)
                : new NativeProjectFile { Project = importFormat.Deserialize(stream) };
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    // native-aware 保存：把宿主私有的 editor/export 元数据随 musical 工程一并写出。exporter 实现
    // INativeProjectFormat（native .tlp/.tlpx）时走 native 路径；否则（foreign）委托到 musical
    // Serialize(file.Project, ...)——musical Serialize 因此成为 foreign 的兜底实现（非死代码）。
    public static bool SerializeNative(NativeProjectFile file, string format, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? error)
    {
        stream = null;
        error = null;

        try
        {
            var provider = ActiveExporter(format);
            if (provider?.ExportFactory == null)
            {
                throw new Exception(string.Format("Format {0} is not support!", format));
            }

            IExportFormat exportFormat = provider.ExportFactory.Invoke();
            if (exportFormat is not INativeProjectFormat native)
                return Serialize(file.Project, format, out stream, out error);

            // 与 musical 重载同：缓冲进 MemoryStream 保原子写语义（失败不落半截文件）。
            var buffer = new MemoryStream();
            native.SerializeNative(buffer, file);
            buffer.Position = 0;
            stream = buffer;
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    // 该扩展名导入方向的活提供者（在有导入工厂的提供者里解析：用户选中且已装→用它；否则内建优先；再否则 packageId 序最小）。
    static FormatProvider? ActiveImporter(string fileExtension)
    {
        if (!mFormats.TryGetValue(fileExtension, out var list))
            return null;
        var importers = list.Where(p => p.ImportFactory != null).ToArray();
        return ExtensionRouting.ResolveActive(ExtensionRouting.RouteKey("format-import", fileExtension), importers, p => p.PackageId);
    }

    static FormatProvider? ActiveExporter(string fileExtension)
    {
        if (!mFormats.TryGetValue(fileExtension, out var list))
            return null;
        var exporters = list.Where(p => p.ExportFactory != null).ToArray();
        return ExtensionRouting.ResolveActive(ExtensionRouting.RouteKey("format-export", fileExtension), exporters, p => p.PackageId);
    }

    // ── 扩展设置（IExtensionSettings）──

    // 声明了扩展设置的 format 条目（按注册序），供 ExtensionSettingsManager 汇总进设置窗/agent。
    // Settings 是【探测实例】：只用来问 schema 与承接存取值，不参与任何导入导出。
    public static IReadOnlyList<(string PackageId, string Kind, string EntryId, string DisplayName, IExtensionSettings Settings)> GetSettingsEntries()
    {
        var result = new List<(string, string, string, string, IExtensionSettings)>();
        foreach (var entry in mEntries)
        {
            var settings = entry.Probe();
            if (settings != null)
                result.Add((entry.PackageId, entry.Kind, entry.EntryId, entry.DisplayName, settings));
        }
        return result;
    }

    // 同包内是否已有【同一个实现类】的 format 条目、且后缀有交集。
    // 这是「一个条目 = 一个实现类 = 一份设置」这条主张的守卫：把同一个类拆进两个条目，就等于让一份实现
    // 拿两份设置、两份说明、两个详情窗 tab——而作者想表达的"某后缀只读不写"本来就该用 import-suffixes /
    // export-suffixes 在**一个**条目里说。不拦的话这条路会一直通着，等于我们默许了自己不祝福的写法。
    // 后缀不相交则放行：同一个通用实现类服务两种彼此无关的格式，各自一份设置是合理的。
    public static string? FindConflictingEntry(string packageId, string className, IReadOnlyList<string> suffixes)
    {
        if (string.IsNullOrEmpty(className))
            return null;

        foreach (var existing in mEntries)
        {
            if (existing.PackageId != packageId || existing.ClassName != className)
                continue;
            foreach (var suffix in suffixes)
                if (existing.Suffixes.Contains(suffix))
                    return existing.EntryId;
        }
        return null;
    }

    static FormatEntry GetOrAddEntry(string packageId, string kind, string entryId, string displayName,
        Func<IImportFormat>? importFactory, Func<IExportFormat>? exportFactory, bool declaresSettings, string className)
    {
        foreach (var existing in mEntries)
        {
            if (existing.PackageId != packageId || existing.Kind != kind || existing.EntryId != entryId)
                continue;
            // 同包 + 同 type + 同后缀清单 ⇒ 同一个桶键 ⇒ **并进同一条目**，别让一份格式分裂成两个设置桶。
            // 【这条路径是给 legacy 的】：老插件的 importer / exporter 是分两次推来的，两次都是同一个
            // 扩展名、同一个 kind，合成一个条目才对。
            // V1 侧则不该走到这里——两个 manifest 条目撞同一个桶键是作者 bug（要区分方向就用单向 type），
            // 且显示名只能留一个（设置页就一行），故不一致时如实告警。
            if (existing.DisplayName != displayName)
                Log.Warning(string.Format(
                    "Format entry '{0}:{1}' of package '{2}' is declared more than once with different display names ('{3}' and '{4}'); they share one settings bucket and the first name is kept.",
                    kind, entryId, packageId, existing.DisplayName, displayName));
            existing.Absorb(importFactory, exportFactory, declaresSettings);
            return existing;
        }
        var entry = new FormatEntry(packageId, kind, entryId, displayName, importFactory, exportFactory, declaresSettings, className);
        mEntries.Add(entry);
        return entry;
    }

    // 一个 format 条目：一个包的一个格式实现（可认多个后缀别名），是**扩展设置的单位**——
    // 一份实现类只该有一份设置，逐后缀分桶会把同一份值存 N 遍、设置窗也会出现 N 行一模一样的东西。
    sealed class FormatEntry(string packageId, string kind, string entryId, string displayName,
        Func<IImportFormat>? importFactory, Func<IExportFormat>? exportFactory, bool declaresSettings, string className)
    {
        public string PackageId => packageId;
        public string Kind => kind;
        public string EntryId => entryId;
        public string DisplayName => displayName;

        // 实现类全名（内建/legacy 为空）：供「同一个类别拆进两个条目」的守卫比对，见 FindConflictingEntry。
        public string ClassName => className;

        // 本条目的身份集（EntryId 的组成部分，拆开供守卫比对后缀交集）。
        public IReadOnlyList<string> Suffixes => entryId.Split(SuffixSeparator);

        public void Absorb(Func<IImportFormat>? import, Func<IExportFormat>? export, bool settings)
        {
            importFactory ??= import;
            exportFactory ??= export;
            declaresSettings |= settings;
        }

        // 刚 new 出来的实例：立即回喂已落盘设置。format 与三种引擎的结构差异就在这里——引擎注册时即造出长驻
        // 实例、宿主直接对它 ApplySettings；format 注册的是工厂、每次导入导出现 new，取值只能发生在实例化处。
        // 导入导出是用户触发的低频操作，每次多读一次设置文件无所谓。
        public T Configure<T>(T instance)
        {
            if (declaresSettings && instance is IExtensionSettings settings)
                ExtensionSettingsManager.ApplyToFormatInstance(packageId, kind, entryId, displayName, settings);
            return instance;
        }

        // 惰性长驻【探测实例】：schema 得有个实例可问，而 format 没有长驻实例。
        // 任何实例都行——GetSettingsConfig 按 SDK 契约是纯函数、必须在 Init 之前可调、且只依赖传入的 context，
        // 故与"哪一个实例"无关。惰性是为了不破坏工厂延迟实例化：没声明设置的格式一次都不会被造出来。
        public IExtensionSettings? Probe()
        {
            if (!declaresSettings)
                return null;
            if (mProbe == null && !mProbeFailed)
            {
                try
                {
                    // 两个方向可能来自不同实现类，取先命中的那个（导入优先）。
                    mProbe = importFactory?.Invoke() as IExtensionSettings ?? exportFactory?.Invoke() as IExtensionSettings;
                }
                catch (Exception ex)
                {
                    Log.Error(string.Format("Format entry {0}/{1}:{2} failed to instantiate for settings: {3}", packageId, kind, entryId, ex));
                }
                mProbeFailed = mProbe == null;   // 造不出来就不再重试（每开一次设置窗重试一遍毫无意义）
            }
            return mProbe;
        }

        IExtensionSettings? mProbe;
        bool mProbeFailed;
    }

    // 某扩展名的一个包的格式实现：可单提供导入或导出、或两者（同一个类常同时实现两接口）。
    sealed class FormatProvider(string packageId, string displayName)
    {
        public string PackageId { get; } = packageId;
        public string DisplayName { get; } = displayName;
        public Func<IImportFormat>? ImportFactory { get; set; }
        public Func<IExportFormat>? ExportFactory { get; set; }
    }

    // 扩展名 → 该扩展名各包的提供者（按注册序）。多包同扩展名均并存，活实现按 import/export 各自解析。
    static readonly OrderedMap<string, List<FormatProvider>> mFormats = new();

    // 全部 format 条目（按注册序）；一个条目在 mFormats 里对应它每个后缀的一个 FormatProvider。
    static readonly List<FormatEntry> mEntries = new();
}
