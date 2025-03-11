using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Utils;

namespace TuneLab.Extensions.Voices;

internal static class VoiceManager
{
    public static void LoadBuiltIn()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();
        LoadFromTypes(types, AppDomain.CurrentDomain.BaseDirectory);
    }

    public static void Load(string path, ExtensionInfo? description = null)
    {
        var assemblies = description == null ? Directory.GetFiles(path, "*.dll") : description.assemblies.Convert(s => Path.Combine(path, s));
        foreach (var file in assemblies)
        {
            try
            {
                var types = Assembly.LoadFrom(file).GetTypes();
                LoadFromTypes(types, path);
            }
            catch { }
        }
    }

    public static void Destroy()
    {
        foreach (var engine in mVoiceEngineStates.Values)
        {
            if (engine.IsInited)
                engine.Engine.Destroy();
        }

        mVoiceEngineStates.Clear();
    }

    static void LoadFromTypes(Type[] types, string path)
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
                        mVoiceEngineStates.Add(attribute.Type, new VoiceEngineState((IVoiceEngine)constructor.Invoke(null), path));
                }
            }
        }
    }

    public static IReadOnlyList<string> GetAllVoiceEngines()
    {
        return mVoiceEngineStates.Keys;
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
        var state = mVoiceEngineStates[type];
        if (state.IsInited)
            return;

        if (!state.Init(out var error))
            throw new Exception(error);
    }

    static IVoiceEngine? GetInitedEngine(string type)
    {
        if (!mVoiceEngineStates.ContainsKey(type))
            return null;

        var engine = mVoiceEngineStates[type];
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

    class VoiceEngineState
    {
        public IVoiceEngine? Engine => IsInited ? mVoiceEngine : null;
        [MemberNotNullWhen(true, nameof(Engine))]
        public bool IsInited => mIsInited;

        public VoiceEngineState(IVoiceEngine engine, string enginePath)
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

    static OrderedMap<string, VoiceEngineState> mVoiceEngineStates = new();
#nullable disable
    static IVoiceEngine mDefaultEngine => mVoiceEngineStates[string.Empty].Engine;
#nullable enable
}
