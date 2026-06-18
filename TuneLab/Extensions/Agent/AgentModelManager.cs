using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Agent;

// agent 模型引擎注册表（与 EffectManager 同范式）：按 [AgentModelEngine] attribute 发现引擎，按 type 登记，
// 引擎在首次被使用时惰性 Init。未注册 / Init 失败的类型由调用方按"该模型不可用"优雅降级。
internal static class AgentModelManager
{
    // 内建 agent 模型引擎显式注册（编进宿主、无 description.json）。openai-compatible 为开箱即用的参考适配器。
    public static void LoadBuiltIn()
    {
        RegisterEngine(ExtensionManager.BuiltInPackageId, "openai-compatible", "OpenAI Compatible", new TuneLab.Agent.Models.OpenAICompatibleEngine());
    }

    public static void Destroy()
    {
        foreach (var state in mEngines.Values)
        {
            if (state.IsInited)
                state.Engine.Destroy();
        }
    }

    // 由 ExtensionManager（V1 manifest 驱动）实例化后注册引擎。type 已存在则跳过（内建/先到优先）。
    // type 是不可变身份 id；displayName 仅供 UI 展示、可本地化。
    // packageId 是来源插件包的反向域名 id（内建为空）——供 provider 设置按包分桶持久化，避免不同包同 id 引擎设置串味。
    public static void RegisterEngine(string packageId, string type, string displayName, IAgentModelEngine engine)
    {
        if (!mEngines.ContainsKey(type))
            mEngines.Add(type, new AgentModelEngineStatus(engine, displayName, packageId));
    }

    public static IReadOnlyList<string> GetAllAgentModelEngines() => mEngines.Keys;

    // UI 展示名（本地化，注册时按当前语言定）；未注册回退到 id 本身。
    public static string GetDisplayName(string type)
        => mEngines.TryGetValue(type, out var status) && !string.IsNullOrEmpty(status.DisplayName) ? status.DisplayName : type;

    // 来源插件包 id（provider 设置按包分桶用）；内建 / 未注册为空。
    public static string GetPackageId(string type)
        => mEngines.TryGetValue(type, out var status) ? status.PackageId : string.Empty;

    public static bool Exists(string type) => mEngines.ContainsKey(type);

    // 取已 Init 的引擎；未注册 / Init 失败返回 null（调用方据此提示"该模型不可用"，不崩主程序）。
    public static IAgentModelEngine? GetInitedEngine(string type)
    {
        if (!mEngines.TryGetValue(type, out var engine))
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

    static readonly OrderedMap<string, AgentModelEngineStatus> mEngines = new();
}
