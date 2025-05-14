using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Extensions.Synthesizer;
using TuneLab.Extensions.Voice.BuiltIn;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;
using TuneLab.Foundation.Utils;

namespace TuneLab.Extensions.Voice;

internal static class VoiceManager
{
    public static void LoadBuiltIn()
    {
        mVoiceEngineStates.Add(string.Empty, new VoiceEngineState(new EmptyVoiceEngine()));

        var types = Assembly.GetExecutingAssembly().GetTypes();
        LoadFromTypes(types);

        Load(PathManager.ExtensionCompatibilityLayerFolder);
    }

    public static void Load(string path, ExtensionInfo? description = null)
    {
        var assemblies = description == null ? Directory.GetFiles(path, "*.dll") : description.assemblies.Convert(s => Path.Combine(path, s));
        foreach (var file in assemblies)
        {
            try
            {
                using var _ = AssemblyHelper.RegisterAssemblyResolve(path);
                var types = Assembly.LoadFrom(file).GetTypes();
                LoadFromTypes(types);
            }
            catch (Exception e)
            {
                Log.Error($"Failed to load assembly {file} of extension {path}: {e}");
            }
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

    static void LoadFromTypes(Type[] types)
    {
        foreach (Type type in types)
        {
            LoadType(type);
        }
    }

    static void LoadType(Type type)
    {
        var attribute = type.GetCustomAttribute<VoiceExtensionServiceAttribute>();
        if (attribute == null)
            return;

        if (!typeof(IVoiceExtensionService).IsAssignableFrom(type))
            return;

        var constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor == null)
            return;

        var service = (IVoiceExtensionService)constructor.Invoke(null);
        service.Load();
        foreach (var kvp in service.VoiceEngines)
        {
            if (mVoiceEngineStates.ContainsKey(kvp.Key))
            {
                Log.Info($"Voice engine {kvp.Key} already exists.");
                continue;
            }

            mVoiceEngineStates.Add(kvp.Key, new VoiceEngineState(kvp.Value));
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

    public static IVoiceSource Create(string type, IVoiceSynthesisContext context)
    {
        var engine = GetInitedEngine(type);
        if (engine == null)
        {
            return mDefaultEngine.CreateVoiceSource(context);
        }

        if (engine.VoiceInfos.ContainsKey(context.VoiceID))
            return engine.CreateVoiceSource(context);
        else
            return Create(string.Empty, new EmptyVoiceContext());
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

        public VoiceEngineState(IVoiceEngine engine)
        {
            mVoiceEngine = engine;
        }

        public bool Init(out string? error)
        {
            error = null;
            try
            {
                mVoiceEngine.Init([]);
                mIsInited = true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                mIsInited = false;
            }
            return mIsInited;
        }

        IVoiceEngine mVoiceEngine;
        bool mIsInited = false;
    }

    static OrderedMap<string, VoiceEngineState> mVoiceEngineStates = [];
#nullable disable
    static IVoiceEngine mDefaultEngine => mVoiceEngineStates[string.Empty].Engine;
#nullable enable
}
