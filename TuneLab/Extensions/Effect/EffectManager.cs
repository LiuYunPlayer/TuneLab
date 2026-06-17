using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Effect;

// 效果器引擎注册表（与 VoicesManager 同范式）：按 [EffectEngine] attribute 发现引擎，按 type 登记，
// 引擎在首次被使用时惰性 Init。未注册/Init 失败的类型由调用方按 passthrough 优雅降级。
internal static class EffectManager
{
    // 内建无 effect 引擎；effect 全部由外置插件经 manifest 提供。保留方法以对齐各 manager 加载入口。
    public static void LoadBuiltIn()
    {
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
    // type 是不可变身份 id（工程序列化引用）；displayName 仅供 UI 展示、可本地化。
    // 引擎 Init 无参：插件 DLL 经 Assembly.Location 自定位包目录，无需宿主递路径。
    public static void RegisterEngine(string type, string displayName, IEffectEngine engine)
    {
        if (!mEngines.ContainsKey(type))
            mEngines.Add(type, new EffectEngineStatus(engine, displayName));
    }

    public static IReadOnlyList<string> GetAllEffectEngines() => mEngines.Keys;

    // UI 展示名（本地化，注册时按当前语言定）；未注册回退到 id 本身。
    public static string GetDisplayName(string type)
        => mEngines.TryGetValue(type, out var status) && !string.IsNullOrEmpty(status.DisplayName) ? status.DisplayName : type;

    public static bool Exists(string type) => mEngines.ContainsKey(type);

    // 取已 Init 的引擎；未注册 / Init 失败返回 null（调用方按 passthrough 优雅降级，不崩主程序）。
    public static IEffectEngine? GetInitedEngine(string type)
    {
        if (!mEngines.TryGetValue(type, out var engine))
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

    class EffectEngineStatus
    {
        public IEffectEngine Engine => mEngine;
        public string DisplayName { get; }
        [MemberNotNullWhen(true, nameof(Engine))]
        public bool IsInited => mIsInited;

        public EffectEngineStatus(IEffectEngine engine, string displayName)
        {
            mEngine = engine;
            DisplayName = displayName;
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

        readonly IEffectEngine mEngine;
        bool mIsInited = false;
    }

    static readonly OrderedMap<string, EffectEngineStatus> mEngines = new();
}
