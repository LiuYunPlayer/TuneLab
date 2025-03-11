using System;
using System.IO;
using System.Text.Json;
using TuneLab.Extensions.Effect;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation.Utils;

namespace TuneLab.Extensions;

internal static class ExtensionManager
{
    public static void LoadExtensions()
    {
        PathManager.MakeSureExist(PathManager.ExtensionsFolder);
        FormatsManager.LoadBuiltIn();
        foreach (var dir in Directory.GetDirectories(PathManager.ExtensionsFolder))
        {
            Load(dir);
        }
        VoiceManager.LoadBuiltIn();
    }

    public static void Destroy()
    {
        VoiceManager.Destroy();
    }

    public static void Load(string path)
    {
        string descriptionPath = Path.Combine(path, "description.json");
        var extensionName = Path.GetFileName(path);
        ExtensionDescription? description = null;
        if (File.Exists(descriptionPath))
        {
            try
            {
                description = JsonSerializer.Deserialize<ExtensionDescription>(File.OpenRead(descriptionPath));
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("Failed to parse description of {0}: {1}", extensionName, ex));
            }
        }

        description ??= new ExtensionDescription() { name = extensionName };

        var extensionInfos = description.extensions;
        if (extensionInfos.IsEmpty())
        {
            extensionInfos = [description];
        }

        foreach (var extensionInfo in extensionInfos)
        {
            if (!extensionInfo.IsPlatformAvailable())
            {
                Log.Warning(string.Format("Failed to load extension {0}: Platform not supported.", extensionName));
                continue;
            }

            if (extensionInfo.type == "format")
            {
                FormatsManager.Load(path, extensionInfo);
            }
            else if (extensionInfo.type == "voice")
            {
                VoiceManager.Load(path, extensionInfo);
            }
            else if (extensionInfo.type == "effect")
            {
                EffectManager.Load(path, extensionInfo);
            }
            else
            {
                FormatsManager.Load(path, extensionInfo);
                VoiceManager.Load(path, extensionInfo);
                EffectManager.Load(path, extensionInfo);
            }
        }
    }
}
