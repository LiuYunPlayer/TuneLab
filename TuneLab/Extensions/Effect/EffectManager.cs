﻿using Avalonia.Media.TextFormatting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using TuneLab.Base.Utils;
using TuneLab.Synthesizer;

namespace TuneLab.Extensions.Effect;

internal static class EffectManager
{
    public static void LoadBuiltIn()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();
        LoadFromTypes(types, AppDomain.CurrentDomain.BaseDirectory);
    }

    public static void Load(string path, ExtensionDescription? description = null)
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

            PropertyObject args = PropertyObject.Empty;
            try
            {
                Engine.Initialize(args);
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
        public string PropertyConfig => string.Empty;
        public string AutomationConfig => string.Empty;

        public IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output)
        {
            return new FallbackEffectSynthesisTask(input, output);
        }

        public void Destroy()
        {
            
        }

        public void Initialize(PropertyObject args)
        {
            
        }

        class FallbackEffectSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output) : IEffectSynthesisTask
        {
            public event Action<double>? Progress;
            public event Action<SynthesisException?>? Finished;

            public void SetDirty(string context, string key)
            {
                throw new NotImplementedException();
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
