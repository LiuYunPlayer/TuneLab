using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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
        mVoiceEngineStates.Add(string.Empty, [new VoiceEngineState(new EmptyVoiceEngine())]);

        var types = Assembly.GetExecutingAssembly().GetTypes();
        LoadFromTypes(types);

        Load(PathManager.ExtensionCompatibilityLayerFolder);
    }

    public static void Load(string path, ExtensionInfo? description = null)
    {
        if (!Directory.Exists(path))
            return;

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
        foreach (var states in mVoiceEngineStates.Values)
        {
            foreach (var state in states)
                if (state.IsInited)
                    state.Engine.Destroy();
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
        foreach (var extension in service.VoiceExtensions)
        {
            var state = new VoiceEngineState(extension.VoiceEngine);
            if (mVoiceEngineStates.TryGetValue(extension.Type, out var states))
            {
                states.Add(state);
                continue;
            }

            mVoiceEngineStates.Add(extension.Type, [state]);
        }
    }

    public static IReadOnlyList<string> GetAllVoiceEngines()
    {
        return mVoiceEngineStates.Keys;
    }

    public static IReadOnlyOrderedMap<string, VoiceSourceInfo> GetAllVoiceInfos(string type)
    {
        return GetInitedEngine(type)?.VoiceInfos ?? [];
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

    public static ObjectConfig GetContextPropertyConfig(string type, IEnumerable<IVoiceSynthesisContext> contexts)
    {
        var engine = GetInitedEngine(type);
        if (engine == null)
            return new ObjectConfig();

        return engine.GetContextPropertyConfig(contexts);
    }

    public static IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfig(string type, IEnumerable<IVoiceSynthesisContext> contexts)
    {
        var engine = GetInitedEngine(type);
        if (engine == null)
            return [];

        return engine.GetAutomationConfigs(contexts);
    }

    public static void InitEngine(string type)
    {
        var state = mVoiceEngineStates[type][0];
        if (state.IsInited)
            return;

        state.Init();
    }

    static IVoiceEngine? GetInitedEngine(string type)
    {
        if (!mVoiceEngineStates.TryGetValue(type, out var states))
            return null;

        var state = states[0];
        if (state.IsInited)
            return state.Engine;

        if (!state.IsInited)
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

        return state.IsInited ? state.Engine : null;
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

        public void Init()
        {
            mVoiceEngine.Init();
            mIsInited = true;
        }

        IVoiceEngine mVoiceEngine;
        bool mIsInited = false;
    }

    static OrderedMap<string, List<VoiceEngineState>> mVoiceEngineStates = [];
#nullable disable
    static IVoiceEngine mDefaultEngine => mVoiceEngineStates[string.Empty][0].Engine;
#nullable enable
}
