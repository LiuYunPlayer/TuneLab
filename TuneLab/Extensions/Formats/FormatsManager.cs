using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Base.Structures;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Base.Utils;

namespace TuneLab.Extensions.Formats;

internal static class FormatsManager
{
    public static void LoadBuiltIn()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();
        LoadFromTypes(types);
    }

    public static void Load(string path, ExtensionInfo? description = null)
    {
        var assemblies = description == null ? Directory.GetFiles(path, "*.dll") : description.assemblies.Convert(s => Path.Combine(path, s));
        foreach (var file in assemblies)
        {
            try
            {
                var types = Assembly.LoadFrom(file).GetTypes();
                LoadFromTypes(types);
            }
            catch { }
        }
    }

    static void LoadFromTypes(Type[] types)
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
                        mImportFormats.Add(importAttribute.FileExtension, constructor);
                }
            }

            var exportAttribute = type.GetCustomAttribute<ExportFormatAttribute>();
            if (exportAttribute != null)
            {
                if (typeof(IExportFormat).IsAssignableFrom(type))
                {
                    var constructor = type.GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                        mExportFormats.Add(exportAttribute.FileExtension, constructor);
                }
            }
        }
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
            IImportFormat importFormat = (IImportFormat)mImportFormats[format].Invoke(null);
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
            if (!mImportFormats.ContainsKey(format))
            {
                throw new Exception(string.Format("Format {0} is not support!", format));
            }

            IExportFormat importFormat = (IExportFormat)mExportFormats[format].Invoke(null);
            stream = importFormat.Serialize(info);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    static OrderedMap<string, ConstructorInfo> mImportFormats = new();
    static OrderedMap<string, ConstructorInfo> mExportFormats = new();
}
