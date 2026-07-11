using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TuneLab.Extensions;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Agent;

// agent 模型引擎注册表（与 EffectManager 同范式）：身份 id 跨包可重名，多包同 id 均并存，
// 活实现由 ExtensionRoutingStore 按用户选择 / 确定性默认解析。引擎在首次被使用时惰性 Init。
// 未注册 / Init 失败的类型由调用方按"该模型不可用"优雅降级。
internal static class AgentModelManager
{
    // 内建 agent 模型引擎显式注册（编进宿主、无 manifest.json）。openai-compatible 为开箱即用的参考适配器。
    public static void LoadBuiltIn()
    {
        RegisterEngine(ExtensionManager.BuiltInPackageId, "openai-compatible", "OpenAI Compatible", new TuneLab.Agent.Models.OpenAICompatibleEngine());
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
    // type 是不可变身份 id；displayName 仅供 UI 展示、可本地化。
    // packageId 是来源插件包的反向域名 id（内建为 (built-in)）——身份组按它区分各包实现，并供 provider 设置按包分桶。
    public static void RegisterEngine(string packageId, string type, string displayName, IAgentModelEngine engine)
    {
        if (!mEngines.TryGetValue(type, out var list))
        {
            list = new List<AgentModelEngineStatus>();
            mEngines.Add(type, list);
        }
        if (list.Any(s => s.PackageId == packageId))
        {
            Log.Warning(string.Format("Agent model engine '{0}' already registered by package '{1}', duplicate ignored.", type, packageId));
            return;
        }
        list.Add(new AgentModelEngineStatus(engine, displayName, packageId));
    }

    public static IReadOnlyList<string> GetAllAgentModelEngines() => mEngines.Keys;

    // 某身份的全部提供者（packageId + 显示名，按注册序）——供「插件路由」矩阵枚举。
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

    // 该身份当前活实现的来源包 id（多包冲突时按用户选择 / 确定性默认解析）——provider 设置按包分桶用；未注册为空。
    public static string GetActivePackageId(string type) => ActiveStatus(type)?.PackageId ?? string.Empty;

    // 取该身份活实现且已 Init 的引擎；未注册 / Init 失败返回 null（调用方据此提示"该模型不可用"，不崩主程序）。
    public static IAgentModelEngine? GetInitedEngine(string type)
    {
        var engine = ActiveStatus(type);
        if (engine == null)
            return null;

        if (engine.IsInited)
            return engine.Engine;

        if (!engine.Init(out var error))
        {
            Log.Error(string.Format("Agent model engine {0} init failed: {1}", type, error));
            return null;
        }

        return engine.IsInited ? engine.Engine : null;
    }

    // 该身份在多包冲突中的活实现状态（用户选中且已装 → 用它；否则内建优先；再否则 packageId 序最小）。
    static AgentModelEngineStatus? ActiveStatus(string type)
        => mEngines.TryGetValue(type, out var list)
            ? ExtensionRouting.ResolveActive(ExtensionRouting.RouteKey("agent-model", type), list, s => s.PackageId)
            : null;

    class AgentModelEngineStatus
    {
        public IAgentModelEngine Engine => mEngine;
        public string DisplayName { get; }
        public string PackageId { get; }
        [MemberNotNullWhen(true, nameof(Engine))]
        public bool IsInited => mIsInited;

        public AgentModelEngineStatus(IAgentModelEngine engine, string displayName, string packageId)
        {
            mEngine = engine;
            DisplayName = displayName;
            PackageId = packageId;
        }

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

        readonly IAgentModelEngine mEngine;
        bool mIsInited = false;
    }

    // 身份 id → 该身份各包的提供者（按注册序）。多包同 id 均并存，活实现由 ExtensionRoutingStore 解析。
    static readonly OrderedMap<string, List<AgentModelEngineStatus>> mEngines = new();
}
