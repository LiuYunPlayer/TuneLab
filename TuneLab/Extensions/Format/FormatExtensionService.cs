using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Utils;

namespace TuneLab.Extensions.Format;

[FormatExtensionService]
internal class FormatExtensionService : IFormatExtensionService
{
    public IReadOnlyOrderedMap<string, IImportableFormat> ImportableFormats => mImportableFormats;
    public IReadOnlyOrderedMap<string, IExportableFormat> ExportableFormats => mExportableFormats;

    public void Load()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();
        LoadFromTypes(types);
    }

    void LoadFromTypes(Type[] types)
    {
        foreach (Type type in types)
        {
            TryLoadImportableFormat(type);
            TryLoadExportableFormat(type);
        }
    }

    void TryLoadImportableFormat(Type type)
    {
        var importAttribute = type.GetCustomAttribute<ImportableFormatAttribute>();
        if (importAttribute == null)
            return;

        if (!typeof(IImportableFormat).IsAssignableFrom(type))
        {
            Log.Error($"Type {type.Name} does not implement IImportableFormat!");
            return;
        }

        var constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor == null)
        {
            Log.Error($"Type {type.Name} does not have a parameterless constructor!");
            return;
        }

        try
        {
            if (constructor.Invoke(null) is not IImportableFormat importableFormat)
            {
                Log.Error($"Type {type.Name} does not implement IImportableFormat!");
                return;
            }

            mImportableFormats.Add(importAttribute.FileExtension, importableFormat);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load type {type.Name}: {e.Message}");
            return;
        }
    }

    void TryLoadExportableFormat(Type type)
    {
        var exportAttribute = type.GetCustomAttribute<ExportableFormatAttribute>();
        if (exportAttribute == null)
            return;

        if (!typeof(IExportableFormat).IsAssignableFrom(type))
        {
            Log.Error($"Type {type.Name} does not implement IExportableFormat!");
            return;
        }

        var constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor == null)
        {
            Log.Error($"Type {type.Name} does not have a parameterless constructor!");
            return;
        }

        try
        {
            if (constructor.Invoke(null) is not IExportableFormat exportableFormat)
            {
                Log.Error($"Type {type.Name} does not implement IExportableFormat!");
                return;
            }

            mExportableFormats.Add(exportAttribute.FileExtension, exportableFormat);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load type {type.Name}: {e.Message}");
            return;
        }
    }

    readonly OrderedMap<string, IImportableFormat> mImportableFormats = [];
    readonly OrderedMap<string, IExportableFormat> mExportableFormats = [];
}
