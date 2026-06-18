using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using TuneLab.Foundation;

using TuneLab.SDK;
namespace TuneLab.Extensions.Voices;

internal static class VoicesManager
{
    // 内建声源引擎显式注册（编进宿主、无 description.json）。空引擎(type="")是无声源 part 的回退。
    public static void LoadBuiltIn()
    {
        RegisterEngine(string.Empty, string.Empty, string.Empty, new EmptyVoiceEngine());
    }

    public static void Destroy()
    {
        foreach (var engine in mVoiceEngines.Values)
        {
            if (engine.IsInited)
                engine.Engine.Destroy();
        }
    }

    // 由 ExtensionManager（V1 manifest 驱动）实例化后、或 Compat.Legacy（经 LegacyLoadHook → LegacyCompatLoader）
    // 包装好适配器后注册引擎实例。引擎 Init 无参：插件 DLL 经 Assembly.Location 自定位包目录，无需宿主递路径。
    // type 是不可变身份 id（工程序列化引用）；displayName 仅供 UI 展示、可本地化。内建/先到优先：type 已存在则跳过。
    // packageId 是来源插件包的反向域名 id（内建为空）——供扩展设置按包分桶持久化，避免不同包同 id 引擎设置串味。
    public static void RegisterEngine(string packageId, string type, string displayName, IVoiceEngine engine)
    {
        if (!mVoiceEngines.ContainsKey(type))
            mVoiceEngines.Add(type, new VoiceEngineStatus(engine, displayName, packageId));
    }

    public static IReadOnlyList<string> GetAllVoiceEngines()
    {
        return mVoiceEngines.Keys;
    }

    // UI 展示名（本地化，注册时按当前语言定）；未注册回退到 id 本身。
    public static string GetDisplayName(string type)
        => mVoiceEngines.TryGetValue(type, out var status) && !string.IsNullOrEmpty(status.DisplayName) ? status.DisplayName : type;

    // 来源插件包 id（扩展设置按包分桶用）；内建 / 未注册为空。
    public static string GetPackageId(string type)
        => mVoiceEngines.TryGetValue(type, out var status) ? status.PackageId : string.Empty;

    // 取该 voice 的扩展设置接口（未实现 IExtensionSettings 则 null）；不触发 Init——设置须在 Init 前可编辑。
    // 走 RawEngine（非 Engine：后者在 Init 前返回 null），因 schema/设置须先于 Init 可达。
    public static IExtensionSettings? GetExtensionSettings(string type)
        => mVoiceEngines.TryGetValue(type, out var status) ? status.RawEngine as IExtensionSettings : null;

    public static IReadOnlyOrderedMap<string, VoiceSourceInfo>? GetAllVoiceInfos(string type)
    {
        var engine = GetInitedEngine(type);
        if (engine == null)
            return null;

        return engine.VoiceSourceInfos;
    }

    // 声库目录元数据（无需创建会话）；引擎不可用或 id 未知返回 false。
    public static bool TryGetVoiceInfo(string type, string id, out VoiceSourceInfo info)
    {
        var engine = GetInitedEngine(type);
        if (engine != null && engine.VoiceSourceInfos.TryGetValue(id, out info))
            return true;

        info = default;
        return false;
    }

    // 创建合成会话；引擎不可用或 id 未知时回退空声源会话（行为等价于无声源 part）。
    public static ISynthesisSession CreateSession(string type, string id, ISynthesisContext context)
    {
        var engine = GetInitedEngine(type);
        if (engine != null && engine.VoiceSourceInfos.ContainsKey(id))
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
        // 未经 Init 的引擎实例（仅供读扩展设置 schema/回喂——这些须先于 Init 可达）。
        public IVoiceEngine RawEngine => mVoiceEngine;
        public string DisplayName { get; }
        public string PackageId { get; }
        [MemberNotNullWhen(true, nameof(Engine))]
        public bool IsInited => mIsInited;

        public VoiceEngineStatus(IVoiceEngine engine, string displayName, string packageId)
        {
            mVoiceEngine = engine;
            DisplayName = displayName;
            PackageId = packageId;
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
