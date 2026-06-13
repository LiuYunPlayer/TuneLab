using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Effect;

// 效果器引擎注册表（与 VoicesManager 同范式）：按 [EffectEngine] attribute 发现引擎，按 type 登记，
// 引擎在首次被使用时惰性 Init。未注册/Init 失败的类型由调用方按 passthrough 优雅降级。
internal static class EffectManager
{
    public static void LoadBuiltIn()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();
        RegisterFromTypes(types, AppDomain.CurrentDomain.BaseDirectory);
    }

    public static void Destroy()
    {
        foreach (var state in mEngines.Values)
        {
            if (state.IsInited)
                state.Engine.Destroy();
        }
    }

    // 由 ExtensionManager 在 per-folder ALC 加载后传入已加载类型 + 来源目录（供引擎 Init 定位模型资源）。
    public static void RegisterFromTypes(Type[] types, string path)
    {
        foreach (Type type in types)
        {
            var attribute = type.GetCustomAttribute<EffectEngineAttribute>();
            if (attribute == null)
                continue;

            if (!typeof(IEffectEngine).IsAssignableFrom(type))
                continue;

            var constructor = type.GetConstructor(Type.EmptyTypes);
            if (constructor != null && !mEngines.ContainsKey(attribute.Type))
                mEngines.Add(attribute.Type, new EffectEngineStatus((IEffectEngine)constructor.Invoke(null), path));
        }
    }

    public static IReadOnlyList<string> GetAllEffectEngines() => mEngines.Keys;

    public static bool Exists(string type) => mEngines.ContainsKey(type);

    // 取已 Init 的引擎；未注册 / Init 失败返回 null（调用方按 passthrough 优雅降级，不崩主程序）。
    public static IEffectEngine? GetInitedEngine(string type)
    {
        if (!mEngines.TryGetValue(type, out var engine))
            return null;

        if (engine.IsInited)
            return engine.Engine;

        try
        {
            if (!engine.Init(out var error))
            {
                Log.Error(string.Format("Effect engine {0} init failed: {1}", type, error));
                return null;
            }
        }
        catch (Exception ex)
        {
            Log.Error(string.Format("Effect engine {0} init failed: {1}", type, ex));
            return null;
        }

        return engine.IsInited ? engine.Engine : null;
    }

    class EffectEngineStatus
    {
        public IEffectEngine Engine => mEngine;
        [MemberNotNullWhen(true, nameof(Engine))]
        public bool IsInited => mIsInited;

        public EffectEngineStatus(IEffectEngine engine, string enginePath)
        {
            mEngine = engine;
            mEnginePath = enginePath;
        }

        public bool Init(out string? error)
        {
            mIsInited = mEngine.Init(mEnginePath, out error);
            return mIsInited;
        }

        readonly IEffectEngine mEngine;
        readonly string mEnginePath;
        bool mIsInited = false;
    }

    static readonly OrderedMap<string, EffectEngineStatus> mEngines = new();
}
