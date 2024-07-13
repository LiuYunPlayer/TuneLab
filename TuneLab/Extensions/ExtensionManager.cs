using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
        PathManager.MakeSureExist(PathManager.ExtensionsFolder);
        FormatsManager.LoadBuiltIn();
        foreach (var dir in Directory.GetDirectories(PathManager.ExtensionsFolder))
        {
            Load(dir);
        }
        VoicesManager.LoadBuiltIn();
    }

    public static void Destroy()
    {
        VoicesManager.Destroy();
    }

    public static void Load(string path)
    {
        FormatsManager.Load(path);
        VoicesManager.Load(path);
    }
}
