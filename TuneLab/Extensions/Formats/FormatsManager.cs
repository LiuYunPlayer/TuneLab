using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Format.DataInfo;
using TuneLab.Foundation.Utils;

using TuneLab.SDK.Format;
using TuneLab.Extensions.Formats;
namespace TuneLab.Extensions.Formats;

internal static class FormatsManager
{
    public static void LoadBuiltIn()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();
        RegisterFromTypes(types);
    }

    // 由 ExtensionManager 在加载（V1 走 per-folder ALC、Legacy 走 fallback）后传入已加载类型，
    // 扫 [ImportFormat]/[ExportFormat] 注册。manager 不再自行解析 description.json（话题#10）。
    public static void RegisterFromTypes(Type[] types)
    {
        foreach (Type type in types)
        {
            var importAttribute = type.GetCustomAttribute<ImportFormatAttribute>();
            if (importAttribute != null)
            {
                if (typeof(IImportFormat).IsAssignableFrom(type))
                {
                    var constructor = type.GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                        mImportFormats.Add(importAttribute.FileExtension, () => (IImportFormat)constructor.Invoke(null));
                }
            }

            var exportAttribute = type.GetCustomAttribute<ExportFormatAttribute>();
            if (exportAttribute != null)
            {
                if (typeof(IExportFormat).IsAssignableFrom(type))
                {
                    var constructor = type.GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                        mExportFormats.Add(exportAttribute.FileExtension, () => (IExportFormat)constructor.Invoke(null));
                }
            }
        }
    }

    // 由 Compat.Legacy（经 ExtensionManager.LegacyLoadHook → LegacyCompatLoader）注册已包装好的适配器实例（话题#9）。
    // 老插件链接老 attribute/接口，扫不出 V1 attribute，故走实例/工厂注册而非 RegisterFromTypes 的反射实例化。
    // 内建/V1 优先：扩展名已存在则跳过（不让 Legacy 覆盖内建格式）。
    public static void RegisterImporter(string fileExtension, Func<IImportFormat> factory)
    {
        if (!mImportFormats.ContainsKey(fileExtension))
            mImportFormats.Add(fileExtension, factory);
    }

    public static void RegisterExporter(string fileExtension, Func<IExportFormat> factory)
    {
        if (!mExportFormats.ContainsKey(fileExtension))
            mExportFormats.Add(fileExtension, factory);
    }

    public static IReadOnlyList<string> GetAllImportFormats()
    {
        return mImportFormats.Keys;
    }

    public static IReadOnlyList<string> GetAllExportFormats()
    {
        return mExportFormats.Keys;
    }

    public static bool Deserialize(string filePath, [NotNullWhen(true)] out ProjectInfo? projectInfo, [NotNullWhen(false)] out string? error)
    {
        projectInfo = null;
        error = null;
       
        try
        {
            var fileInfo = new FileInfo(filePath);

            var format = fileInfo.Extension.TrimStart('.');
            if (!mImportFormats.ContainsKey(format))
            {
                throw new Exception(string.Format("Format {0} is not support!", format));
            }

            var stream = File.OpenRead(filePath);
            IImportFormat importFormat = mImportFormats[format].Invoke();
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
            if (!mExportFormats.ContainsKey(format))
            {
                throw new Exception(string.Format("Format {0} is not support!", format));
            }

            IExportFormat exportFormat = mExportFormats[format].Invoke();
            stream = exportFormat.Serialize(info);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    static OrderedMap<string, Func<IImportFormat>> mImportFormats = new();
    static OrderedMap<string, Func<IExportFormat>> mExportFormats = new();
}
