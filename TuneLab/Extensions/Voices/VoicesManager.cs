using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Base.Structures;
using TuneLab.Utils;

namespace TuneLab.Extensions.Voices;

internal static class VoicesManager
{
    public static void LoadBuiltIn()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();
        LoadFromTypes(types, AppDomain.CurrentDomain.BaseDirectory);
    }

    public static void Load(string path)
    {
        string descriptionPath = Path.Combine(path, "description.json");
        var extensionName = Path.GetFileName(path);
        ExtensionDescription? description = null;
        if (File.Exists(descriptionPath))
        {
            try
            {
                description = JsonSerializer.Deserialize<ExtensionDescription>(File.OpenRead(descriptionPath));
                if (description != null && !description.IsPlatformAvailable())
                {
                    Log.Warning(string.Format("Failed to load extension {0}: Platform not supported.", extensionName));
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("Failed to parse description of {0}: {1}", extensionName, ex));
                return;
            }
        }

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

        if (GetInitedEngine(string.Empty) == null)
            throw new Exception("Default engine init failed!");
    }

    public static void Destroy()
    {
        foreach (var engine in mVoiceEngines.Values)
        {
            if (engine.IsInited)
                engine.Engine.Destroy();
        }
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
                        mVoiceEngines.Add(attribute.Type, new VoiceEngineStatus((IVoiceEngine)constructor.Invoke(null), path));
                }
            }
        }
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

    static IVoiceEngine? GetInitedEngine(string type)
    {
        if (!mVoiceEngines.ContainsKey(type))
            return null;

        var engine = mVoiceEngines[type];
        if (engine.IsInited)
            return engine.Engine;

        if (!engine.IsInited)
        {
            bool success = engine.Init(out string? error);
            if (!success)
            {
                Log.Error(string.Format("Engine {0} init failed: {1}", type, error));
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
