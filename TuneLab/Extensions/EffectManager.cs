using System;
using System.IO;
using System.Reflection;
using System.Text;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Extensions.Synthesizer;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;
using TuneLab.Foundation.Utils;

namespace TuneLab.Extensions.Effect;

internal static class EffectManager
{
    public static void LoadBuiltIn()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();
        LoadFromTypes(types, AppDomain.CurrentDomain.BaseDirectory);
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
                var types = Assembly.LoadFrom(file).GetTypes();
                LoadFromTypes(types, path);
            }
            catch { }
        }
    }

    public static void Destroy()
    {
        foreach (var state in mEffectEngineStates.Values)
        {
            if (state.IsInited)
                state.Engine.Destroy();
        }

        mEffectEngineStates.Clear();
    }

    static void LoadFromTypes(Type[] types, string path)
    {
        foreach (Type type in types)
        {
            var attribute = type.GetCustomAttribute<EffectEngineAttribute>();
            if (attribute != null)
            {
                if (typeof(IEffectEngine).IsAssignableFrom(type))
                {
                    var constructor = type.GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                        mEffectEngineStates.Add(attribute.Type, new EffectEngineState((IEffectEngine)constructor.Invoke(null)));
                }
            }
        }
    }

    class EffectEngineState(IEffectEngine engine)
    {
        public IEffectEngine Engine { get; private set; } = engine;
        public bool IsInited { get; private set; } = false;
        public void Init()
        {
            if (IsInited)
                return;

            IReadOnlyMap<string, IReadOnlyPropertyValue> args = [];
            try
            {
                Engine.Init(args);
                IsInited = true;
            }
            catch (Exception ex)
            {
                Log.Error("Failed to init effect engine: " + ex);
            }
        }
    }

    static OrderedMap<string, EffectEngineState> mEffectEngineStates = [];

    class FallbackEffectEngine() : IEffectEngine
    {
        public ObjectConfig PropertyConfig => new();
        public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfig => [];

        public IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output)
        {
            return new FallbackEffectSynthesisTask(input, output);
        }

        public void Destroy()
        {

        }

        public void Init(IReadOnlyMap<string, IReadOnlyPropertyValue> args)
        {

        }

        class FallbackEffectSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output) : IEffectSynthesisTask
        {
            public event Action<double>? Progress;
            public event Action<SynthesisError?>? Finished;

            public void OnDirtyEvent(EffectDirtyEvent dirtyEvent)
            {

            }

            public void Start()
            {
                output.Audio = input.Audio.Clone();
                Finished?.Invoke(null);
            }

            public void Stop()
            {

            }
        }
    }
}
