using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using TuneLab.Extensions;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Formats;

// 格式注册表（与各引擎 manager 同范式）：身份 = 文件扩展名，跨包可重名，多包同扩展名均并存，
// 活实现由 ExtensionRoutingStore 解析。【import/export 各算一条可路由身份】（routeKey kind = "format-import"/"format-export"），
// 故一个扩展名可分别为导入、导出选不同包的实现。工厂延迟实例化（与旧行为一致）。
internal static class FormatsManager
{
    // 内建格式显式注册（编进宿主、无 description.json，故不走 manifest，直接登记）。
    public static void LoadBuiltIn()
    {
        var pkg = ExtensionManager.BuiltInPackageId;
        RegisterImporter(pkg, "tlp", "TuneLab Project", () => new TLP.TuneLabProject());
        RegisterExporter(pkg, "tlp", "TuneLab Project", () => new TLP.TuneLabProject());
        RegisterImporter(pkg, "tlpx", "TuneLab Project (CBOR)", () => new TLP.TuneLabProjectCbor());
        RegisterExporter(pkg, "tlpx", "TuneLab Project (CBOR)", () => new TLP.TuneLabProjectCbor());
        RegisterImporter(pkg, "acep", "ACE Studio Project", () => new ACEP.ACEStudioProject());
        RegisterExporter(pkg, "acep", "ACE Studio Project", () => new ACEP.ACEStudioProject());
        RegisterImporter(pkg, "ufdata", "UtaFormatix Data", () => new UFData.UtaFormatixV1Data());
        RegisterExporter(pkg, "ufdata", "UtaFormatix Data", () => new UFData.UtaFormatixV1Data());
        RegisterImporter(pkg, "mid", "MIDI", () => new Midi.MidiWithExtension_mid());
        RegisterImporter(pkg, "midi", "MIDI", () => new Midi.MidiWithExtension_midi());
        RegisterImporter(pkg, "vpr", "VOCALOID Project", () => new VPR.VprWithExtension());
    }

    // 工厂注册导入器：内建（LoadBuiltIn）、V1（ExtensionManager 按 manifest class 实例化）、
    // Compat.Legacy（经 LegacyLoadHook → LegacyCompatLoader 包装老插件）三条路径共用。
    // fileExtension 是不可变身份 id（路由 + 工程序列化引用），跨包可重名；displayName 仅供 UI 展示、可本地化。
    // 不同包同扩展名均并存（用户在矩阵选活实现）；同包同扩展名的同向(import)工厂只留首个。
    public static void RegisterImporter(string packageId, string fileExtension, string displayName, Func<IImportFormat> factory)
    {
        var provider = GetOrAddProvider(fileExtension, packageId, displayName);
        if (provider.ImportFactory != null)
        {
            Log.Warning(string.Format("Format importer '{0}' already registered by package '{1}', duplicate ignored.", fileExtension, packageId));
            return;
        }
        provider.ImportFactory = factory;
    }

    public static void RegisterExporter(string packageId, string fileExtension, string displayName, Func<IExportFormat> factory)
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
            stream = exportFormat.Serialize(info);
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
}
