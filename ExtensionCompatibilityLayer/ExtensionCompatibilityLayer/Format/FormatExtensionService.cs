using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.Format;
using TuneLab.Foundation.DataStructures;
using TuneLab.Core.Environment;
using System.IO;
using System.Reflection;
using System.Text.Json;
using TuneLab.Foundation.Utils;
using TuneLab.Extensions.Formats;

namespace ExtensionCompatibilityLayer.Format;

[FormatExtensionService]
internal class FormatExtensionService : IFormatExtensionService
{
    public IReadOnlyOrderedMap<string, IImportableFormat> ImportableFormats => mImportableFormats;
    public IReadOnlyOrderedMap<string, IExportableFormat> ExportableFormats => mExportableFormats;

    public void Load()
    {
        foreach (var dir in TuneLabContext.Global.ExtensionDirectories)
        {
            Load(dir);
        }
    }

    void Load(string dir)
    {
        string descriptionPath = Path.Combine(dir, "description.json");
        var extensionName = Path.GetFileName(dir);
        ExtensionDescription? description = null;
        if (File.Exists(descriptionPath))
        {
            try
            {
                description = JsonSerializer.Deserialize<ExtensionDescription>(File.OpenRead(descriptionPath));
                if (description != null && !description.IsPlatformAvailable())
                {
                    Log.Warning(string.Format("Failed to load extension {0}: Platform not supported.", extensionName));
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("Failed to parse description of {0}: {1}", extensionName, ex));
                return;
            }
        }

        var assemblies = description == null ? Directory.GetFiles(dir, "*.dll") : description.assemblies.Convert(s => Path.Combine(dir, s));
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

    void LoadFromTypes(Type[] types)
    {
        foreach (Type type in types)
        {
            var importAttribute = type.GetCustomAttribute<ImportFormatAttribute>();
            if (importAttribute != null)
            {
                if (typeof(IImportFormat).IsAssignableFrom(type))
                {
                    var constructor = type.GetConstructor(Type.EmptyTypes);
                    if (constructor == null)
                        continue;

                    var instance = (IImportFormat)constructor.Invoke(null);
                    if (instance == null)
                        continue;

                    if (mImportableFormats.ContainsKey(importAttribute.FileExtension))
                    {
                        Log.Info($"Import format {importAttribute.FileExtension} already exists.");
                        continue;
                    }

                    mImportableFormats.Add(importAttribute.FileExtension, new ImportableFormat(instance));
                }
            }

            var exportAttribute = type.GetCustomAttribute<ExportFormatAttribute>();
            if (exportAttribute != null)
            {
                if (typeof(IExportFormat).IsAssignableFrom(type))
                {
                    var constructor = type.GetConstructor(Type.EmptyTypes);
                    if (constructor == null)
                        continue;

                    var instance = (IExportFormat)constructor.Invoke(null);
                    if (instance == null)
                        continue;

                    if (mExportableFormats.ContainsKey(exportAttribute.FileExtension))
                    {
                        Log.Info($"Export format {exportAttribute.FileExtension} already exists.");
                        continue;
                    }

                    mExportableFormats.Add(exportAttribute.FileExtension, new ExportableFormat(instance));
                }
            }
        }
    }

    readonly OrderedMap<string, IImportableFormat> mImportableFormats = [];
    readonly OrderedMap<string, IExportableFormat> mExportableFormats = [];
}
