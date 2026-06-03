using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.Primitives.DataStructures;
using TuneLab.Foundation.Utils;

using TuneLab.SDK.Voice;
using TuneLab.Extensions.Voices;
namespace TuneLab.Extensions.Voices;

internal static class VoicesManager
{
    public static void LoadBuiltIn()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();
        RegisterFromTypes(types, AppDomain.CurrentDomain.BaseDirectory);
    }

    public static void Destroy()
    {
        foreach (var engine in mVoiceEngines.Values)
        {
            if (engine.IsInited)
                engine.Engine.Destroy();
        }
    }

    // 由 ExtensionManager 在加载后传入已加载类型 + 来源目录（供引擎 Init 定位资源），
    // 扫 [VoiceEngine] 注册。manager 不再自行解析 description.json。
    public static void RegisterFromTypes(Type[] types, string path)
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
                        mVoiceEngines.Add(attribute.Type, new VoiceEngineStatus((IVoiceEngine)constructor.Invoke(null), path));
                }
            }
        }
    }

    // 由 Compat.Legacy（经 ExtensionManager.LegacyLoadHook → LegacyCompatLoader）注册已包装好的引擎适配器实例。
    // 老引擎链接老 [VoiceEngine]/IVoiceEngine，扫不出 V1 attribute，故走实例注册而非 RegisterFromTypes 的反射实例化。
    // enginePath = 包目录，Init 时传给老引擎定位声库/模型。内建/V1 优先：type 已存在则跳过。
    public static void RegisterEngine(string type, IVoiceEngine engine, string enginePath)
    {
        if (!mVoiceEngines.ContainsKey(type))
            mVoiceEngines.Add(type, new VoiceEngineStatus(engine, enginePath));
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

    public static IVoiceSource Create(string type, string id)
    {
        var engine = GetInitedEngine(type);
        if (engine == null)
        {
            return mDefaultEngine.CreateVoiceSource(id);
        }

        if (engine.VoiceInfos.ContainsKey(id))
            return engine.CreateVoiceSource(id);
        else
            return Create(string.Empty, string.Empty);
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

        public VoiceEngineStatus(IVoiceEngine engine, string enginePath)
        {
            mVoiceEngine = engine;
            mEnginePath = enginePath;
        }

        public bool Init(out string? error)
        {
            mIsInited = mVoiceEngine.Init(mEnginePath, out error);
            return mIsInited;
        }

        IVoiceEngine mVoiceEngine;
        string mEnginePath;
        bool mIsInited = false;
    }

    static OrderedMap<string, VoiceEngineStatus> mVoiceEngines = new();
#nullable disable
    static IVoiceEngine mDefaultEngine => mVoiceEngines[string.Empty].Engine;
#nullable enable
}
