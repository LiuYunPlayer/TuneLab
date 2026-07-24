using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TuneLab.Foundation;

using TuneLab.Extensions;
using TuneLab.SDK;
namespace TuneLab.Extensions.Derivers;

// deriver（一次性、音频驱动的派生）引擎注册表——镜像 InstrumentsManager 的多包并存 / 冲突路由 / 惰性 Init 骨架，
// 但远更精简：deriver 是按需一次性动作，无音源目录、无常驻 session。故不设「空引擎回退」——空引擎对 voice/instrument
// 是「part 无音源」的序列化回退，而 deriver 无 per-part 绑定：无引擎时 picker 为空、右键项不出现，调用方按 null 处理。
//
// 参数面板 config（GetPropertyConfig）用未 Init 的引擎实例读（对话框呈现参数时不加载模型）；
// Init 只在真正 Derive 前触发（GetInitedEngine）。「能产什么」无静态声明——唯一真相是运行时产物。
internal static class DeriversManager
{
    public static void Destroy()
    {
        foreach (var list in mDeriverEngines.Values)
            foreach (var engine in list)
                if (engine.IsInited)
                    engine.Engine.Destroy();
    }

    // type 是不可变身份 id（对话框 / 路由引用），【跨包可重名】；displayName 仅供 UI 展示、可本地化。
    // packageId 是来源插件包的反向域名 id。【冲突消解】不同包同 type 均并存登记；【同包同 type 只留首个】（warn 后忽略）。
    public static void RegisterEngine(string packageId, string type, string displayName, IAudioDerivationEngine engine)
    {
        if (!mDeriverEngines.TryGetValue(type, out var list))
        {
            list = new List<DeriverEngineStatus>();
            mDeriverEngines.Add(type, list);
        }
        if (list.Any(s => s.PackageId == packageId))
        {
            Log.Warning(string.Format("Deriver engine '{0}' already registered by package '{1}', duplicate ignored.", type, packageId));
            return;
        }
        list.Add(new DeriverEngineStatus(engine, displayName, packageId));
    }

    // 全部不同身份 id（去重；多包提供同 id 仅出现一次）。
    public static IReadOnlyList<string> GetAllDeriverEngines() => mDeriverEngines.Keys;

    // 某身份的全部提供者（packageId + 显示名，按注册序）——供「插件路由」矩阵与扩展设置按包枚举。
    public static IReadOnlyList<(string PackageId, string DisplayName)> GetProviders(string type)
        => mDeriverEngines.TryGetValue(type, out var list)
            ? list.Select(s => (s.PackageId, s.DisplayName)).ToArray()
            : Array.Empty<(string, string)>();

    // UI 展示名（活实现的本地化名）；未注册回退到 id 本身。
    public static string GetDisplayName(string type)
    {
        var status = ActiveStatus(type);
        return status != null && !string.IsNullOrEmpty(status.DisplayName) ? status.DisplayName : type;
    }

    // 取某【特定包】该 deriver 的扩展设置接口（未实现 IExtensionSettings 则 null）；不触发 Init。
    public static IExtensionSettings? GetExtensionSettings(string packageId, string type)
    {
        if (!mDeriverEngines.TryGetValue(type, out var list))
            return null;
        var status = list.FirstOrDefault(s => s.PackageId == packageId);
        return status?.RawEngine as IExtensionSettings;
    }

    // 参数面板 config（反应式、不触发 Init）：对话框在用户改值时按当前情境重算并 diff。读活实现的未 Init 引擎实例
    // （引擎须保证 GetPropertyConfig 不依赖 Init，见 IAudioDerivationEngine）。
    public static ObjectConfig GetPropertyConfig(string type, IAudioDerivationContext context)
    {
        var status = ActiveStatus(type);
        if (status == null)
            return mEmptyConfig;
        try
        {
            return status.RawEngine.GetPropertyConfig(context);
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed(string.Format("Deriver engine {0} GetPropertyConfig failed", type), ex);
            return mEmptyConfig;
        }
    }

    // 取该身份【活实现】的已 Init 引擎（真正跑 Derive 用）；未注册 / Init 失败返回 null。
    public static IAudioDerivationEngine? GetInitedEngine(string type)
    {
        var status = ActiveStatus(type);
        if (status == null)
            return null;

        if (status.IsInited)
            return status.Engine;

        try
        {
            if (!status.Init(out var error))
            {
                Log.Error(string.Format("Deriver engine {0} init failed: {1}", type, error));
                return null;
            }
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed(string.Format("Deriver engine {0} init failed", type), ex);
            return null;
        }

        return status.IsInited ? status.Engine : null;
    }

    // 该身份活实现的来源包 id（供缓存键取该包 manifest version）；未注册返回 null。
    public static string? GetActivePackageId(string type) => ActiveStatus(type)?.PackageId;

    // 该身份当前活实现的状态（用户选中且已装 → 用它；否则内建优先；再否则 packageId 序最小）。
    static DeriverEngineStatus? ActiveStatus(string type)
        => mDeriverEngines.TryGetValue(type, out var list)
            ? ExtensionRouting.ResolveActive(ExtensionRouting.RouteKey("deriver", type), list, s => s.PackageId)
            : null;

    class DeriverEngineStatus
    {
        public IAudioDerivationEngine? Engine => IsInited ? mDeriverEngine : null;
        // 未经 Init 的引擎实例（供读声明面 / 扩展设置 schema——这些须先于 Init 可达）。
        public IAudioDerivationEngine RawEngine => mDeriverEngine;
        public string DisplayName { get; }
        public string PackageId { get; }
        [MemberNotNullWhen(true, nameof(Engine))]
        public bool IsInited => mIsInited;

        public DeriverEngineStatus(IAudioDerivationEngine engine, string displayName, string packageId)
        {
            mDeriverEngine = engine;
            DisplayName = displayName;
            PackageId = packageId;
        }

        // Init 无参、失败抛异常：宿主在调用边界 catch，责任归属靠捕获点判定。
        public bool Init(out string? error)
        {
            try
            {
                mDeriverEngine.Init();
                mIsInited = true;
                error = null;
            }
            catch (Exception ex)
            {
                mIsInited = false;
                error = ex.ToString();
            }
            return mIsInited;
        }

        IAudioDerivationEngine mDeriverEngine;
        bool mIsInited = false;
    }

    // 身份 id → 该身份各包的提供者（按注册序）。多包同 id 均并存，活实现由 ExtensionRouting 解析。
    static OrderedMap<string, List<DeriverEngineStatus>> mDeriverEngines = new();

    static readonly ObjectConfig mEmptyConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
}
