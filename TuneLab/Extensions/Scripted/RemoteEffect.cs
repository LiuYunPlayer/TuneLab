using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Scripted;

// 脚本 effect 引擎的进程内代理（设计文档 §5.5 effect 家族）：EffectManager/EffectGraph 眼里
// 它与 in-proc IEffectSynthesisEngine 不可分辨。反向拉取/发布走共享 RemotePullBus（M2b 落地时
// 自建路由，M3c 起并入总线——对 effect 而言 wire 语义零变化）。
//
// 正向（宿主→sidecar）：engine.init/destroy、engine.create_session、effect.get_*（声明，缓存+有界）、
// effect.process（无超时——应答即完成；取消经通知尽力）。
internal sealed class RemoteEffectEngine : IEffectSynthesisEngine, IExtensionSettings
{
    readonly SidecarHost mHost;
    readonly string mEngineId;
    readonly bool mHasSettings;
    readonly ConcurrentDictionary<string, RemoteEffectSession> mSessions = new();
    int mSessionCounter;

    // 声明面缓存（§3.5）：数据线程只吃 250ms 有界调用；miss 后台补发，旧值兜底。
    const int DeclarationTimeoutMs = 250;
    (string Fingerprint, JsonNode? Result)? mPropCfgCache;
    (string Fingerprint, JsonNode? Result)? mAutoCfgCache;
    (string Fingerprint, JsonNode? Result)? mSynthCfgCache;

    public RemoteEffectEngine(SidecarHost host, string engineId, bool hasSettings)
    {
        mHost = host; mEngineId = engineId; mHasSettings = hasSettings;
    }

    public void Init()
        => mHost.CallAsync("engine.init", new JsonObject { ["engine"] = mEngineId }, Timeout.InfiniteTimeSpan)
            .GetAwaiter().GetResult();

    public void Destroy()
    {
        try { mHost.Notify("engine.destroy", new JsonObject { ["engine"] = mEngineId }); } catch { }
    }

    public IEffectSynthesisSession CreateSession(IEffectSynthesisContext context)
    {
        var key = $"effect:{mEngineId}#{Interlocked.Increment(ref mSessionCounter)}";
        var session = new RemoteEffectSession(this, context, key);
        mSessions[key] = session;
        RemotePullBus.Register(mHost, session);
        session.CreateHandshake = mHost.CallAsync("engine.create_session",
                new JsonObject { ["engine"] = mEngineId, ["session"] = key })
            .ContinueWith(t => { if (t.Exception != null) session.CreateFault = t.Exception.GetBaseException(); });
        return session;
    }

    internal void DropSession(string key)
    {
        mSessions.TryRemove(key, out _);
        RemotePullBus.Unregister(mHost, key);
    }

    // ── 声明面 ──

    public ObjectConfig GetPropertyConfig(IEffectSynthesisPropertyContext context)
    {
        var values = DeclarationArgs(context);
        return CachedDecl(ref mPropCfgCache, "effect.get_property_config", values) is { } node
            ? ScriptedConfigWire.ReadConfig(node["config"]) as ObjectConfig ?? EmptyObject()
            : EmptyObject();
    }

    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IEffectSynthesisPropertyContext context)
    {
        var node = CachedDecl(ref mAutoCfgCache, "effect.get_automation_configs", DeclarationArgs(context));
        return ScriptedConfigWire.ReadAutomationMap(node?["tracks"], "automations");
    }

    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context)
    {
        var node = CachedDecl(ref mSynthCfgCache, "effect.get_synthesized_parameter_configs", DeclarationArgs(context));
        return ScriptedConfigWire.ReadAutomationMap(node?["tracks"], "synthesized_parameters");
    }

    static JsonObject DeclarationArgs(IEffectSynthesisPropertyContext context)
    {
        // 只带声明所需的数据：各 effect 实例的当前参数值（求值器是活视图，不可跨线也不该跨）。
        var effects = new JsonArray();
        foreach (var view in context.Effects)
            effects.Add(ScriptedWire.SerializeNode(view.Properties));
        return new JsonObject { ["effects"] = effects };
    }

    JsonNode? CachedDecl(ref (string Fingerprint, JsonNode? Result)? cache, string method, JsonObject extra)
    {
        var payload = new JsonObject { ["engine"] = mEngineId };
        foreach (var kv in extra)
            payload[kv.Key] = kv.Value?.DeepClone();
        var fingerprint = payload.ToJsonString();
        if (mHost.TryCallRunning(method, payload, TimeSpan.FromMilliseconds(DeclarationTimeoutMs), out var result, out _))
        {
            cache = (fingerprint, result);
            return result;
        }
        if (cache?.Fingerprint == fingerprint)
            return cache.Value.Result;
        return cache?.Result;
    }

    static ObjectConfig EmptyObject() => ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());

    // ── 设置面（§5.7；key 用 engine id）──

    ObjectConfig IExtensionSettings.GetSettingsConfig(IExtensionSettingsContext context)
    {
        if (!mHasSettings)
            return EmptyObject();
        var reply = mHost.CallAsync("settings.get_config", new JsonObject { ["entry"] = mEngineId })
            .GetAwaiter().GetResult();
        return reply?["config"] is { } cfg ? (ObjectConfig)ScriptedConfigWire.ReadConfig(cfg) : EmptyObject();
    }

    void IExtensionSettings.ApplySettings(PropertyObject settings)
    {
        if (!mHasSettings)
            return;
        mHost.SetSettings(mEngineId, ScriptedWire.SerializeNode(settings));
    }

    // ── 会话 ──

    sealed class RemoteEffectSession : IEffectSynthesisSession, IPullSession
    {
        readonly RemoteEffectEngine mEngine;
        readonly IEffectSynthesisContext mContext;
        readonly SidecarHost mHost;
        SynchronizationContext? mSync;
        volatile IReadOnlyList<SynthesisStatusSegment> mStatus = Array.Empty<SynthesisStatusSegment>();
        volatile IReadOnlyMap<string, SynthesizedParameter> mParameters = new Map<string, SynthesizedParameter>();
        int mBusy;
        bool mDisposed;

        public string SessionKey { get; }
        public Exception? CreateFault;
        public Task CreateHandshake = Task.CompletedTask;

        readonly ActionEvent mStatusChanged = new();
        readonly ActionEvent mParametersChanged = new();
        public IActionEvent StatusChanged => mStatusChanged;
        public IActionEvent SynthesizedParametersChanged => mParametersChanged;

        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mParameters;
        public IReadOnlyList<SynthesisStatusSegment> Status => mStatus;

        SynchronizationContext? IPullSession.Sync => mSync;

        public RemoteEffectSession(RemoteEffectEngine engine, IEffectSynthesisContext context, string key)
        {
            mEngine = engine; mContext = context; SessionKey = key; mHost = engine.mHost;
            mSync = SynchronizationContext.Current;
        }

        public async Task Process(CancellationToken cancellation)
        {
            if (mDisposed) throw new ObjectDisposedException(nameof(RemoteEffectSession));
            if (Interlocked.CompareExchange(ref mBusy, 1, 0) != 0)
                throw new InvalidOperationException("effect session already processing");
            mSync = SynchronizationContext.Current ?? mSync;
            try
            {
                await CreateHandshake.ConfigureAwait(false);
                if (CreateFault != null)
                    throw CreateFault;
                var geometry = mContext.Input;
                var request = new JsonObject
                {
                    ["engine"] = mEngine.mEngineId,
                    ["session"] = SessionKey,
                    ["input"] = new JsonObject
                    {
                        ["sample_offset"] = geometry.SampleOffset,
                        ["sample_count"] = geometry.SampleCount,
                        ["sample_rate"] = geometry.SampleRate,
                    },
                };
                using var reg = cancellation.CanBeCanceled
                    ? cancellation.Register(() =>
                    {
                        try { mHost.Notify("effect.cancel", new JsonObject { ["engine"] = mEngine.mEngineId, ["session"] = SessionKey }); } catch { }
                    })
                    : default;
                await mHost.CallAsync("effect.process", request, Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref mBusy, 0);
            }
        }

        public void Dispose()
        {
            if (mDisposed) return;
            mDisposed = true;
            RemotePullBus.DisposeOwnedSegments(mHost, SessionKey);   // 先于解注册（marshal 需要会话在场）
            mEngine.DropSession(SessionKey);
            try { mHost.Notify("session.dispose", new JsonObject { ["engine"] = mEngine.mEngineId, ["session"] = SessionKey }); } catch { }
        }

        // ── IPullSession（总线 handler 已经 marshal 回本会话捕获的数据线程；此处直接触活视图）──

        bool IPullSession.TryInputGeometry(out long offset, out int count, out int rate)
        {
            var input = mContext.Input;
            offset = input.SampleOffset; count = input.SampleCount; rate = input.SampleRate;
            return true;
        }

        bool IPullSession.TryReadInput(int offset, int count, out float[] samples)
        {
            var input = mContext.Input;
            if (offset < 0 || count < 0 || offset + count > input.SampleCount)
            {
                samples = [];
                return false;
            }
            samples = new float[count];
            input.Read(offset, samples);
            return true;
        }

        PropertyObject IPullSession.PullProperties(IReadOnlyList<string>? keys)
        {
            var map = new Map<string, PropertyValue>();
            if (keys != null)
                foreach (var k in keys)
                    map[k] = mContext.Properties.GetValue(k, default);
            return new PropertyObject(map);
        }

        bool IPullSession.TryEvaluateTrack(string track, IReadOnlyList<double> positions, out double[] results)
        {
            if (!mContext.Automations.TryGetValue(track, out var automation))
            {
                results = [];
                return false;
            }
            results = new double[positions.Count];
            automation.Evaluate(positions, results);
            return true;
        }

        IReadOnlyList<string> IPullSession.AutomationTracks() => mContext.Automations.Keys.ToList();

        IAudioSegment IPullSession.CreateOutputSegment(long sampleOffset, int sampleCount, int sampleRate)
            => mContext.CreateAudioSegment(sampleOffset, sampleCount, sampleRate);

        void IPullSession.PublishStatus(IReadOnlyList<SynthesisStatusSegment> segments)
        {
            mStatus = segments;
            mStatusChanged.Invoke();
        }

        void IPullSession.PublishParameters(IReadOnlyMap<string, SynthesizedParameter> parameters)
        {
            mParameters = parameters;
            mParametersChanged.Invoke();
        }
    }
}
