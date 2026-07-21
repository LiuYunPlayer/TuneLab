using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TuneLab.Foundation;

using TuneLab.Extensions;
using TuneLab.SDK;
namespace TuneLab.Extensions.Voices;

internal static class VoicesManager
{
    // 内建声源引擎显式注册（编进宿主、无 manifest.json）。空引擎(type="")是无声源 part 的回退。
    public static void LoadBuiltIn()
    {
        RegisterEngine(ExtensionManager.BuiltInPackageId, string.Empty, string.Empty, new EmptyVoiceSynthesisEngine());
    }

    public static void Destroy()
    {
        foreach (var list in mVoiceEngines.Values)
            foreach (var engine in list)
                if (engine.IsInited)
                    engine.Engine.Destroy();
    }

    // 由 ExtensionManager（V1 manifest 驱动）实例化后、或 Compat.Legacy（经 LegacyLoadHook → LegacyCompatLoader）
    // 包装好适配器后注册引擎实例。引擎 Init 无参：插件 DLL 经 Assembly.Location 自定位包目录，无需宿主递路径。
    // type 是不可变身份 id（工程序列化引用），【跨包可重名】；displayName 仅供 UI 展示、可本地化。
    // packageId 是来源插件包的反向域名 id（内建为 (built-in)）——身份组按它区分各包实现，并供扩展设置按包分桶。
    // 【冲突消解】不同包同 type 均并存登记（用户在矩阵选活实现）；【同包同 type 只留首个】（包内重复实现属打包错误，warn 后忽略）。
    public static void RegisterEngine(string packageId, string type, string displayName, IVoiceSynthesisEngine engine)
    {
        if (!mVoiceEngines.TryGetValue(type, out var list))
        {
            list = new List<VoiceEngineStatus>();
            mVoiceEngines.Add(type, list);
        }
        if (list.Any(s => s.PackageId == packageId))
        {
            Log.Warning(string.Format("Voice engine '{0}' already registered by package '{1}', duplicate ignored.", type, packageId));
            return;
        }
        list.Add(new VoiceEngineStatus(engine, displayName, packageId));
    }

    // 全部不同身份 id（去重；多包提供同 id 仅出现一次）。
    public static IReadOnlyList<string> GetAllVoiceEngines() => mVoiceEngines.Keys;

    // 某身份的全部提供者（packageId + 显示名，按注册序）——供「插件路由」矩阵与扩展设置按包枚举。
    public static IReadOnlyList<(string PackageId, string DisplayName)> GetProviders(string type)
        => mVoiceEngines.TryGetValue(type, out var list)
            ? list.Select(s => (s.PackageId, s.DisplayName)).ToArray()
            : Array.Empty<(string, string)>();

    // UI 展示名（活实现的本地化名；注册时按当前语言定）；未注册回退到 id 本身。
    public static string GetDisplayName(string type)
    {
        var status = ActiveStatus(type);
        return status != null && !string.IsNullOrEmpty(status.DisplayName) ? status.DisplayName : type;
    }

    // 取某【特定包】该 voice 的扩展设置接口（未实现 IExtensionSettings 则 null）；不触发 Init——设置须在 Init 前可编辑。
    // 走 RawEngine（非 Engine：后者在 Init 前返回 null），因 schema/设置须先于 Init 可达。
    public static IExtensionSettings? GetExtensionSettings(string packageId, string type)
    {
        if (!mVoiceEngines.TryGetValue(type, out var list))
            return null;
        var status = list.FirstOrDefault(s => s.PackageId == packageId);
        return status?.RawEngine as IExtensionSettings;
    }

    public static IReadOnlyOrderedMap<string, VoiceSourceInfo>? GetAllVoiceInfos(string type)
    {
        var engine = GetInitedEngine(type);
        if (engine == null)
            return null;

        return engine.VoiceSourceInfos;
    }

    // 声库目录元数据（无需创建会话）；引擎不可用或 id 未知返回 false。
    public static bool TryGetVoiceInfo(string type, string id, [MaybeNullWhen(false)] out VoiceSourceInfo info)
    {
        var engine = GetInitedEngine(type);
        if (engine != null && engine.VoiceSourceInfos.TryGetValue(id, out info))
            return true;

        info = null;
        return false;
    }

    // 创建合成会话；引擎不可用或 id 未知时回退空声源会话（行为等价于无声源 part）。voiceId 由 context.VoiceId 承载。
    public static IVoiceSynthesisSession CreateSession(string type, IVoiceSynthesisContext context)
    {
        var engine = GetInitedEngine(type);
        if (engine != null && engine.VoiceSourceInfos.ContainsKey(context.VoiceId))
        {
            try
            {
                return engine.CreateSession(context);
            }
            catch (Exception ex)
            {
                Log.ErrorAttributed(string.Format("Engine {0} create session failed", type), ex);
            }
        }

        // 空引擎注册于内建加载、Init 恒成功。
        return GetInitedEngine(string.Empty)!.CreateSession(context);
    }

    // —— 声明类 config 求值（不依赖会话实例：宿主在「建会话之前」即可填好声明，故无构造期时序陷阱）——
    // 引擎不可用 / id 未知 → 回退空引擎；插件求值抛异常 → 记日志并回退空引擎结果（声明每次参数 commit 都调，
    // 不能让一个烂实现拖垮 UI）。voiceId 由 context 内各 part 的 VoiceId 承载（与 part 真值同为纯函数输入）。
    public static IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(string type, IVoiceSynthesisPartPropertyContext context)
        => Declare(type, VoiceIdOf(context), e => e.GetAutomationConfigs(context));

    public static IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(string type, IVoiceSynthesisPartPropertyContext context)
        => Declare(type, VoiceIdOf(context), e => e.GetSynthesizedParameterConfigs(context));

    public static ObjectConfig GetPartPropertyConfig(string type, IVoiceSynthesisPartPropertyContext context)
        => Declare(type, VoiceIdOf(context), e => e.GetPartPropertyConfig(context));

    public static ObjectConfig GetNotePropertyConfig(string type, IVoiceSynthesisNotePropertyContext context)
        => Declare(type, context.Part.VoiceId, e => e.GetNotePropertyConfig(context));

    public static IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(string type, IVoiceSynthesisNotePropertyContext context)
        => Declare(type, context.Part.VoiceId, e => e.GetPhonemePropertyConfigs(context));

    // 路由 / 校验用声库 id：多选 part 取首个（phase A 各调用点恒单 part；跨引擎多选由上游不调声明拦下）。
    static string VoiceIdOf(IVoiceSynthesisPartPropertyContext context) => context.Parts.Count > 0 ? context.Parts[0].VoiceId : string.Empty;

    static T Declare<T>(string type, string voiceId, Func<IVoiceSynthesisEngine, T> get)
    {
        var empty = GetInitedEngine(string.Empty)!;   // 空引擎注册于内建加载、Init 恒成功。
        var engine = GetInitedEngine(type);
        if (engine == null || !engine.VoiceSourceInfos.ContainsKey(voiceId))
            engine = empty;

        try
        {
            return get(engine);
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed(string.Format("Engine {0} declaration failed", type), ex);
            return get(empty);
        }
    }

    // Init 该身份的【活实现】（用户选择 / 确定性默认解析出的那个包的引擎）。
    public static void InitEngine(string type)
    {
        var status = ActiveStatus(type);
        if (status == null || status.IsInited)
            return;

        if (!status.Init(out var error))
            throw new Exception(error);
    }

    // 该身份当前活实现的引擎实例（按用户选择 / 确定性默认解析），惰性 Init；未注册 / Init 失败返回 null。
    static IVoiceSynthesisEngine? GetInitedEngine(string type)
    {
        var status = ActiveStatus(type);
        if (status == null)
            return null;

        if (status.IsInited)
            return status.Engine;

        try
        {
            if (!status.Init(out var error))
            {
                Log.Error(string.Format("Engine {0} init failed: {1}", type, error));
                return null;
            }
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed(string.Format("Engine {0} init failed", type), ex);
            return null;
        }

        return status.IsInited ? status.Engine : null;
    }

    // 该身份在多包冲突中的活实现状态（用户选中且已装 → 用它；否则内建优先；再否则 packageId 序最小）。
    static VoiceEngineStatus? ActiveStatus(string type)
        => mVoiceEngines.TryGetValue(type, out var list)
            ? ExtensionRouting.ResolveActive(ExtensionRouting.RouteKey("voice", type), list, s => s.PackageId)
            : null;

    class VoiceEngineStatus
    {
        public IVoiceSynthesisEngine? Engine => IsInited ? mVoiceEngine : null;
        // 未经 Init 的引擎实例（仅供读扩展设置 schema/回喂——这些须先于 Init 可达）。
        public IVoiceSynthesisEngine RawEngine => mVoiceEngine;
        public string DisplayName { get; }
        public string PackageId { get; }
        [MemberNotNullWhen(true, nameof(Engine))]
        public bool IsInited => mIsInited;

        public VoiceEngineStatus(IVoiceSynthesisEngine engine, string displayName, string packageId)
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

        IVoiceSynthesisEngine mVoiceEngine;
        bool mIsInited = false;
    }

    // 身份 id → 该身份各包的提供者（按注册序）。多包同 id 均并存，活实现由 ExtensionRoutingStore 解析。
    static OrderedMap<string, List<VoiceEngineStatus>> mVoiceEngines = new();
}
