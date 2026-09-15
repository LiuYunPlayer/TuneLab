using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Data.Synthesis;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Scripted;

// 常驻会话（effect/voice）共享的「反向拉取 + 产物发布」总线（设计 §5.5/§6 的 inline 面）：
// sidecar 经 host.input_read / host.properties / host.evaluate / host.snapshot / host.segment_*
// 反向拉数据线程真值；产物经 effect.*/voice.* 通知回发布。会话键带 kind 前缀（effect:/voice:）。
internal interface IPullSession
{
    string SessionKey { get; }
    // 数据线程投递面：null = 无上下文（headless），反向处理直接池上内联执行。
    SynchronizationContext? Sync { get; }
    bool TryInputGeometry(out long offset, out int count, out int rate);
    // 读输入段（绝对轴寻址的段内偏移）。越界返回 false。
    bool TryReadInput(int offset, int count, out float[] samples);
    // part 级当前参数值（effect = Properties 活视图；voice = PartProperties）。keys null = 空集。
    PropertyObject PullProperties(IReadOnlyList<string>? keys);
    bool TryEvaluateTrack(string track, IReadOnlyList<double> positions, out double[] results);
    IReadOnlyList<string> AutomationTracks();
    IAudioSegment CreateOutputSegment(long sampleOffset, int sampleCount, int sampleRate);

    void PublishStatus(IReadOnlyList<SynthesisStatusSegment> segments);
    void PublishParameters(IReadOnlyMap<string, SynthesizedParameter> parameters);
    // voice-only：effect 会话保持默认 no-op（façade 对 effect 从不发 voice.pitch/phonemes）。
    void PublishPitch(SynthesizedPitch pitch) { }
    void PublishPhonemes(IReadOnlyMap<string, SynthesizedSyllable> phonemes) { }
    // voice 反向拉（effect 会话不会收到）。
    JsonNode? PullSnapshot(IReadOnlyList<string> noteIds) => null;
    JsonNode? PullNotes() => null;
}

// 每 host 一张表；反向处理器经 EnsureAttached 每 host 恰好注册一次。
internal static class RemotePullBus
{
    sealed class Table
    {
        public readonly ConcurrentDictionary<string, IPullSession> Sessions = new();
        public readonly ConcurrentDictionary<string, IAudioSegment> Segments = new();
        // shm 面（M4b）：协商为 shm 后创建的段的共享区域（id→region）；体的唯一权威副本在区域里。
        public readonly ConcurrentDictionary<string, ShmRegion> Regions = new();
        // 曲线面结果环（M5a / §5.6）：per-session 一个 f64 区域，大批量求值走它；同一会话的 RPC 天然串行。
        public readonly ConcurrentDictionary<string, ShmRegion> EvalRings = new();
        public int SegCounter;
        public int Attached;
    }

    static readonly ConcurrentDictionary<SidecarHost, Table> sTables = new();

    public static void Register(SidecarHost host, IPullSession session)
    {
        var table = sTables.GetOrAdd(host, _ => new Table());
        table.Sessions[session.SessionKey] = session;
        if (Interlocked.Exchange(ref table.Attached, 1) == 0)
            Attach(host, table);
    }

    public static void Unregister(SidecarHost host, string sessionKey)
    {
        if (sTables.TryGetValue(host, out var table))
            table.Sessions.TryRemove(sessionKey, out _);
    }

    // 会话终结的段清算：把该会话名下未显式 dispose 的段在数据线程上收割（in-proc 契约里这归插件自己，
    // 代理不信任 sidecar 会先清账——它可能已经死了）。
    public static void DisposeOwnedSegments(SidecarHost host, string sessionKey)
    {
        if (!sTables.TryGetValue(host, out var table))
            return;
        var prefix = sessionKey + "/seg";
        if (table.EvalRings.TryRemove(sessionKey, out var ring))
            try { ring.Dispose(); } catch { }
        foreach (var id in table.Segments.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            if (table.Segments.TryRemove(id, out var seg))
            {
                if (table.Regions.TryRemove(id, out var region))
                    try { region.Dispose(); } catch { }
                if (table.Sessions.TryGetValue(sessionKey, out var session))
                    _ = OnSessionThread(session, () => { try { seg.Dispose(); } catch { } return 0; });
                else
                    try { seg.Dispose(); } catch { }
            }
    }

    static (Table?, IPullSession?) Resolve(JsonNode? args)
    {
        var key = (args as JsonObject)?["session"]?.GetValue<string>();
        if (key != null)
            foreach (var table in sTables.Values)
                if (table.Sessions.TryGetValue(key, out var session))
                    return (table, session);
        return (null, null);
    }

    static Task<JsonNode?> Need(JsonNode? args, Func<IPullSession, JsonNode?> body)
    {
        var (_, session) = Resolve(args);
        if (session == null)
            throw new SidecarRpcException("stale_handle", "unknown session for reverse call");
        return Task.FromResult(body(session));
    }

    static Task<JsonNode?> NeedAsync(JsonNode? args, Func<IPullSession, Task<JsonNode?>> body)
    {
        var (_, session) = Resolve(args);
        if (session == null)
            throw new SidecarRpcException("stale_handle", "unknown session for reverse call");
        return body(session);
    }

    static void Attach(SidecarHost host, Table table)
    {
        // —— 反向拉取（handler 在池线程被调；经会话捕获的同步上下文 marshal 回数据线程）——
        host.RegisterLocalCall("host.input_read", args => NeedAsync(args, async s =>
        {
            var o = args!.AsObject();
            int offset = (int)(o["offset"]?.GetValue<long>() ?? 0);
            int count = (int)(o["count"]?.GetValue<long>() ?? 0);
            if (!s.TryInputGeometry(out var _, out var total, out var rate))
                throw new SidecarRpcException("stale_handle", "session has no input plane");
            if (count <= 0 || count > RemoteLimits.MaxChunkSamples)
                count = Math.Clamp(total - offset, 0, RemoteLimits.MaxChunkSamples);
            var samples = await OnSessionThread(s, () =>
            {
                if (!s.TryReadInput(offset, count, out var buf))
                    throw new SidecarRpcException("io", "input read out of range");
                return buf;
            }).ConfigureAwait(false);
            return new JsonObject { ["data"] = WireAudio.EncodeF32(samples), ["sample_rate"] = rate };
        }));

        host.RegisterLocalCall("host.properties", args => NeedAsync(args, async s =>
        {
            var keys = ((args as JsonObject)?["keys"] as JsonArray)?.Select(x => x!.GetValue<string>()).ToList();
            var values = await OnSessionThread(s, () => s.PullProperties(keys is { Count: > 0 } ? keys : null)).ConfigureAwait(false);
            return new JsonObject { ["values"] = ScriptedWire.SerializeNode(values) };
        }));

        host.RegisterLocalCall("host.automation_tracks", args => NeedAsync(args, async s =>
        {
            var tracks = await OnSessionThread(s, () => s.AutomationTracks().ToArray()).ConfigureAwait(false);
            return new JsonObject { ["tracks"] = JsonSerializer.SerializeToNode(tracks) };
        }));

        host.RegisterLocalCall("host.evaluate", args => NeedAsync(args, async s =>
        {
            var o = args!.AsObject();
            var track = o["track"]!.GetValue<string>();
            var positions = WireAudio.DecodeF64(o["positions"]!.GetValue<string>());
            var results = await OnSessionThread(s, () =>
            {
                if (!s.TryEvaluateTrack(track, positions, out var r))
                    throw new SidecarRpcException("stale_handle", "no automation track '" + track + "'");
                return r;
            }).ConfigureAwait(false);

            // §5.6/§6：小批量 inline f64 base64；shm 面下大批量（≥64KB）落 per-session 结果环，
            // 应答改回 {region}（façade 映射读回）——序列化成本从 O(n) base64 降到 memcpy。
            if (host.AudioPlaneShm && (long)results.Length * 8 >= ShmPlane.CurveInlineBytes)
            {
                var ring = await OnSessionThread(s, () =>
                {
                    var reg = table.EvalRings.GetOrAdd(s.SessionKey,
                        _ => ShmPlane.AllocateCurve(host.PackageId, results.Length));
                    if (results.Length > reg.SampleCount)
                        reg.SetGeometry(0, results.Length, 0);   // 只增：扩容保内容
                    reg.WriteF64(0, results);
                    return reg;
                }).ConfigureAwait(false);
                return new JsonObject
                {
                    ["region"] = new JsonObject
                    {
                        ["path"] = ring.FilePath,
                        ["data_offset"] = ShmRegion.HeaderBytes,
                        ["count"] = results.Length,
                    },
                };
            }
            return new JsonObject { ["values"] = WireAudio.EncodeF64(results) };
        }));

        host.RegisterLocalCall("host.snapshot", args => NeedAsync(args, async s =>
        {
            var ids = (args!.AsObject()["note_ids"] as JsonArray)?.Select(x => x!.GetValue<string>()).ToList() ?? [];
            return await OnSessionThread(s, () => s.PullSnapshot(ids)).ConfigureAwait(false);
        }));
        host.RegisterLocalCall("host.notes", args => NeedAsync(args, async s =>
            await OnSessionThread(s, () => s.PullNotes()).ConfigureAwait(false)));

        // —— 段族（id 由本表发号；操作 marshal 回其会话的数据线程）——
        host.RegisterLocalCall("host.segment_create", args => NeedAsync(args, async s =>
        {
            var o = args!.AsObject();
            var offset = o["sample_offset"]!.GetValue<long>();
            var count = (int)o["sample_count"]!.GetValue<long>();
            var rate = (int)o["sample_rate"]!.GetValue<long>();
            return await OnSessionThread(s, () =>
            {
                var seg = s.CreateOutputSegment(offset, count, rate);
                var id = s.SessionKey + "/seg" + Interlocked.Increment(ref table.SegCounter);
                table.Segments[id] = seg;
                var res = new JsonObject { ["segment"] = id };
                if (host.AudioPlaneShm)
                {
                    var region = ShmPlane.Allocate(host.PackageId, offset, count, rate);
                    table.Regions[id] = region;
                    res["region"] = new JsonObject
                    {
                        ["path"] = region.FilePath,
                        ["name"] = Path.GetFileName(region.FilePath),
                        ["data_offset"] = ShmRegion.HeaderBytes,
                        ["sample_offset"] = offset,
                        ["sample_count"] = count,
                        ["sample_rate"] = rate,
                    };
                }
                return (JsonNode)res;
            }).ConfigureAwait(false);
        }));

        host.RegisterLocalCall("host.segment_write", args => SegmentCall(args, table, (seg, region, o) =>
        {
            var offset = (int)o["offset"]!.GetValue<long>();
            var samples = WireAudio.DecodeF32(o["data"]!.GetValue<string>());
            if (region != null)
            {
                // shm 段的权威副本在区域里：宿主代理写也写区域（否则 commit 会拿 region 覆盖掉这次 inline 写）。
                if (offset < 0 || offset + samples.Length > region.SampleCount)
                    throw new SidecarRpcException("invalid_params", "segment_write out of range");
                region.WriteBody(offset, samples);
            }
            else
                seg.Write(offset, samples);
        }));
        host.RegisterLocalCall("host.segment_commit", args => SegmentCall(args, table, (seg, region, _) =>
        {
            if (region != null)
            {
                var buf = new float[region.SampleCount];
                region.ReadBody(0, buf);
                seg.Write(0, buf);
            }
            seg.Commit();
        }));
        host.RegisterLocalCall("host.segment_resize", args => NeedAsync(args, async s =>
        {
            var o = args!.AsObject();
            var id = o["segment"]!.GetValue<string>();
            if (!table.Segments.TryGetValue(id, out var seg))
                throw new SidecarRpcException("stale_handle", "unknown segment " + id);
            if (seg is not AudioSegment audio)
                throw new SidecarRpcException("invalid_params", "resize needs host AudioSegment");
            var newOffset = o["sample_offset"]!.GetValue<long>();
            var newCount = (int)o["sample_count"]!.GetValue<long>();
            return await OnSessionThread(s, () =>
            {
                var res = new JsonObject();
                if (table.Regions.TryGetValue(id, out var region))
                {
                    // region 先冲回 buffer（Resize 保内容靠 buffer），改几何后重铺 region。
                    var cur = new float[region.SampleCount];
                    region.ReadBody(0, cur);
                    seg.Write(0, cur);
                    seg.Resize(newOffset, newCount);
                    region.Dispose();
                    var fresh = ShmPlane.Allocate(host.PackageId, newOffset, newCount, audio.SampleRate);
                    fresh.WriteBody(0, audio.Samples);
                    table.Regions[id] = fresh;
                    res["region"] = new JsonObject
                    {
                        ["path"] = fresh.FilePath,
                        ["name"] = Path.GetFileName(fresh.FilePath),
                        ["data_offset"] = ShmRegion.HeaderBytes,
                        ["sample_offset"] = newOffset,
                        ["sample_count"] = newCount,
                        ["sample_rate"] = audio.SampleRate,
                    };
                }
                else seg.Resize(newOffset, newCount);
                return (JsonNode)res;
            }).ConfigureAwait(false);
        }));
        host.RegisterLocalCall("host.segment_dispose", args =>
        {
            var o = args!.AsObject();
            var id = o["segment"]!.GetValue<string>();
            if (!table.Segments.TryRemove(id, out var seg))
                throw new SidecarRpcException("stale_handle", "unknown segment " + id);
            if (table.Regions.TryRemove(id, out var region))
                try { region.Dispose(); } catch { }
            var (tbl, session) = Resolve(args);
            if (session == null)
                throw new SidecarRpcException("stale_handle", "session gone");
            return OnSessionThread(session, () => { seg.Dispose(); return (JsonNode?)new JsonObject(); });
        });

        // —— 发布（sidecar→宿主通知；effect/voice 同形方法共用实现）——
        host.RegisterLocalNotify("effect.status", args => Publish(args, (s, v) => s.PublishStatus(v), ParseStatuses));
        host.RegisterLocalNotify("voice.status", args => Publish(args, (s, v) => s.PublishStatus(v), ParseStatuses));
        host.RegisterLocalNotify("effect.parameters", args => Publish(args, (s, v) => s.PublishParameters(v), ParseParameters));
        host.RegisterLocalNotify("voice.parameters", args => Publish(args, (s, v) => s.PublishParameters(v), ParseParameters));
        host.RegisterLocalNotify("voice.pitch", args => Publish(args, (s, v) => s.PublishPitch(v), ParsePitch));
        host.RegisterLocalNotify("voice.phonemes", args => Publish(args, (s, v) => s.PublishPhonemes(v), ParsePhonemes));
    }

    static Task<JsonNode?> SegmentCall(JsonNode? args, Table table, Action<IAudioSegment, ShmRegion?, JsonObject> op)
    {
        var o = args!.AsObject();
        var id = o["segment"]!.GetValue<string>();
        if (!table.Segments.TryGetValue(id, out var seg))
            throw new SidecarRpcException("stale_handle", "unknown segment " + id);
        table.Regions.TryGetValue(id, out var region);
        var (_, session) = Resolve(args);
        if (session == null)
            throw new SidecarRpcException("stale_handle", "session gone");
        return OnSessionThread(session, () => { op(seg, region, o); return (JsonNode?)new JsonObject(); });
    }

    static void Publish<T>(JsonNode? args, Action<IPullSession, T> sink, Func<JsonNode?, T> parse)
    {
        var (_, session) = Resolve(args);
        if (session != null)
            sink(session, parse(args));
    }

    static Task<T> OnSessionThread<T>(IPullSession session, Func<T> work)
    {
        var sync = session.Sync;
        if (sync == null)
            return Task.FromResult(work());
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        sync.Post(_ =>
        {
            try { tcs.TrySetResult(work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }, null);
        return WithTimeout(tcs.Task);
    }

    static async Task<T> WithTimeout<T>(Task<T> task)
    {
        var done = await Task.WhenAny(task, Task.Delay(RemoteLimits.MarshalTimeout)).ConfigureAwait(false);
        if (done != task)
            throw new SidecarRpcException("timeout", "data-thread marshal timed out");
        return await task.ConfigureAwait(false);
    }

    // ── wire 解析小件 ──

    internal static IReadOnlyList<SynthesisStatusSegment> ParseStatuses(JsonNode? args)
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
                    Status = so["status"]?.GetValue<string>() switch
                    {
                        "pending" => SynthesisSegmentStatus.Pending,
                        "synthesizing" => SynthesisSegmentStatus.Synthesizing,
                        "failed" => SynthesisSegmentStatus.Failed,
                        _ => SynthesisSegmentStatus.Synthesized,
                    },
                    Message = so["message"]?.GetValue<string>(),
                    Progress = so["progress"]?.GetValue<double>() ?? 0,
                });
            }
        return list;
    }

    internal static IReadOnlyMap<string, SynthesizedParameter> ParseParameters(JsonNode? args)
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
        return map;
    }

    internal static SynthesizedPitch ParsePitch(JsonNode? args)
    {
        var segments = new List<IReadOnlyList<Point>>();
        if (args is JsonObject o && o["segments"] is JsonArray segs)
            foreach (var seg in segs)
                segments.Add(((JsonArray)seg!).Select(pt => new Point(pt![0]!.GetValue<double>(), pt[1]!.GetValue<double>())).ToList());
        return new SynthesizedPitch { Segments = segments };
    }

    internal static IReadOnlyMap<string, SynthesizedSyllable> ParsePhonemes(JsonNode? args)
    {
        var map = new Map<string, SynthesizedSyllable>();
        if (args is JsonObject o && o["syllables"] is JsonObject ys)
            foreach (var y in ys)
            {
                if (y.Value is not JsonObject so) continue;
                map[y.Key] = new SynthesizedSyllable(
                    ReadPhonemes(so["leading"] as JsonArray),
                    ReadPhonemes(so["body"] as JsonArray),
                    so["body_offset"]?.GetValue<double>() ?? 0);
            }
        return map;
    }

    static IReadOnlyList<SynthesizedPhoneme> ReadPhonemes(JsonArray? arr)
        => arr == null ? [] : arr.Select(p => new SynthesizedPhoneme
        {
            Symbol = p!["symbol"].GetValue<string>(),
            Duration = p["duration"].GetValue<double>(),
            StretchWeight = p["stretch_weight"].GetValue<double>(),
        }).ToList();
}

internal static class RemoteLimits
{
    public const int MaxChunkSamples = 1 << 20;                 // 单次反向读上限（4MB 原样 / ≈5.3MB base64）
    public static readonly TimeSpan MarshalTimeout = TimeSpan.FromSeconds(30);
}
