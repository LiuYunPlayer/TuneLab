using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Agent;

// agent 模型引擎注册表（与 EffectManager 同范式）：按 [AgentModelEngine] attribute 发现引擎，按 type 登记，
// 引擎在首次被使用时惰性 Init。未注册 / Init 失败的类型由调用方按"该模型不可用"优雅降级。
internal static class AgentModelManager
{
    public static void LoadBuiltIn()
    {
        RegisterFromTypes(Assembly.GetExecutingAssembly().GetTypes());
    }

    public static void Destroy()
    {
        foreach (var state in mEngines.Values)
        {
            if (state.IsInited)
                state.Engine.Destroy();
        }
    }

    // 由 ExtensionManager 在 per-folder ALC 加载后传入已加载类型，扫 [AgentModelEngine] 注册。
    public static void RegisterFromTypes(Type[] types)
    {
        foreach (Type type in types)
        {
            var attribute = type.GetCustomAttribute<AgentModelEngineAttribute>();
            if (attribute == null)
                continue;

            if (!typeof(IAgentModelEngine).IsAssignableFrom(type))
                continue;

            var constructor = type.GetConstructor(Type.EmptyTypes);
            if (constructor != null && !mEngines.ContainsKey(attribute.Type))
                mEngines.Add(attribute.Type, new AgentModelEngineStatus((IAgentModelEngine)constructor.Invoke(null)));
        }
    }

    public static IReadOnlyList<string> GetAllAgentModelEngines() => mEngines.Keys;

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
        [MemberNotNullWhen(true, nameof(Engine))]
        public bool IsInited => mIsInited;

        public AgentModelEngineStatus(IAgentModelEngine engine)
        {
            mEngine = engine;
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
