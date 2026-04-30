using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Voices;
using TuneLab.Utils;

namespace TuneLab.Extensions;

internal static class ExtensionManager
{
    public static IReadOnlyList<string> PendingUninstalls => mPendingUninstalls;
    public static bool RestartAfterUninstall { get; set; }

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

    public static void AddPendingUninstall(string extensionDirPath)
    {
        if (!mPendingUninstalls.Contains(extensionDirPath))
            mPendingUninstalls.Add(extensionDirPath);
    }

    public static void LaunchPendingUninstalls()
    {
        if (mPendingUninstalls.Count == 0)
            return;

        string installer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "ExtensionInstaller.exe"
            : "ExtensionInstaller";
        var installerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, installer);
        List<string> args = [];
        if (RestartAfterUninstall)
            args.Add("-restart");
        args.Add("-uninstall");
        args.AddRange(mPendingUninstalls);
        ProcessHelper.CreateProcess(installerPath, args);
        mPendingUninstalls.Clear();
    }

    static readonly List<string> mPendingUninstalls = [];
}
