using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Core.Environment;
using TuneLab.Extensions.Voice;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Utils;

namespace ExtensionCompatibilityLayer.Voice;

internal class VoiceExtensionService : IVoiceExtensionService
{
    public IReadOnlyOrderedMap<string, IVoiceEngine> VoiceEngines => mVoiceEngines;

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
                LoadFromTypes(types, dir);
            }
            catch { }
        }
    }

    void LoadFromTypes(Type[] types, string path)
    {
        foreach (Type type in types)
        {
            var attribute = type.GetCustomAttribute<TuneLab.Extensions.Voices.VoiceEngineAttribute>();
            if (attribute == null)
                 continue;

            if (!typeof(TuneLab.Extensions.Voices.IVoiceEngine).IsAssignableFrom(type))
                continue;

            var constructor = type.GetConstructor(Type.EmptyTypes);
            if (constructor == null)
                continue;

            var instance = (TuneLab.Extensions.Voices.IVoiceEngine)constructor.Invoke(null);
            if (instance == null)
                continue;

            if (mVoiceEngines.ContainsKey(attribute.Type))
            {
                Log.Info($"Voice engine {attribute.Type} already exists.");
                continue;
            }

            mVoiceEngines.Add(attribute.Type, new VoiceEngine(instance, path));
        }
    }

    readonly OrderedMap<string, IVoiceEngine> mVoiceEngines = [];
}
