using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text;
using TuneLab.Extensions.Adapters.Voice;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Extensions.Synthesizer;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;
using TuneLab.Foundation.Utils;
using TuneLab.SDK.Voice;

namespace TuneLab.Extensions.Voice;

internal static class VoiceManager
{
    public static void LoadBuiltIn()
    {
        mVoiceEngineStates.Add(string.Empty, new VoiceEngineState(new BuiltInVoiceEngine()));

        var types = Assembly.GetExecutingAssembly().GetTypes();
        LoadFromTypes(types);
    }

    public static void Load(string path, ExtensionInfo? description = null)
    {
        var assemblies = description == null ? Directory.GetFiles(path, "*.dll") : description.assemblies.Convert(s => Path.Combine(path, s));
        foreach (var file in assemblies)
        {
            try
            {
                var types = Assembly.LoadFrom(file).GetTypes();
                LoadFromTypes(types);
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

    static void LoadFromTypes(Type[] types)
    {
        foreach (Type type in types)
        {
            LoadV1(type);
        }
    }

    static void LoadV1(Type type)
    {
        var attribute = type.GetCustomAttribute<VoiceExtensionService_V1Attribute>();
        if (attribute == null)
            return;

        if (!typeof(IVoiceExtensionService_V1).IsAssignableFrom(type))
            return;

        var constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor == null)
            return;

        var service = (IVoiceExtensionService_V1)constructor.Invoke(null);
        service.Load();
        foreach (var kvp in service.VoiceEngines)
        {
            mVoiceEngineStates.Add(kvp.Key, new VoiceEngineState(kvp.Value.ToDomain()));
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

    class BuiltInVoiceEngine : IVoiceEngine
    {
        public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceInfos { get; } = new OrderedMap<string, VoiceSourceInfo>() { { string.Empty, new VoiceSourceInfo() { Name = "Empty Voice" } } };

        public IVoiceSource CreateVoiceSource(IVoiceSynthesisContext context)
        {
            return new EmptyVoiceSource();
        }

        public void Destroy()
        {
            
        }

        public void Init(IReadOnlyMap<string, IReadOnlyPropertyValue> properties)
        {
            
        }

        class EmptyVoiceSource : IVoiceSource
        {
            public string DefaultLyric { get; } = "a";
            public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; } = [];
            public ObjectConfig PropertyConfig { get; } = new();

            public IVoiceSynthesisSegment CreateSegment(IVoiceSynthesisInput input, IVoiceSynthesisOutput output)
            {
                return new EmptyVoiceSynthesisSegment(input, output);
            }

            public ObjectConfig GetNotePropertyConfig(IEnumerable<ISynthesisNote> notes)
            {
                return PropertyConfig;
            }

            public IReadOnlyList<IReadOnlyList<ISynthesisNote>> Segment(IEnumerable<ISynthesisNote> notes)
            {
                return this.SimpleSegment(notes);
            }

            class EmptyVoiceSynthesisSegment(IVoiceSynthesisInput input, IVoiceSynthesisOutput output) : IVoiceSynthesisSegment
            {
                public event Action<double>? Progress;
                public event Action<SynthesisError?>? Finished;

                public void OnDirtyEvent(VoiceDirtyEvent dirtyEvent)
                {
                    
                }

                public void StartSynthesis()
                {
                    Finished?.Invoke(null);
                }

                public void StopSynthesis()
                {
                    
                }
            }
        }
    }

    class EmptyVoiceContext : IVoiceSynthesisContext
    {
        public string VoiceID => string.Empty;
        public IReadOnlyMap<string, IReadOnlyPropertyValue> Properties => [];
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
