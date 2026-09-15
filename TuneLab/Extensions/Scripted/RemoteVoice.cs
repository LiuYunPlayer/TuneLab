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

// 脚本 voice 引擎的进程内代理（设计 §5；wire 形状见 scripting/wire/v1.json "voice"）。
// VoiceGraph 管线眼里它与 in-proc IVoiceSynthesisEngine 不可分辨：
//  - catalog/layout：Init 期拉取、volatile 缓存（"必须立即返回"契约）；engine.catalog_changed 通知触发重拉。
//  - 声明五件套：TryCallRunning 有界（250ms）+ 指纹缓存（§3.5），数据线程永不无界等待。
//  - 会话：宿主侧订阅 context 事件 → voice.dirty 通知推送（粗粒度信号）；sidecar 按需经反向
//    host.notes/host.snapshot/host.evaluate 重拉全量真相——"廉价标脏、拉取式重算"是 in-proc
//    三层最小事实的 remote 映射，worker 从不触活视图（协议与线程纪律同构）。
//  - 调度：peek 走有界+缓存；synthesize 无超时（应答即完成），取消尽力。
//  - 产物四路（pitch/parameters/phonemes/status）经总线通知换引用发布 + changed 事件。
internal sealed class RemoteVoiceEngine : IVoiceSynthesisEngine, IExtensionSettings
{
    readonly SidecarHost mHost;
    readonly string mEngineId;
    readonly bool mHasSettings;
    readonly ConcurrentDictionary<string, RemoteVoiceSession> mSessions = new();
    int mSessionCounter;

    volatile OrderedMap<string, VoiceSourceInfo> mCatalog = new();
    volatile List<VoiceSourceLayoutItem> mLayout = [];
    JsonNode? mCatalogRaw;

    const int DeclarationTimeoutMs = 250;
    (string Fingerprint, JsonNode? Result)? mAutoCfgCache, mSynthCfgCache, mPartCfgCache, mNoteCfgCache, mPhonemeCfgCache;

    public RemoteVoiceEngine(SidecarHost host, string engineId, bool hasSettings)
    {
        mHost = host; mEngineId = engineId; mHasSettings = hasSettings;
        host.RegisterLocalNotify("engine.catalog_changed", args =>
        {
            if ((args as JsonObject)?["engine"]?.GetValue<string>() == engineId)
                _ = Task.Run(RefreshCatalogAsync);
        });
    }

    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mCatalog;
    public IReadOnlyList<VoiceSourceLayoutItem> VoiceSourceLayout => mLayout;

    public void Init()
    {
        mHost.CallAsync("engine.init", new JsonObject { ["engine"] = mEngineId }, Timeout.InfiniteTimeSpan)
            .GetAwaiter().GetResult();
        RefreshCatalogAsync().GetAwaiter().GetResult();
    }

    public void Destroy()
    {
        try { mHost.Notify("engine.destroy", new JsonObject { ["engine"] = mEngineId }); } catch { }
    }

    async Task RefreshCatalogAsync()
    {
        try
        {
            var reply = await mHost.CallAsync("voice.get_catalog", new JsonObject { ["engine"] = mEngineId }).ConfigureAwait(false);
            if (reply == null) return;
            mCatalogRaw = reply;
            ApplyCatalog(reply);
        }
        catch (Exception ex)
        {
            Log.Warning(string.Format(System.Globalization.CultureInfo.InvariantCulture, "voice catalog fetch failed for {0}: {1}", mEngineId, ex.Message));
        }
    }

    void ApplyCatalog(JsonNode reply)
    {
        var catalog = new OrderedMap<string, VoiceSourceInfo>();
        if (reply["sources"] is JsonArray arr)
            foreach (var item in arr)
            {
                var o = item!.AsObject();
                var id = o["id"]!.GetValue<string>();
                catalog[id] = new VoiceSourceInfo
                {
                    Name = o["name"]?.GetValue<string>() ?? id,
                    Description = o["description"]?.GetValue<string>() ?? string.Empty,
                    Portrait = null,   // 图像资源跨线（M3d：发文件引用再挂 FileImageResource）
                };
            }
        var layout = new List<VoiceSourceLayoutItem>();
        if (reply["layout"] is JsonArray lay)
            foreach (var item in lay)
                if (ParseLayoutItem(item) is { } li)
                    layout.Add(li);
        mCatalog = catalog;
        mLayout = layout;
    }

    static VoiceSourceLayoutItem? ParseLayoutItem(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        if (o["group"] is { } g && g is JsonArray members)
        {
            var items = members.Select(ParseLayoutItem).Where(x => x != null).Select(x => x!).ToList();
            return VoiceSourceLayoutItem.Group(o["name"]?.GetValue<string>() ?? "", items);
        }
        if (o["voice"]?.GetValue<string>() is { Length: > 0 } vid)
            return VoiceSourceLayoutItem.Voice(vid);
        return null;
    }

    public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context)
    {
        var key = $"voice:{mEngineId}#{Interlocked.Increment(ref mSessionCounter)}";
        var session = new RemoteVoiceSession(this, context, key);
        mSessions[key] = session;
        RemotePullBus.Register(mHost, session);
        session.CreateHandshake = mHost.CallAsync("engine.create_session", new JsonObject
            {
                ["engine"] = mEngineId, ["session"] = key, ["voice_id"] = context.VoiceId,
            })
            .ContinueWith(t => { if (t.Exception != null) session.CreateFault = t.Exception.GetBaseException(); });
        return session;
    }

    internal void DropSession(string key)
    {
        mSessions.TryRemove(key, out _);
        RemotePullBus.Unregister(mHost, key);
    }

    // ── 声明五件套（缓存 + 有界；§3.5）──

    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context)
    {
        var node = CachedDecl(ref mAutoCfgCache, "voice.get_automation_configs", PartArgs(context));
        return ScriptedConfigWire.ReadAutomationMap(node?["tracks"], "automations");
    }

    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context)
    {
        var node = CachedDecl(ref mSynthCfgCache, "voice.get_synthesized_parameter_configs", PartArgs(context));
        return ScriptedConfigWire.ReadAutomationMap(node?["tracks"], "synthesized_parameters");
    }

    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context)
    {
        var node = CachedDecl(ref mPartCfgCache, "voice.get_part_property_config", PartArgs(context));
        return node?["config"] is { } cfg ? (ObjectConfig)ScriptedConfigWire.ReadConfig(cfg) : VoiceViewWire.EmptyObject();
    }

    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context)
    {
        var node = CachedDecl(ref mNoteCfgCache, "voice.get_note_property_config", NoteArgs(context));
        return node?["config"] is { } cfg ? (ObjectConfig)ScriptedConfigWire.ReadConfig(cfg) : VoiceViewWire.EmptyObject();
    }

    public IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context)
    {
        var node = CachedDecl(ref mPhonemeCfgCache, "voice.get_phoneme_property_configs", NoteArgs(context));
        var map = new Map<int, ObjectConfig>();
        if (node?["slots"] is JsonArray slots)
            foreach (var s in slots)
            {
                var so = s!.AsObject();
                map[so["slot"].GetValue<int>()] = (ObjectConfig)ScriptedConfigWire.ReadConfig(so["config"]);
            }
        return map;
    }

    JsonObject PartArgs(IVoiceSynthesisPartPropertyContext context)
    {
        var parts = new JsonArray();
        foreach (var part in context.Parts)
            parts.Add(VoiceViewWire.PartView(part));
        return new JsonObject { ["engine"] = mEngineId, ["parts"] = parts };
    }

    JsonObject NoteArgs(IVoiceSynthesisNotePropertyContext context)
        => new()
        {
            ["engine"] = mEngineId,
            ["part"] = VoiceViewWire.PartView(context.Part),
            ["notes"] = new JsonArray(context.Notes.Select(VoiceViewWire.NoteView).ToArray()),
        };

    JsonNode? CachedDecl(ref (string Fingerprint, JsonNode? Result)? cache, string method, JsonObject payload)
    {
        var fingerprint = payload.ToJsonString();
        if (mHost.TryCallRunning(method, JsonNode.Parse(fingerprint), TimeSpan.FromMilliseconds(DeclarationTimeoutMs), out var result, out _))
        {
            cache = (fingerprint, result);
            return result;
        }
        if (cache?.Fingerprint == fingerprint)
            return cache.Value.Result;
        return cache?.Result;
    }

    // ── 设置面（§5.7；key 用 engine id）──

    ObjectConfig IExtensionSettings.GetSettingsConfig(IExtensionSettingsContext context)
    {
        if (!mHasSettings)
            return VoiceViewWire.EmptyObject();
        var reply = mHost.CallAsync("settings.get_config", new JsonObject { ["entry"] = mEngineId })
            .GetAwaiter().GetResult();
        return reply?["config"] is { } cfg ? (ObjectConfig)ScriptedConfigWire.ReadConfig(cfg) : VoiceViewWire.EmptyObject();
    }

    void IExtensionSettings.ApplySettings(PropertyObject settings)
    {
        if (!mHasSettings)
            return;
        mHost.SetSettings(mEngineId, ScriptedWire.SerializeNode(settings));
    }

    // ══════════════════════════════ 会话 ══════════════════════════════

    sealed class RemoteVoiceSession : IVoiceSynthesisSession, IPullSession
    {
        readonly RemoteVoiceEngine mEngine;
        readonly IVoiceSynthesisContext mContext;
        readonly SidecarHost mHost;
        readonly string mVoiceId;
        SynchronizationContext? mSync;
        long mDirtySeq;                       // 任何推送事件 +1；peek/continuation 缓存键的一部分
        int mBusy;
        bool mDisposed;

        volatile SynthesizedPitch mPitch = new() { Segments = [] };
        volatile IReadOnlyMap<string, SynthesizedParameter> mParameters = new Map<string, SynthesizedParameter>();
        volatile IReadOnlyMap<string, SynthesizedSyllable> mPhonemes = new Map<string, SynthesizedSyllable>();
        volatile IReadOnlyList<SynthesisStatusSegment> mStatus = Array.Empty<SynthesisStatusSegment>();
        volatile string mDefaultLyric = string.Empty;
        (SynthesisRange? Range, long Seq)? mPeekCache;
        readonly ConcurrentDictionary<string, (string Fingerprint, bool Value)> mContinuationCache = new();

        readonly ActionEvent mPitchChanged = new();
        readonly ActionEvent mParametersChanged = new();
        readonly ActionEvent mPhonemesChanged = new();
        readonly ActionEvent mStatusChanged = new();

        // context 订阅句柄（Dispose 全摘）
        readonly IActionEvent mNotesAny, mNotesMembership, mPartProps, mCommitted;
        readonly Action mNotesAnyHandler, mNotesMembershipHandler, mPartPropsHandler, mCommittedHandler;
        readonly List<(ISynthesisAutomation Auto, Action<double, double> Handler)> mRangeSubs = [];

        public string SessionKey { get; }
        public Exception? CreateFault;
        public Task CreateHandshake = Task.CompletedTask;

        public RemoteVoiceSession(RemoteVoiceEngine engine, IVoiceSynthesisContext context, string key)
        {
            mEngine = engine; mContext = context; SessionKey = key; mHost = engine.mHost; mVoiceId = context.VoiceId;
            mSync = SynchronizationContext.Current;

            // 订阅即"廉价标脏 + 转发粗信号"（数据线程；CreateSession 保证在数据线程被调）。
            mNotesAnyHandler = () => PushDirty("fields", null, 0, 0);
            mNotesMembershipHandler = () => PushDirty("notes", null, 0, 0);
            mPartPropsHandler = () => PushDirty("part_properties", null, 0, 0);
            mCommittedHandler = () => PushDirty("committed", null, 0, 0);
            mNotesAny = context.Notes.WhenAny(
                n => n.StartTime.Modified, n => n.EndTime.Modified, n => n.Pitch.Modified,
                n => n.Lyric.Modified, n => n.LeadingPhonemes.Modified, n => n.BodyPhonemes.Modified,
                n => n.BodyOffset.Modified, n => n.Properties.Modified);
            mNotesAny.Subscribe(mNotesAnyHandler);
            mNotesMembership = context.Notes.MembershipModified;
            mNotesMembership.Subscribe(mNotesMembershipHandler);
            mPartProps = context.PartProperties.Modified;
            mPartProps.Subscribe(mPartPropsHandler);
            mCommitted = context.Committed;
            mCommitted.Subscribe(mCommittedHandler);
            SubscribeRange(context.Pitch, "pitch");
            SubscribeRange(context.PitchDeviation, "pitch_deviation");
            foreach (var kv in context.Automations)
                SubscribeRange(kv.Value, kv.Key);
        }

        void SubscribeRange(ISynthesisAutomation auto, string track)
        {
            Action<double, double> handler = (s, e) => PushDirty("automation", track, s, e);
            auto.RangeModified.Subscribe(handler);
            mRangeSubs.Add((auto, handler));
        }

        void PushDirty(string kind, string? track, double start, double end)
        {
            Interlocked.Increment(ref mDirtySeq);
            if (mDisposed || !mHost.Running)
                return;   // 未起/已终结：sidecar 首次 peek/synthesize 反正全量拉，事件丢不得也白丢
            var args = new JsonObject { ["session"] = SessionKey, ["kind"] = kind };
            if (track != null)
            {
                args["track"] = track;
                args["start"] = start;
                args["end"] = end;
            }
            try { mHost.Notify("voice.dirty", args); } catch { }
        }

        public string DefaultLyric
        {
            get
            {
                if (mHost.TryCallRunning("voice.default_lyric", new JsonObject { ["engine"] = mEngine.mEngineId, ["session"] = SessionKey },
                        TimeSpan.FromMilliseconds(250), out var reply, out _) &&
                    reply?["lyric"]?.GetValue<string>() is { } l)
                {
                    mDefaultLyric = l;
                    return l;
                }
                return mDefaultLyric;
            }
        }

        public bool IsContinuation(IVoiceSynthesisNote note)
        {
            var id = note.Id;
            var fingerprint = VoiceViewWire.NoteFingerprint(note);
            if (mContinuationCache.TryGetValue(id, out var hit) && hit.Fingerprint == fingerprint)
                return hit.Value;
            if (mHost.TryCallRunning("voice.is_continuation",
                    new JsonObject { ["engine"] = mEngine.mEngineId, ["session"] = SessionKey, ["note"] = VoiceViewWire.NoteLive(note) },
                    TimeSpan.FromMilliseconds(250), out var reply, out _))
            {
                var v = reply?["continuation"]?.GetValue<bool>() ?? false;
                mContinuationCache[id] = (fingerprint, v);
                return v;
            }
            return hit.Value;   // 有界失败：旧判定兜着（下一拍 commit 后值变了自然换指纹重问）
        }

        public SynthesisRange? GetNextPendingSynthesisRange(double startTime, double endTime)
        {
            var seq = Interlocked.Read(ref mDirtySeq);
            if (mPeekCache is { } c)
            {
                // 窗同、seq 未推进：直接复用；推进了但 RPC 未成也拿它垫一拍（synthesize 电平语义幂等）。
                double cs = c.Range?.StartTime ?? double.NaN, ce = c.Range?.EndTime ?? double.NaN;
                if (c.Seq == seq && Math.Abs(cs - startTime) < 1e-9 && Math.Abs(ce - endTime) < 1e-9)
                    return c.Range;
            }
            if (mHost.TryCallRunning("voice.peek", new JsonObject
                {
                    ["engine"] = mEngine.mEngineId, ["session"] = SessionKey, ["start"] = startTime, ["end"] = endTime,
                }, TimeSpan.FromMilliseconds(250), out var reply, out _))
            {
                SynthesisRange? range = null;
                if (reply?["range"] is JsonArray r && r.Count == 2)
                    range = new SynthesisRange(r[0]!.GetValue<double>(), r[1]!.GetValue<double>());
                mPeekCache = (range, seq);
                return range;
            }
            return mPeekCache?.Range;
        }

        public async Task SynthesizeNext(double startTime, double endTime, CancellationToken cancellation = default)
        {
            if (mDisposed) throw new ObjectDisposedException(nameof(RemoteVoiceSession));
            if (Interlocked.CompareExchange(ref mBusy, 1, 0) != 0)
                throw new InvalidOperationException("voice session already synthesizing");
            mSync = SynchronizationContext.Current ?? mSync;
            try
            {
                await CreateHandshake.ConfigureAwait(false);
                if (CreateFault != null)
                    throw CreateFault;
                using var reg = cancellation.CanBeCanceled
                    ? cancellation.Register(() =>
                    {
                        try { mHost.Notify("voice.cancel", new JsonObject { ["engine"] = mEngine.mEngineId, ["session"] = SessionKey }); } catch { }
                    })
                    : default;
                await mHost.CallAsync("voice.synthesize", new JsonObject
                {
                    ["engine"] = mEngine.mEngineId, ["session"] = SessionKey, ["start"] = startTime, ["end"] = endTime,
                }, Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref mBusy, 0);
            }
        }

        public SynthesizedPitch SynthesizedPitch => mPitch;
        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mParameters;
        public IReadOnlyMap<string, SynthesizedSyllable> SynthesizedPhonemes => mPhonemes;
        public IReadOnlyList<SynthesisStatusSegment> Status => mStatus;
        public IActionEvent SynthesizedPitchChanged => mPitchChanged;
        public IActionEvent SynthesizedParametersChanged => mParametersChanged;
        public IActionEvent SynthesizedPhonemesChanged => mPhonemesChanged;
        public IActionEvent StatusChanged => mStatusChanged;

        public void Dispose()
        {
            if (mDisposed) return;
            mDisposed = true;
            mNotesAny.Unsubscribe(mNotesAnyHandler);
            mNotesMembership.Unsubscribe(mNotesMembershipHandler);
            mPartProps.Unsubscribe(mPartPropsHandler);
            mCommitted.Unsubscribe(mCommittedHandler);
            foreach (var (auto, handler) in mRangeSubs)
                auto.RangeModified.Unsubscribe(handler);
            mRangeSubs.Clear();
            RemotePullBus.DisposeOwnedSegments(mHost, SessionKey);
            mEngine.DropSession(SessionKey);
            try { mHost.Notify("session.dispose", new JsonObject { ["engine"] = mEngine.mEngineId, ["session"] = SessionKey }); } catch { }
        }

        // ── IPullSession：voice 会话的拉取面（总线已 marshal 回数据线程）──

        SynchronizationContext? IPullSession.Sync => mSync;
        bool IPullSession.TryInputGeometry(out long offset, out int count, out int rate)
        {
            offset = 0; count = 0; rate = 0;
            return false;   // voice 无"输入段"（那是 effect 的面）
        }
        bool IPullSession.TryReadInput(int offset, int count, out float[] samples)
        {
            samples = [];
            return false;
        }

        PropertyObject IPullSession.PullProperties(IReadOnlyList<string>? keys)
        {
            var map = new Map<string, PropertyValue>();
            if (keys != null)
                foreach (var k in keys)
                    map[k] = mContext.PartProperties.GetValue(k, default);
            return new PropertyObject(map);
        }

        bool IPullSession.TryEvaluateTrack(string track, IReadOnlyList<double> positions, out double[] results)
        {
            ISynthesisAutomation? auto = track switch
            {
                "pitch" => mContext.Pitch,
                "pitch_deviation" => mContext.PitchDeviation,
                _ => mContext.Automations.TryGetValue(track, out var a) ? a : null,
            };
            if (auto == null)
            {
                results = [];
                return false;
            }
            results = new double[positions.Count];
            auto.Evaluate(positions, results);
            return true;
        }

        IReadOnlyList<string> IPullSession.AutomationTracks()
            => new[] { "pitch", "pitch_deviation" }.Concat(mContext.Automations.Keys).ToList();

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

        void IPullSession.PublishPitch(SynthesizedPitch pitch)
        {
            mPitch = pitch;
            mPitchChanged.Invoke();
        }

        void IPullSession.PublishPhonemes(IReadOnlyMap<string, SynthesizedSyllable> phonemes)
        {
            mPhonemes = phonemes;
            mPhonemesChanged.Invoke();
        }

        JsonNode? IPullSession.PullNotes()
            => new JsonObject { ["notes"] = new JsonArray(mContext.Notes.Select(VoiceViewWire.NoteLive).ToArray()) };

        JsonNode? IPullSession.PullSnapshot(IReadOnlyList<string> noteIds)
        {
            var wanted = new HashSet<string>(noteIds, StringComparer.Ordinal);
            var picked = new List<IVoiceSynthesisNote>();
            var byId = new Dictionary<string, IVoiceSynthesisNote>();
            foreach (var n in mContext.Notes)
            {
                byId[n.Id] = n;
                if (wanted.Contains(n.Id))
                    picked.Add(n);
            }
            // 按请求顺序对齐（façade 传的序 = 它自己标脏的序）
            var ordered = noteIds.Select(id => byId.TryGetValue(id, out var n) ? n : null).Where(x => x != null).Select(x => x!).ToList();
            var snap = mContext.GetSnapshot(ordered);
            var notes = new JsonArray();
            for (int i = 0; i < ordered.Count; i++)
            {
                var sn = snap.Notes[i];
                var o = VoiceViewWire.SnapshotNote(sn);
                o["request_id"] = ordered[i].Id;   // 归属回指（façade 用它回传 phonemes 的 map 键）
                notes.Add(o);
            }
            return new JsonObject
            {
                ["snapshot"] = new JsonObject
                {
                    ["notes"] = notes,
                    ["part_properties"] = ScriptedWire.SerializeNode(snap.PartProperties),
                    ["pitch"] = "pitch", ["pitch_deviation"] = "pitch_deviation",
                    ["automations"] = JsonSerializer.SerializeToNode(snap.Automations.Keys.ToArray()),
                },
            };
        }
    }
}

// voice 值视图的线上形（声明 context / is_continuation / notes / snapshot 共用一套，
// 形状规范同 scripting/wire/v1.json）。
internal static class VoiceViewWire
{
    public static ObjectConfig EmptyObject() => ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());

    public static JsonObject PartView(IVoiceSynthesisPartView part) => new()
    {
        ["voice_id"] = part.VoiceId,
        ["part_properties"] = ScriptedWire.SerializeNode(part.PartProperties),
        ["notes"] = new JsonArray(part.Notes.Select(NoteView).ToArray()),
    };

    public static JsonObject NoteView(IVoiceSynthesisNoteView note) => new()
    {
        ["start"] = note.StartTime,
        ["end"] = note.EndTime,
        ["pitch"] = note.Pitch,
        ["lyric"] = note.Lyric,
        ["body_offset"] = note.BodyOffset,
        ["leading"] = new JsonArray(note.LeadingPhonemes.Select(PhonemeView).ToArray()),
        ["body"] = new JsonArray(note.BodyPhonemes.Select(PhonemeView).ToArray()),
        ["properties"] = ScriptedWire.SerializeNode(note.Properties),
    };

    static JsonObject PhonemeView(IVoiceSynthesisPhonemeView p) => new()
    {
        ["symbol"] = p.Symbol,
        ["duration"] = p.Duration,
        ["stretch_weight"] = p.StretchWeight,
        ["properties"] = ScriptedWire.SerializeNode(p.Properties),
    };

    public static JsonObject NoteLive(IVoiceSynthesisNote note) => new()
    {
        ["id"] = note.Id,
        ["start"] = note.StartTime.Value,
        ["end"] = note.EndTime.Value,
        ["pitch"] = note.Pitch.Value,
        ["lyric"] = note.Lyric.Value,
        ["body_offset"] = note.BodyOffset.Value,
        ["leading"] = PhonemesLive(note.LeadingPhonemes.Value),
        ["body"] = PhonemesLive(note.BodyPhonemes.Value),
        ["properties"] = JsonNode.Parse("{}"),   // 钉死 phonemes 的 properties 只在快照侧出现（活视图无，见 plugin-development §5.7）
    };

    static JsonArray PhonemesLive(IReadOnlyList<SynthesizedPhoneme> list) => new(
        list.Select(p => (JsonNode)new JsonObject
        {
            ["symbol"] = p.Symbol, ["duration"] = p.Duration, ["stretch_weight"] = p.StretchWeight,
        }).ToArray());

    public static JsonObject SnapshotNote(VoiceSynthesisNoteSnapshot sn) => new()
    {
        ["id"] = sn.Id,
        ["start"] = sn.StartTime,
        ["end"] = sn.EndTime,
        ["pitch"] = sn.Pitch,
        ["lyric"] = sn.Lyric,
        ["body_offset"] = sn.BodyOffset,
        ["leading"] = new JsonArray(sn.LeadingPhonemes.Select(SnapshotPhoneme).ToArray()),
        ["body"] = new JsonArray(sn.BodyPhonemes.Select(SnapshotPhoneme).ToArray()),
        ["properties"] = ScriptedWire.SerializeNode(sn.Properties),
    };

    static JsonObject SnapshotPhoneme(VoiceSynthesisPhonemeSnapshot p) => new()
    {
        ["symbol"] = p.Symbol,
        ["duration"] = p.Duration,
        ["stretch_weight"] = p.StretchWeight,
        ["properties"] = ScriptedWire.SerializeNode(p.Properties),
    };

    public static string NoteFingerprint(IVoiceSynthesisNote n)
        => string.Join('\u2063', n.StartTime.Value.ToString("R"), n.EndTime.Value.ToString("R"), n.Pitch.Value.ToString(),
            n.Lyric.Value, n.BodyOffset.Value.ToString("R"), n.LeadingPhonemes.Value.Count, n.BodyPhonemes.Value.Count);
}
