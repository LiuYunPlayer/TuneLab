using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Reflection;
using TuneLab.Foundation;

using TuneLab.SDK;
namespace TuneLab.Extensions.Voices;

internal static class VoicesManager
{
    public static void LoadBuiltIn()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();
        RegisterFromTypes(types);
    }

    public static void Destroy()
    {
        foreach (var engine in mVoiceEngines.Values)
        {
            if (engine.IsInited)
                engine.Engine.Destroy();
        }
    }

    // 由 ExtensionManager 在加载后传入已加载类型，扫 [VoiceEngine] 注册。
    // 引擎 Init 无参：插件 DLL 经 Assembly.Location 自定位包目录，无需宿主递路径。
    public static void RegisterFromTypes(Type[] types)
    {
        foreach (Type type in types)
        {
            var attribute = type.GetCustomAttribute<VoiceEngineAttribute>();
            if (attribute != null)
            {
                if (typeof(IVoiceEngine).IsAssignableFrom(type))
                {
                    var constructor = type.GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                        mVoiceEngines.Add(attribute.Type, new VoiceEngineStatus((IVoiceEngine)constructor.Invoke(null)));
                }
            }
        }
    }

    // 由 Compat.Legacy（经 ExtensionManager.LegacyLoadHook → LegacyCompatLoader）注册已包装好的引擎适配器实例。
    // 老引擎链接老 [VoiceEngine]/IVoiceEngine，扫不出 V1 attribute，故走实例注册而非 RegisterFromTypes 的反射实例化。
    // 内建/V1 优先：type 已存在则跳过。
    public static void RegisterEngine(string type, IVoiceEngine engine)
    {
        if (!mVoiceEngines.ContainsKey(type))
            mVoiceEngines.Add(type, new VoiceEngineStatus(engine));
    }

    public static IReadOnlyList<string> GetAllVoiceEngines()
    {
        return mVoiceEngines.Keys;
    }

    public static IReadOnlyOrderedMap<string, VoiceSourceInfo>? GetAllVoiceInfos(string type)
    {
        var engine = GetInitedEngine(type);
        if (engine == null)
            return null;

        return engine.VoiceInfos;
    }

    // 声库目录元数据（无需创建会话）；引擎不可用或 id 未知返回 false。
    public static bool TryGetVoiceInfo(string type, string id, out VoiceSourceInfo info)
    {
        var engine = GetInitedEngine(type);
        if (engine != null && engine.VoiceInfos.TryGetValue(id, out info))
            return true;

        info = default;
        return false;
    }

    // 创建合成会话；引擎不可用或 id 未知时回退空声源会话（行为等价于无声源 part）。
    public static ISynthesisSession CreateSession(string type, string id, ISynthesisContext context)
    {
        var engine = GetInitedEngine(type);
        if (engine != null && engine.VoiceInfos.ContainsKey(id))
        {
            try
            {
                return engine.CreateSession(id, context);
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("Engine {0} create session failed: {1}", type, ex));
            }
        }

        // 空引擎注册于内建加载、Init 恒成功。
        return GetInitedEngine(string.Empty)!.CreateSession(string.Empty, context);
    }

    public static void InitEngine(string type)
    {
        var engine = mVoiceEngines[type];
        if (engine.IsInited)
            return;

        if (!engine.Init(out var error))
            throw new Exception(error);
    }

    static IVoiceEngine? GetInitedEngine(string type)
    {
        if (!mVoiceEngines.ContainsKey(type))
            return null;

        var engine = mVoiceEngines[type];
        if (engine.IsInited)
            return engine.Engine;

        if (!engine.IsInited)
        {
            try
            {
                InitEngine(type);
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("Engine {0} init failed: {1}", type, ex));
                return null;
            }
        }

        return engine.IsInited ? engine.Engine : null;
    }

    class VoiceEngineStatus
    {
        public IVoiceEngine? Engine => IsInited ? mVoiceEngine : null;
        [MemberNotNullWhen(true, nameof(Engine))]
        public bool IsInited => mIsInited;

        public VoiceEngineStatus(IVoiceEngine engine)
        {
            mVoiceEngine = engine;
        }

        // Init 无参、失败抛异常：宿主在调用边界 catch，责任归属靠捕获点判定。
        public bool Init(out string? error)
        {
            try
            {
                mVoiceEngine.Init();
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

        IVoiceEngine mVoiceEngine;
        bool mIsInited = false;
    }

    static OrderedMap<string, VoiceEngineStatus> mVoiceEngines = new();
}
