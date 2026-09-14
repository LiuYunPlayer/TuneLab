using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Scripted;

// 脚本 effect 引擎的进程内代理（设计 §5.5 effect 家族 + §3.5 数据线程纪律）：
// EffectManager/EffectGraph 眼里它与 in-proc IEffectSynthesisEngine 不可分辨。
//
// 正向（宿主→sidecar）：engine.init/destroy、effect.get_*（声明，经 TryCallRunning 有界 + 缓存）、
// effect.process（无限超时——长模型推理；死亡即 fault——电平语义下失败=passthrough）。
// 反向（sidecar→宿主）：host.input_read / host.properties / host.evaluate / host.segment_* ——
// 全部经会话捕获的同步上下文 marshal 回数据线程执行（活视图纪律），30s 上界。
// 发布（sidecar→宿主通知）：effect.status / effect.parameters（volatile 引用换 + ActionEvent 触发，
// 宿主管线自己 marshal）。
internal sealed class RemoteEffectEngine : IEffectSynthesisEngine, IExtensionSettings
{
    // 一个 host 一个包，包可多 effect 条目 → 反向调用按 engine 字段扇出到本包全部脚本 effect 引擎。
    static readonly Dictionary<SidecarHost, Dictionary<string, RemoteEffectEngine>> sByHost = new();

    readonly SidecarHost mHost;
    readonly string mEngineId;
    readonly bool mHasSettings;
    readonly ConcurrentDictionary<string, RemoteEffectSession> mSessions = new();
    int mSessionCounter;

    // 声明面缓存（§3.5）：(输入指纹 → 结果)。数据线程只吃 250ms 有界调用；miss 后台补发。
    const int DeclarationTimeoutMs = 250;
    (string Fingerprint, JsonNode? Result)? mPropCfgCache;
    (string Fingerprint, JsonNode? Result)? mAutoCfgCache;
    (string Fingerprint, JsonNode? Result)? mSynthCfgCache;

    public RemoteEffectEngine(SidecarHost host, string engineId, bool hasSettings)
    {
        mHost = host; mEngineId = engineId; mHasSettings = hasSettings;
        bool first;
        lock (sByHost)
        {
            if (!sByHost.TryGetValue(host, out var map))
            {
                map = new Dictionary<string, RemoteEffectEngine>();
                sByHost[host] = map;
                first = true;
                RegisterReverseChannels(host);
            }
            else
                first = false;
            map[engineId] = this;
        }
        _ = first;
    }

    static void RegisterReverseChannels(SidecarHost host)
    {
        host.RegisterLocalCall("host.input_read", a => Route(a, s => s.InputReadAsync(a)));
        host.RegisterLocalCall("host.properties", a => Route(a, s => s.PropertiesAsync(a)));
        host.RegisterLocalCall("host.evaluate", a => Route(a, s => s.EvaluateAsync(a)));
        host.RegisterLocalCall("host.automation_tracks", a => Route(a, s => s.TracksAsync()));
        host.RegisterLocalCall("host.segment_create", a => Route(a, s => s.SegmentCreateAsync(a)));
        host.RegisterLocalCall("host.segment_write", a => Route(a, s => s.SegmentWriteAsync(a)));
        host.RegisterLocalCall("host.segment_commit", a => Route(a, s => s.SegmentSimpleAsync(a, static seg => seg.Commit())));
        host.RegisterLocalCall("host.segment_resize", a => Route(a, s => s.SegmentResizeAsync(a)));
        host.RegisterLocalCall("host.segment_dispose", a => Route(a, s => s.SegmentDisposeAsync(a)));
        host.RegisterLocalNotify("effect.status", a => RouteFire(a, (s, args) => s.ApplyStatus(args)));
        host.RegisterLocalNotify("effect.parameters", a => RouteFire(a, (s, args) => s.ApplyParameters(args)));
    }

    static Task<JsonNode?> Route(JsonNode? args, Func<RemoteEffectSession, Task<JsonNode?>> body)
        => RouteCore(args, body);

    static async Task<JsonNode?> RouteCore(JsonNode? args, Func<RemoteEffectSession, Task<JsonNode?>> body)
    {
        var (engine, session) = Resolve(args);
        if (engine == null || session == null)
            throw new SidecarRpcException("stale_handle", "unknown engine/session for reverse call");
        return await body(session).ConfigureAwait(false);
    }

    static void RouteFire(JsonNode? args, Action<RemoteEffectSession, JsonNode?> body)
    {
        var (engine, session) = Resolve(args);
        if (engine != null && session != null)
            body(session, args);
    }

    static (RemoteEffectEngine?, RemoteEffectSession?) Resolve(JsonNode? args)
    {
        var o = args as JsonObject;
        var engineId = o?["engine"]?.GetValue<string>();
        var sessionId = o?["session"]?.GetValue<string>();
        if (engineId == null || sessionId == null) return (null, null);
        lock (sByHost)
        {
            foreach (var map in sByHost.Values)
                if (map.TryGetValue(engineId, out var eng) && eng.mSessions.TryGetValue(sessionId, out var sess))
                    return (eng, sess);
        }
        return (null, null);
    }

    // ── 引擎生命周期 ──

    public void Init()
    {
        // 长任务（模型加载可数分钟）：无限超时；死亡/错误抛 → EffectManager 边界 catch → passthrough。
        mHost.CallAsync("engine.init", new JsonObject { ["engine"] = mEngineId }, Timeout.InfiniteTimeSpan)
            .GetAwaiter().GetResult();
    }

    public void Destroy()
    {
        try { mHost.Notify("engine.destroy", new JsonObject { ["engine"] = mEngineId }); } catch { }
    }

    public IEffectSynthesisSession CreateSession(IEffectSynthesisContext context)
    {
        var id = mEngineId + "#" + Interlocked.Increment(ref mSessionCounter);
        var session = new RemoteEffectSession(this, context, id);
        mSessions[id] = session;
        _ = mHost.CallAsync("engine.create_session", new JsonObject { ["engine"] = mEngineId, ["session"] = id })
            .ContinueWith(t => { session.CreateFault = t.Exception?.GetBaseException(); });
        return session;
    }

    internal void DropSession(string id) => mSessions.TryRemove(id, out _);

    // ── 声明面（缓存 + 有界同步；§3.5）──

    public ObjectConfig GetPropertyConfig(IEffectSynthesisPropertyContext context)
    {
        var values = DeclarationArgs(context);
        return CachedDecl(ref mPropCfgCache, "effect.get_property_config", values) is { } node
            ? (IControllerConfig?)ScriptedConfigWire.ReadConfig(node["config"]) as ObjectConfig ?? EmptyObject()
            : EmptyObject();
    }

    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IEffectSynthesisPropertyContext context)
    {
        var values = DeclarationArgs(context);
        var node = CachedDecl(ref mAutoCfgCache, "effect.get_automation_configs", values);
        return ScriptedConfigWire.ReadAutomationMap(node?["tracks"], "automations");
    }

    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context)
    {
        var values = DeclarationArgs(context);
        var node = CachedDecl(ref mSynthCfgCache, "effect.get_synthesized_parameter_configs", values);
        return ScriptedConfigWire.ReadAutomationMap(node?["tracks"], "synthesized_parameters");
    }

    static JsonNode DeclarationArgs(IEffectSynthesisPropertyContext context)
    {
        // 只带声明所需的数据：各 effect 实例的当前参数值（求值器是活视图，跨不了线也不该跨）。
        var effects = new JsonArray();
        foreach (var view in context.Effects)
            effects.Add(ScriptedWire.SerializeNode(view.Properties));
        return new JsonObject { ["effects"] = effects };
    }

    JsonNode? CachedDecl(ref (string Fingerprint, JsonNode? Result)? cache, string method, JsonNode extra)
    {
        var payload = new JsonObject { ["engine"] = mEngineId };
        foreach (var kv in extra.AsObject())
            payload[kv.Key] = kv.Value?.DeepClone();
        var fingerprint = payload.ToJsonString();
        if (mHost.TryCallRunning(method, payload, TimeSpan.FromMilliseconds(DeclarationTimeoutMs), out var result, out _))
        {
            cache = (fingerprint, result);
            return result;
        }
        if (cache?.Fingerprint == fingerprint)
            return cache.Value.Result;      // 进程未起/超时：同输入旧值照用
        return cache?.Result;               // 换了输入但拿不到新值：暂用旧值（下拍 commit 再问）
    }

    static ObjectConfig EmptyObject() => ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());

    // ── 设置面（§5.7；key 用 engine id，与 format 的 entry 同位）──

    ObjectConfig IExtensionSettings.GetSettingsConfig(IExtensionSettingsContext context)
    {
        if (!mHasSettings)
            return EmptyObject();
        var reply = mHost.CallAsync("settings.get_config", new JsonObject { ["entry"] = mEngineId })
            .GetAwaiter().GetResult();
        return (reply?["config"] as JsonObject) is { } cfg ? (ObjectConfig)ScriptedConfigWire.ReadConfig(cfg) : EmptyObject();
    }

    void IExtensionSettings.ApplySettings(PropertyObject settings)
    {
        if (!mHasSettings)
            return;
        mHost.SetSettings(mEngineId, ScriptedWire.SerializeNode(settings));
    }

    // ── 会话 ──

    internal sealed class RemoteEffectSession : IEffectSynthesisSession
    {
        readonly RemoteEffectEngine mEngine;
        readonly IEffectSynthesisContext mContext;
        readonly string mId;
        readonly ConcurrentDictionary<string, IAudioSegment> mSegments = new();
        int mSegmentCounter;
        SynchronizationContext? mSync;                    // 数据线程投递面（构造=管线数据线程；Process 再捕获兜底）
        volatile IReadOnlyList<SynthesisStatusSegment> mStatus = Array.Empty<SynthesisStatusSegment>();
        volatile IReadOnlyMap<string, SynthesizedParameter> mParameters = new Map<string, SynthesizedParameter>();
        int mBusy;
        bool mDisposed;

        public Exception? CreateFault;

        readonly ActionEvent mStatusChanged = new();
        readonly ActionEvent mParametersChanged = new();
        public IActionEvent StatusChanged => mStatusChanged;
        public IActionEvent SynthesizedParametersChanged => mParametersChanged;

        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mParameters;
        public IReadOnlyList<SynthesisStatusSegment> Status => mStatus;

        public RemoteEffectSession(RemoteEffectEngine engine, IEffectSynthesisContext context, string id)
        {
            mEngine = engine; mContext = context; mId = id;
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
                if (CreateFault != null)
                    throw CreateFault;
                var geometry = await OnData(() =>
                {
                    var input = mContext.Input;
                    return new JsonObject { ["sample_offset"] = input.SampleOffset, ["sample_count"] = input.SampleCount, ["sample_rate"] = input.SampleRate };
                }).ConfigureAwait(false);
                using var reg = cancellation.CanBeCanceled
                    ? cancellation.Register(() => { try { mEngine.mHost.Notify("effect.cancel", new JsonObject { ["engine"] = mEngine.mEngineId, ["session"] = mId }); } catch { } })
                    : default;
                // 无限超时：完成/死亡/取消（尽力）由 sidecar 定夺；电平语义下取消=正常返回。
                await mEngine.mHost.CallAsync("effect.process", new JsonObject { ["engine"] = mEngine.mEngineId, ["session"] = mId, ["input"] = geometry },
                    Timeout.InfiniteTimeSpan).ConfigureAwait(false);
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
            mEngine.DropSession(mId);
            try { mEngine.mHost.Notify("session.dispose", new JsonObject { ["engine"] = mEngine.mEngineId, ["session"] = mId }); } catch { }
            var segments = mSegments.Values.ToArray();
            mSegments.Clear();
            _ = OnData(() => { foreach (var s in segments) { try { s.Dispose(); } catch { } } return true; });
        }

        // ── 反向调用处理体（在池线程被调，marshal 回数据线程执行，30s 上界）──

        const int MarshalTimeoutMs = 30_000;

        internal async Task<JsonNode?> InputReadAsync(JsonNode? args)
        {
            var o = args!.AsObject();
            var offset = (int)(o["offset"]?.GetValue<long>() ?? 0);
            var count = (int)(o["count"]?.GetValue<long>() ?? mContext.Input.SampleCount);
            var samples = await OnData(() =>
            {
                count = Math.Min(count, Marshal.MaxChunkSamples);
                var buf = new float[count];
                mContext.Input.Read(offset, buf);
                return buf;
            }).ConfigureAwait(false);
            return new JsonObject { ["data"] = WireAudio.EncodeF32(samples) };
        }

        internal async Task<JsonNode?> PropertiesAsync(JsonNode? args)
        {
            // 活视图按 key 取值（无全键枚举面，纪律即"声明的键自己知道"）；keys 缺省 = 空集。
            var keys = ((args as JsonObject)?["keys"] as JsonArray)?.Select(x => x!.GetValue<string>()).ToArray()
                ?? [];
            var values = await OnData(() =>
            {
                var map = new Map<string, PropertyValue>();
                foreach (var k in keys)
                    map[k] = mContext.Properties.GetValue(k, default);
                return map;
            }).ConfigureAwait(false);
            return new JsonObject { ["values"] = ScriptedWire.SerializeNode(new PropertyObject(values)) };
        }

        internal async Task<JsonNode?> TracksAsync()
        {
            var keys = await OnData(() => mContext.Automations.Keys.ToArray()).ConfigureAwait(false);
            return new JsonObject { ["tracks"] = JsonSerializer.SerializeToNode(keys) };
        }

        internal async Task<JsonNode?> EvaluateAsync(JsonNode? args)
        {
            var o = args!.AsObject();
            var track = o["track"]!.GetValue<string>();
            var positions = WireAudio.DecodeF64(o["positions"]!.GetValue<string>());
            var values = await OnData(() =>
            {
                if (!mContext.Automations.TryGetValue(track, out var auto))
                    throw new SidecarRpcException("stale_handle", "no automation track '" + track + "'");
                var results = new double[positions.Length];
                auto.Evaluate(positions, results);
                return results;
            }).ConfigureAwait(false);
            return new JsonObject { ["values"] = WireAudio.EncodeF64(values) };
        }

        internal async Task<JsonNode?> SegmentCreateAsync(JsonNode? args)
        {
            var o = args!.AsObject();
            var offset = o["sample_offset"]!.GetValue<long>();
            var count = (int)o["sample_count"]!.GetValue<long>();
            var rate = (int)o["sample_rate"]!.GetValue<long>();
            return await OnData<JsonNode?>(() =>
            {
                var seg = mContext.CreateAudioSegment(offset, count, rate);
                var id = mEngine.mEngineId + "/seg" + Interlocked.Increment(ref mSegmentCounter);
                mSegments[id] = seg;
                return new JsonObject { ["segment"] = id };
            }).ConfigureAwait(false);
        }

        internal Task<JsonNode?> SegmentWriteAsync(JsonNode? args)
        {
            var o = args!.AsObject();
            var offset = (int)o["offset"]!.GetValue<long>();
            var samples = WireAudio.DecodeF32(o["data"]!.GetValue<string>());
            return SegmentCallAsync(o, seg => { seg.Write(offset, samples); return null; });
        }

        internal Task<JsonNode?> SegmentResizeAsync(JsonNode? args)
        {
            var o = args!.AsObject();
            var offset = o["sample_offset"]!.GetValue<long>();
            var count = (int)o["sample_count"]!.GetValue<long>();
            return SegmentCallAsync(o, seg => { seg.Resize(offset, count); return null; });
        }

        internal Task<JsonNode?> SegmentSimpleAsync(JsonNode? args, Action<IAudioSegment> op)
            => SegmentCallAsync(args!.AsObject(), seg => { op(seg); return null; });

        internal Task<JsonNode?> SegmentDisposeAsync(JsonNode? args)
        {
            var segId = args!.AsObject()["segment"]!.GetValue<string>();
            if (!mSegments.TryRemove(segId, out var seg))
                throw new SidecarRpcException("stale_handle", "unknown segment " + segId);
            return SegmentCallRaw(seg, s => { s.Dispose(); return null; });
        }

        // 段操作统一路径：解析 id -> marshal 数据线程执行 -> 应答 {}。
        internal Task<JsonNode?> SegmentCallAsync(JsonObject o, Func<IAudioSegment, JsonNode?> op)
        {
            var segId = o["segment"]!.GetValue<string>();
            if (!mSegments.TryGetValue(segId, out var seg))
                throw new SidecarRpcException("stale_handle", "unknown segment " + segId);
            return SegmentCallRaw(seg, op);
        }

        internal Task<JsonNode?> SegmentCallRaw(IAudioSegment seg, Func<IAudioSegment, JsonNode?> op)
        {
            var sync = mSync;
            if (sync == null)
                return Task.FromResult(op(seg) ?? new JsonObject());
            var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            sync.Post(_ =>
            {
                try { tcs.TrySetResult(op(seg) ?? new JsonObject()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, null);
            return WithMarshalTimeout(tcs.Task);
        }

        // ── 发布（sidecar→宿主通知；任意线程到达，管线自己 marshal）──

        internal void ApplyStatus(JsonNode? args)
        {
            var list = new List<SynthesisStatusSegment>();
            if (args is JsonObject o && o["segments"] is JsonArray arr)
                foreach (var item in arr)
                {
                    var so = item!.AsObject();
                    list.Add(new SynthesisStatusSegment
                    {
                        StartTime = so["start"].GetValue<double>(),
                        EndTime = so["end"].GetValue<double>(),
                        Status = ParseStatus(so["status"]?.GetValue<string>()),
                        Message = so["message"]?.GetValue<string>(),
                        Progress = so["progress"]?.GetValue<double>() ?? 0,
                    });
                }
            mStatus = list;
            mStatusChanged.Invoke();
        }

        static SynthesisSegmentStatus ParseStatus(string? s) => s switch
        {
            "pending" => SynthesisSegmentStatus.Pending,
            "synthesizing" => SynthesisSegmentStatus.Synthesizing,
            "failed" => SynthesisSegmentStatus.Failed,
            _ => SynthesisSegmentStatus.Synthesized,
        };

        internal void ApplyParameters(JsonNode? args)
        {
            var map = new Map<string, SynthesizedParameter>();
            if (args is JsonObject o && o["parameters"] is JsonObject ps)
                foreach (var p in ps)
                {
                    var segments = new List<IReadOnlyList<Point>>();
                    if (p.Value is JsonObject po && po["segments"] is JsonArray segs)
                        foreach (var seg in segs)
                            segments.Add(((JsonArray)seg!).Select(pt => new Point(pt![0]!.GetValue<double>(), pt[1]!.GetValue<double>())).ToList());
                    map[p.Key] = new SynthesizedParameter { Segments = segments };
                }
            mParameters = map;
            mParametersChanged.Invoke();
        }

        // ── 数据线程 marshal ──

        internal Task<T> OnData<T>(Func<T> work)
        {
            var sync = mSync;
            if (sync == null)
                return Task.Run(work);      // 无上下文（headless/竞态早期）：池上执行，活视图纪律由宿主自证
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            sync.Post(_ =>
            {
                try { tcs.TrySetResult(work()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, null);
            return WithMarshalTimeout(tcs.Task);
        }

        internal async Task<T> WithMarshalTimeout<T>(Task<T> task)
        {
            var done = await Task.WhenAny(task, Task.Delay(MarshalTimeoutMs)).ConfigureAwait(false);
            if (done != task)
                throw new SidecarRpcException("timeout", "data-thread marshal timed out");
            return await task.ConfigureAwait(false);
        }
    }

    internal static class Marshal
    {
        public const int MaxChunkSamples = 1 << 20;   // 单次反向读上限（1M 样本 ≈ 4MB base64 ≈ 5.3MB 帧）
    }
}
