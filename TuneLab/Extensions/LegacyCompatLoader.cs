using System;
using System.IO;
using System.Reflection;
using TuneLab.Foundation.Utils;
using TuneLab.SDK.Format;
using TuneLab.SDK.Voice;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Voices;

namespace TuneLab.Extensions;

// Compat.Legacy 接线（主程序对 Compat.Legacy 零编译依赖，运行时反射加载）：
//   启动时反射加载 TuneLab.Hosting.Compat.Legacy.dll，取 LegacyCompatEntry.TryLoad，
//   用 SDK 类型构造注册委托（转发给内建 Format/Voice manager），装到 ExtensionManager.LegacyLoadHook。
//   委托参数全是共享契约类型（IImportFormat/IExportFormat/IVoiceEngine），跨 Default ALC 同一 Type，
//   反射 Invoke 实参精确匹配。dll 不存在 / 入口缺失 / 任何异常 → 优雅降级（不崩主程序，Legacy 包走盲扫 fallback）。
internal static class LegacyCompatLoader
{
    const string CompatAssemblyFile = "TuneLab.Hosting.Compat.Legacy.dll";
    const string EntryTypeName = "TuneLab.Hosting.Compat.Legacy.LegacyCompatEntry";
    const string EntryMethodName = "TryLoad";

    public static void Wire()
    {
        try
        {
            var dllPath = Path.Combine(AppContext.BaseDirectory, CompatAssemblyFile);
            if (!File.Exists(dllPath))
            {
                Log.Info("Legacy compatibility layer not present; legacy plugins will be skipped.");
                return;
            }

            var assembly = Assembly.LoadFrom(dllPath);
            var method = assembly.GetType(EntryTypeName)?.GetMethod(EntryMethodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                Log.Warning("Legacy compatibility layer present but entry point not found; legacy plugins will be skipped.");
                return;
            }

            // 注册委托：把 Compat 推来的 V1 适配器实例转发进内建 manager（工厂复用同一实例，适配器无状态）。
            Action<string, IImportFormat> addImporter = (ext, format) => FormatsManager.RegisterImporter(ext, () => format);
            Action<string, IExportFormat> addExporter = (ext, format) => FormatsManager.RegisterExporter(ext, () => format);
            Action<string, IVoiceEngine, string> addVoiceEngine = (type, engine, enginePath) => VoicesManager.RegisterEngine(type, engine, enginePath);
            Action<string> compatLog = message => Log.Info("[Compat.Legacy] " + message);

            ExtensionManager.LegacyLoadHook = (path, description) =>
            {
                var assemblies = description?.assemblies ?? Array.Empty<string>();
                var result = method.Invoke(null, [path, assemblies, addImporter, addExporter, addVoiceEngine, compatLog]);
                return result is true;
            };

            Log.Info("Legacy compatibility layer wired.");
        }
        catch (Exception ex)
        {
            Log.Error(string.Format("Failed to initialize legacy compatibility layer: {0}", ex));
        }
    }
}
