using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TuneLab.Extensions;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Effect;

// 效果器引擎注册表（与 VoicesManager 同范式）：身份 id 跨包可重名，多包同 id 均并存，
// 活实现由 ExtensionRoutingStore 按用户选择 / 确定性默认解析。引擎在首次被使用时惰性 Init。
// 未注册 / Init 失败的类型由调用方按 passthrough 优雅降级。
internal static class EffectManager
{
    // 内建无 effect 引擎；effect 全部由外置插件经 manifest 提供。保留方法以对齐各 manager 加载入口。
    public static void LoadBuiltIn()
    {
    }

    public static void Destroy()
    {
        foreach (var list in mEngines.Values)
            foreach (var state in list)
                if (state.IsInited)
                    state.Engine.Destroy();
    }

    // 由 ExtensionManager（V1 manifest 驱动）实例化后注册引擎。type 跨包可重名：不同包同 type 均并存（用户在矩阵选活实现），
    // 同包同 type 只留首个（包内重复属打包错误，warn 后忽略）。
    // type 是不可变身份 id（工程序列化引用）；displayName 仅供 UI 展示、可本地化。
    // packageId 是来源插件包的反向域名 id（内建为 (built-in)）——身份组按它区分各包实现，并供扩展设置按包分桶。
    // 引擎 Init 无参：插件 DLL 经 Assembly.Location 自定位包目录，无需宿主递路径。
    public static void RegisterEngine(string packageId, string type, string displayName, IEffectSynthesisEngine engine)
    {
        if (!mEngines.TryGetValue(type, out var list))
        {
            list = new List<EffectEngineStatus>();
            mEngines.Add(type, list);
        }
        if (list.Any(s => s.PackageId == packageId))
        {
            Log.Warning(string.Format("Effect engine '{0}' already registered by package '{1}', duplicate ignored.", type, packageId));
            return;
        }
        list.Add(new EffectEngineStatus(engine, displayName, packageId));
    }

    public static IReadOnlyList<string> GetAllEffectEngines() => mEngines.Keys;

    // 某身份的全部提供者（packageId + 显示名，按注册序）——供「插件路由」矩阵与扩展设置按包枚举。
    public static IReadOnlyList<(string PackageId, string DisplayName)> GetProviders(string type)
        => mEngines.TryGetValue(type, out var list)
            ? list.Select(s => (s.PackageId, s.DisplayName)).ToArray()
            : Array.Empty<(string, string)>();

    // UI 展示名（活实现的本地化名；注册时按当前语言定）；未注册回退到 id 本身。
    public static string GetDisplayName(string type)
    {
        var status = ActiveStatus(type);
        return status != null && !string.IsNullOrEmpty(status.DisplayName) ? status.DisplayName : type;
    }

    public static bool Exists(string type) => mEngines.ContainsKey(type);

    // 取某【特定包】该 effect 的扩展设置接口（未实现 IExtensionSettings 则 null）；不触发 Init——设置须在 Init 前可编辑。
    public static IExtensionSettings? GetExtensionSettings(string packageId, string type)
    {
        if (!mEngines.TryGetValue(type, out var list))
            return null;
        var status = list.FirstOrDefault(s => s.PackageId == packageId);
        return status?.Engine as IExtensionSettings;
    }

    // 取该身份活实现且已 Init 的引擎；未注册 / Init 失败返回 null（调用方按 passthrough 优雅降级，不崩主程序）。
    public static IEffectSynthesisEngine? GetInitedEngine(string type)
    {
        var engine = ActiveStatus(type);
        if (engine == null)
            return null;

        if (engine.IsInited)
            return engine.Engine;

        if (!engine.Init(out var error))
        {
            Log.Error(string.Format("Effect engine {0} init failed: {1}", type, error));
            return null;
        }

        return engine.IsInited ? engine.Engine : null;
    }

    // 该身份在多包冲突中的活实现状态（用户选中且已装 → 用它；否则内建优先；再否则 packageId 序最小）。
    static EffectEngineStatus? ActiveStatus(string type)
        => mEngines.TryGetValue(type, out var list)
            ? ExtensionRouting.ResolveActive(ExtensionRouting.RouteKey("effect", type), list, s => s.PackageId)
            : null;

    class EffectEngineStatus
    {
        public IEffectSynthesisEngine Engine => mEngine;
        public string DisplayName { get; }
        public string PackageId { get; }
        [MemberNotNullWhen(true, nameof(Engine))]
        public bool IsInited => mIsInited;

        public EffectEngineStatus(IEffectSynthesisEngine engine, string displayName, string packageId)
        {
            mEngine = engine;
            DisplayName = displayName;
            PackageId = packageId;
        }

        // Init 无参、失败抛异常：宿主在调用边界 catch，责任归属靠捕获点判定。
        public bool Init(out string? error)
        {
            try
            {
                mEngine.Init();
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

        readonly IEffectSynthesisEngine mEngine;
        bool mIsInited = false;
    }

    // 身份 id → 该身份各包的提供者（按注册序）。多包同 id 均并存，活实现由 ExtensionRoutingStore 解析。
    static readonly OrderedMap<string, List<EffectEngineStatus>> mEngines = new();
}
