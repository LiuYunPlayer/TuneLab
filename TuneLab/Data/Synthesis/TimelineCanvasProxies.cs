using System;
using System.Collections.Generic;
using TuneLab.Data.Timing;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data.Synthesis;

// 时间轴画布的会话代理（ISynthesisTimelineCanvas）：值直接读画布表（整值、不拷贝），换算器的原点 = part 原点 + 存储里的偏移。
internal sealed class TimelineCanvasProxy(MidiPart part, TimelineCanvases store, string id) : ISynthesisTimelineCanvas
{
    public PropertyValue Value => store.GetValue(id);

    public ITimeConverter TimeConverter => Converter(part, store, id);

    public IActionEvent<double, double> RangeModified => mRangeModified;

    internal void NotifyRangeModified(double startSecond, double endSecond)
    {
        try { mRangeModified.Invoke(startSecond, endSecond); }
        catch (Exception ex) { Log.ErrorAttributed("Timeline canvas range handler threw", ex); }
    }

    // 快照与活视图共用：当时的速度表快照 + 值的原点（全局 tick）。
    internal static ITimeConverter Converter(MidiPart part, TimelineCanvases store, string id)
        => new LocalTimeConverter(part.TempoManager.CreateSnapshot(), part.Pos.Value + (store.TryGetValue(id, out var entry) ? entry.Offset : 0));

    readonly ActionEvent<double, double> mRangeModified = new();
}

// 一张画布表（part 的或某个 effect 的）对应的会话代理集合：按当前声明给出 map（代理按 id 缓存、保订阅稳定），
// 订阅画布表的写入，把改动范围（part 局部 tick；null = 整条）换成全局秒后交给 forward——由所属上下文决定怎么转（声源经
// ForwardChange 补收口，效果器还要按区间相交标脏）。没被声明的 id（孤儿）不转。
internal sealed class TimelineCanvasProxies : IDisposable
{
    public TimelineCanvasProxies(MidiPart part, TimelineCanvases store, Func<IReadOnlyOrderedMap<PropertyKey, TimelineCanvasConfig>> declared,
        Action<TimelineCanvasProxy, IReadOnlyList<(double Start, double End)>> forward)
    {
        mPart = part;
        mStore = store;
        mDeclared = declared;
        mForward = forward;
        store.CanvasModified.Subscribe(OnCanvasModified, s);
    }

    public IReadOnlyMap<string, ISynthesisTimelineCanvas> Map
    {
        get
        {
            var map = new Map<string, ISynthesisTimelineCanvas>();
            foreach (var kvp in mDeclared())
                map.Add(kvp.Key.Id, ProxyOf(kvp.Key.Id));
            return map;
        }
    }

    // 快照：当前声明的各条取当下的值与换算器。
    public static IReadOnlyMap<string, SynthesisTimelineCanvasSnapshot> Capture(MidiPart part, TimelineCanvases store, IReadOnlyOrderedMap<PropertyKey, TimelineCanvasConfig> declared)
    {
        var map = new Map<string, SynthesisTimelineCanvasSnapshot>();
        foreach (var kvp in declared)
        {
            string id = kvp.Key.Id;
            map.Add(id, new SynthesisTimelineCanvasSnapshot { Value = store.GetValue(id), TimeConverter = TimelineCanvasProxy.Converter(part, store, id) });
        }
        return map;
    }

    TimelineCanvasProxy ProxyOf(string id)
    {
        if (!mProxies.TryGetValue(id, out var proxy))
        {
            proxy = new TimelineCanvasProxy(mPart, mStore, id);
            mProxies.Add(id, proxy);
        }
        return proxy;
    }

    void OnCanvasModified(string id, IReadOnlyList<TickRange>? ranges)
    {
        if (mDisposed || !mDeclared().ContainsKey(id))
            return;

        var seconds = new List<(double Start, double End)>();
        if (ranges == null)
        {
            seconds.Add((double.NegativeInfinity, double.PositiveInfinity));
        }
        else
        {
            foreach (var range in ranges)
                seconds.Add((ToGlobalSecond(range.Start), ToGlobalSecond(range.End)));
        }
        mForward(ProxyOf(id), seconds);
    }

    double ToGlobalSecond(double partTick)
        => double.IsInfinity(partTick) ? partTick : mPart.TempoManager.GetTime(mPart.Pos.Value + partTick);

    public void Dispose()
    {
        mDisposed = true;
        s.DisposeAll();
    }

    readonly MidiPart mPart;
    readonly TimelineCanvases mStore;
    readonly Func<IReadOnlyOrderedMap<PropertyKey, TimelineCanvasConfig>> mDeclared;
    readonly Action<TimelineCanvasProxy, IReadOnlyList<(double Start, double End)>> mForward;
    readonly Dictionary<string, TimelineCanvasProxy> mProxies = new();
    readonly DisposableManager s = new();
    bool mDisposed;
}
