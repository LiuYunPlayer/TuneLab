using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Voices;

namespace TuneLab.Extensions;

internal static class ExtensionManager
{
    public static void LoadExtensions()
    {
        PathManager.MakeSure(PathManager.ExtensionsFolder);
        LoadFormats();
        LoadVoices();
    }

    public static void Destroy()
    {
        VoicesManager.Destroy();
    }

    static void LoadFormats()
    {
        FormatsManager.LoadBuiltIn();
        foreach (var dir in Directory.GetDirectories(PathManager.ExtensionsFolder))
        {
            FormatsManager.Load(dir);
        }
    }

    static void LoadVoices()
    {
        VoicesManager.LoadBuiltIn();
        foreach (var dir in Directory.GetDirectories(PathManager.ExtensionsFolder))
        {
            VoicesManager.Load(dir);
        }
    }
}
