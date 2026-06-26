using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TuneLab.Foundation;
using TuneLab.SDK;
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

            Action<string> compatLog = message => Log.Info("[Compat.Legacy] " + message);

            ExtensionManager.LegacyLoadHook = (path, description, typeSink) =>
            {
                // 注册委托按包重建：把 Compat 推来的 V1 适配器转发进内建 manager（工厂复用同一实例，适配器无状态），
                // 同时把真实类别回填进本包的 typeSink，供 sidebar 展示精确类型而非笼统 "Legacy"。
                // Legacy 老插件无独立显示名，显示名沿用身份 id（扩展名 / 引擎 type）。
                // Legacy 包无 V1 反向域名 id（有 id 即走 V1 路径），故用目录名当包 id（与 LoadLegacy 的 LoadResult.Id 同源）——
                // 供冲突消解区分多个 legacy 包、并反查真实包名；其适配器不实现 IExtensionSettings、无设置桶受影响。
                var legacyPackageId = ExtensionManager.LegacyPackageId(path);
                Action<string, IImportFormat> addImporter = (ext, format) => { FormatsManager.RegisterImporter(legacyPackageId, ext, ext, () => format); AddType(typeSink, "format"); };
                Action<string, IExportFormat> addExporter = (ext, format) => { FormatsManager.RegisterExporter(legacyPackageId, ext, ext, () => format); AddType(typeSink, "format"); };
                // enginePath 由 compat 侧的引擎适配器自持（老引擎 Init 需要包路径，新引擎面 Init 无参）。
                Action<string, IVoiceSynthesisEngine, string> addVoiceEngine = (type, engine, enginePath) => { VoicesManager.RegisterEngine(legacyPackageId, type, type, engine); AddType(typeSink, "voice"); };

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

    static void AddType(ICollection<string> sink, string kind)
    {
        if (!sink.Contains(kind))
            sink.Add(kind);
    }
}
