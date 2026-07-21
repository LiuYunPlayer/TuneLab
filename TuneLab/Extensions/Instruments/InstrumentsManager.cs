using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TuneLab.Foundation;

using TuneLab.Extensions;
using TuneLab.SDK;
namespace TuneLab.Extensions.Instruments;

// instrument（多声部音源）引擎注册表——镜像 VoicesManager，差异仅 instrument 接口面与路由 kind。
internal static class InstrumentsManager
{
    // 内建空引擎显式注册（编进宿主、无 manifest.json）。空引擎(type="")是无音源 part 的回退。
    public static void LoadBuiltIn()
    {
        RegisterEngine(ExtensionManager.BuiltInPackageId, string.Empty, string.Empty, new EmptyInstrumentSynthesisEngine());
    }

    public static void Destroy()
    {
        foreach (var list in mInstrumentEngines.Values)
            foreach (var engine in list)
                if (engine.IsInited)
                    engine.Engine.Destroy();
    }

    // type 是不可变身份 id（工程序列化引用），【跨包可重名】；displayName 仅供 UI 展示、可本地化。
    // packageId 是来源插件包的反向域名 id（内建为 (built-in)）。
    // 【冲突消解】不同包同 type 均并存登记（用户在矩阵选活实现）；【同包同 type 只留首个】（包内重复属打包错误，warn 后忽略）。
    public static void RegisterEngine(string packageId, string type, string displayName, IInstrumentSynthesisEngine engine)
    {
        if (!mInstrumentEngines.TryGetValue(type, out var list))
        {
            list = new List<InstrumentEngineStatus>();
            mInstrumentEngines.Add(type, list);
        }
        if (list.Any(s => s.PackageId == packageId))
        {
            Log.Warning(string.Format("Instrument engine '{0}' already registered by package '{1}', duplicate ignored.", type, packageId));
            return;
        }
        list.Add(new InstrumentEngineStatus(engine, displayName, packageId));
    }

    // 全部不同身份 id（去重；多包提供同 id 仅出现一次）。
    public static IReadOnlyList<string> GetAllInstrumentEngines() => mInstrumentEngines.Keys;

    // 某身份的全部提供者（packageId + 显示名，按注册序）——供「插件路由」矩阵与扩展设置按包枚举。
    public static IReadOnlyList<(string PackageId, string DisplayName)> GetProviders(string type)
        => mInstrumentEngines.TryGetValue(type, out var list)
            ? list.Select(s => (s.PackageId, s.DisplayName)).ToArray()
            : Array.Empty<(string, string)>();

    // UI 展示名（活实现的本地化名）；未注册回退到 id 本身。
    public static string GetDisplayName(string type)
    {
        var status = ActiveStatus(type);
        return status != null && !string.IsNullOrEmpty(status.DisplayName) ? status.DisplayName : type;
    }

    // 取某【特定包】该 instrument 的扩展设置接口（未实现 IExtensionSettings 则 null）；不触发 Init。
    public static IExtensionSettings? GetExtensionSettings(string packageId, string type)
    {
        if (!mInstrumentEngines.TryGetValue(type, out var list))
            return null;
        var status = list.FirstOrDefault(s => s.PackageId == packageId);
        return status?.RawEngine as IExtensionSettings;
    }

    public static IReadOnlyOrderedMap<string, InstrumentSourceInfo>? GetAllInstrumentInfos(string type)
    {
        var engine = GetInitedEngine(type);
        if (engine == null)
            return null;

        return engine.InstrumentSourceInfos;
    }

    // 音源呈现布局（有序分组树）；引擎不可用返回空列表（宿主据此平铺兜底 = 不分组）。
    public static IReadOnlyList<InstrumentSourceLayoutItem> GetInstrumentLayout(string type)
    {
        var engine = GetInitedEngine(type);
        return engine?.InstrumentSourceLayout ?? [];
    }

    // 音源目录元数据（无需创建会话）；引擎不可用或 id 未知返回 false。
    public static bool TryGetInstrumentInfo(string type, string id, [MaybeNullWhen(false)] out InstrumentSourceInfo info)
    {
        var engine = GetInitedEngine(type);
        if (engine != null && engine.InstrumentSourceInfos.TryGetValue(id, out info))
            return true;

        info = null;
        return false;
    }

    // 创建合成会话；引擎不可用或 id 未知时回退空音源会话（行为等价于无音源 part）。instrumentId 由 context.InstrumentId 承载。
    public static IInstrumentSynthesisSession CreateSession(string type, IInstrumentSynthesisContext context)
    {
        var engine = GetInitedEngine(type);
        if (engine != null && engine.InstrumentSourceInfos.ContainsKey(context.InstrumentId))
        {
            try
            {
                return engine.CreateSession(context);
            }
            catch (Exception ex)
            {
                Log.ErrorAttributed(string.Format("Instrument engine {0} create session failed", type), ex);
            }
        }

        // 空引擎注册于内建加载、Init 恒成功。
        return GetInitedEngine(string.Empty)!.CreateSession(context);
    }

    // —— 声明类 config 求值（不依赖会话实例：宿主在「建会话之前」即可填好声明）；instrumentId 由 context 内各 part 的 InstrumentId 承载。——
    public static IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(string type, IInstrumentSynthesisPartPropertyContext context)
        => Declare(type, InstrumentIdOf(context), e => e.GetAutomationConfigs(context));

    public static IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(string type, IInstrumentSynthesisPartPropertyContext context)
        => Declare(type, InstrumentIdOf(context), e => e.GetSynthesizedParameterConfigs(context));

    public static ObjectConfig GetPartPropertyConfig(string type, IInstrumentSynthesisPartPropertyContext context)
        => Declare(type, InstrumentIdOf(context), e => e.GetPartPropertyConfig(context));

    public static ObjectConfig GetNotePropertyConfig(string type, IInstrumentSynthesisNotePropertyContext context)
        => Declare(type, context.Part.InstrumentId, e => e.GetNotePropertyConfig(context));

    static string InstrumentIdOf(IInstrumentSynthesisPartPropertyContext context) => context.Parts.Count > 0 ? context.Parts[0].InstrumentId : string.Empty;

    static T Declare<T>(string type, string instrumentId, Func<IInstrumentSynthesisEngine, T> get)
    {
        var empty = GetInitedEngine(string.Empty)!;   // 空引擎注册于内建加载、Init 恒成功。
        var engine = GetInitedEngine(type);
        if (engine == null || !engine.InstrumentSourceInfos.ContainsKey(instrumentId))
            engine = empty;

        try
        {
            return get(engine);
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed(string.Format("Instrument engine {0} declaration failed", type), ex);
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
    static IInstrumentSynthesisEngine? GetInitedEngine(string type)
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
                Log.Error(string.Format("Instrument engine {0} init failed: {1}", type, error));
                return null;
            }
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed(string.Format("Instrument engine {0} init failed", type), ex);
            return null;
        }

        return status.IsInited ? status.Engine : null;
    }

    // 该身份在多包冲突中的活实现状态（用户选中且已装 → 用它；否则内建优先；再否则 packageId 序最小）。
    static InstrumentEngineStatus? ActiveStatus(string type)
        => mInstrumentEngines.TryGetValue(type, out var list)
            ? ExtensionRouting.ResolveActive(ExtensionRouting.RouteKey("instrument", type), list, s => s.PackageId)
            : null;

    class InstrumentEngineStatus
    {
        public IInstrumentSynthesisEngine? Engine => IsInited ? mInstrumentEngine : null;
        // 未经 Init 的引擎实例（仅供读扩展设置 schema / 回喂——这些须先于 Init 可达）。
        public IInstrumentSynthesisEngine RawEngine => mInstrumentEngine;
        public string DisplayName { get; }
        public string PackageId { get; }
        [MemberNotNullWhen(true, nameof(Engine))]
        public bool IsInited => mIsInited;

        public InstrumentEngineStatus(IInstrumentSynthesisEngine engine, string displayName, string packageId)
        {
            mInstrumentEngine = engine;
            DisplayName = displayName;
            PackageId = packageId;
        }

        // Init 无参、失败抛异常：宿主在调用边界 catch，责任归属靠捕获点判定。
        public bool Init(out string? error)
        {
            try
            {
                mInstrumentEngine.Init();
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

        IInstrumentSynthesisEngine mInstrumentEngine;
        bool mIsInited = false;
    }

    // 身份 id → 该身份各包的提供者（按注册序）。多包同 id 均并存，活实现由 ExtensionRouting 解析。
    static OrderedMap<string, List<InstrumentEngineStatus>> mInstrumentEngines = new();
}
