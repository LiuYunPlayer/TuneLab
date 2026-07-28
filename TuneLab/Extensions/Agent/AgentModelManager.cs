using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TuneLab.Agent;
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

    // 注册一个模型适配器。【唯一调用方是 LoadBuiltIn】——适配器不开放为插件类型，新适配走 PR 编进宿主。
    // 插件那条路走不到这里：manifest 声明的 agent-model 对 ExtensionManager 就是个不认识的 type，
    // 与任何未知 kind 一样被判为「本宿主不支持的插件类型」跳过。
    // 故一个 type 只会有一个实现：同 type 被注册两次是宿主自己的编码错误（两个内建适配器撞了 id），
    // 报错并只留首个——【不】把它当"多包争身份"扔进冲突消解矩阵让用户选，那是给互不知情的第三方包用的。
    // type 是不可变身份 id；displayName 仅供 UI 展示、可本地化。
    // packageId 恒为内建包 id，保留它是因为 provider 设置按 (包, kind:id) 分桶时要用。
    public static void RegisterEngine(string packageId, string type, string displayName, IAgentModelEngine engine)
    {
        if (!mEngines.TryGetValue(type, out var list))
        {
            list = new List<AgentModelEngineStatus>();
            mEngines.Add(type, list);
        }
        if (list.Count > 0)
        {
            Log.Error(string.Format("Agent model engine '{0}' is already registered; duplicate ignored (two built-in adapters share this type id).", type));
            return;
        }
        list.Add(new AgentModelEngineStatus(engine, displayName, packageId));
    }

    public static IReadOnlyList<string> GetAllAgentModelEngines() => mEngines.Keys;

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

    // 该 type 的引擎状态。【不经 ExtensionRouting】：适配器不开放为插件类型、全部编进宿主，故一个 type
    // 永远只有一个实现——没有"多个互不知情的包争同一身份"这回事，也就无需用户裁决。
    // 同 type 被注册两次是宿主自己的编码错误，由 RegisterEngine 处报错并只留首个。
    static AgentModelEngineStatus? ActiveStatus(string type)
        => mEngines.TryGetValue(type, out var list) && list.Count > 0 ? list[0] : null;

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
