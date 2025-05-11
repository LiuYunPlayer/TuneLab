using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using TuneLab.Core.DataInfo;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Utils;

namespace TuneLab.Extensions.Format;

internal static class FormatManager
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
            var importAttribute = type.GetCustomAttribute<ImportableFormatAttribute>();
            if (importAttribute != null)
            {
                if (typeof(IImportableFormat).IsAssignableFrom(type))
                {
                    var constructor = type.GetConstructor(Type.EmptyTypes);
                    if (constructor == null)
                    {
                        Log.Error($"Type {type.Name} does not have a parameterless constructor!");
                        continue;
                    }

                    try
                    {
                        if (constructor.Invoke(null) is not IImportableFormat importableFormat)
                        {
                            Log.Error($"Type {type.Name} does not implement IImportableFormat!");
                            continue;
                        }

                        mImportableFormats.Add(importableFormat.FileExtension, importableFormat);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Failed to load type {type.Name}: {e.Message}");
                        continue;
                    }
                }
            }

            var exportAttribute = type.GetCustomAttribute<ExportableFormatAttribute>();
            if (exportAttribute != null)
            {
                if (typeof(IExportableFormat).IsAssignableFrom(type))
                {
                    var constructor = type.GetConstructor(Type.EmptyTypes);
                    if (constructor == null)
                    {
                        Log.Error($"Type {type.Name} does not have a parameterless constructor!");
                        continue;
                    }

                    try
                    {
                        if (constructor.Invoke(null) is not IExportableFormat exportableFormat)
                        {
                            Log.Error($"Type {type.Name} does not implement IExportableFormat!");
                            continue;
                        }

                        mExportableFormats.Add(exportableFormat.FileExtension, exportableFormat);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Failed to load type {type.Name}: {e.Message}");
                        continue;
                    }
                }
            }
        }
    }

    public static IReadOnlyList<string> GetAllImportFormats()
    {
        return mImportableFormats.Keys;
    }

    public static IReadOnlyList<string> GetAllExportFormats()
    {
        return mExportableFormats.Keys;
    }

    public static bool Deserialize(string filePath, [NotNullWhen(true)] out ProjectInfo? projectInfo, [NotNullWhen(false)] out string? error)
    {
        projectInfo = null;
        error = null;

        try
        {
            var fileInfo = new FileInfo(filePath);

            var format = fileInfo.Extension.TrimStart('.');
            if (!mImportableFormats.ContainsKey(format))
            {
                throw new Exception(string.Format("Format {0} is not support!", format));
            }

            var stream = File.OpenRead(filePath);
            IImportableFormat importFormat = mImportableFormats[format];
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
            if (!mImportableFormats.ContainsKey(format))
            {
                throw new Exception(string.Format("Format {0} is not support!", format));
            }

            IExportableFormat importFormat = mExportableFormats[format];
            stream = importFormat.Serialize(info);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    static OrderedMap<string, IImportableFormat> mImportableFormats = new();
    static OrderedMap<string, IExportableFormat> mExportableFormats = new();
}
