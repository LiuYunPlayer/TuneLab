using System;
using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data.Synthesis;

// 合成 context 对其会话级活视图代理（DerivedProperty / PropertyObjectGuard / AutomationProxy）暴露的转发面：
// voice 的 SynthesisContext 与 instrument 的 InstrumentSynthesisContext 都实现，使这些领域中性的代理两边共用。
// 各 context 自己实现 ForwardChange/ForwardWill（含各自的 Committed 收口与批量括号语义）；代理只管通过此面转发。
internal interface ISynthesisForwarder
{
    // 数据线程纪律断言（DEBUG 期生效、Release 空转——见各 context 实现转发到 [Conditional] 方法）。
    void AssertThread();
    // 改后类通知统一转发口（发完补 Committed 收口；批量中由批量结束统一收口）。
    void ForwardChange(Action raise);
    // 改前事件直转（不入批量括号）。
    void ForwardWill(Action raise);
    // 全局秒 → part 相对 tick（AutomationProxy 求值用）：全局秒 → 全局 tick → 减 part 偏移。
    double[] ToRelativeTicks(IReadOnlyList<double> times);
}

// —— 派生只读属性：借壳一个或多个数据层源（最小订阅面）的改前/改后事件，
//    值即时从 getter 计算；事件字段在本对象上（短命），源订阅随 Dispose 拆除。 ——
internal sealed class DerivedProperty<T> : IReadOnlyNotifiableProperty<T>, IDisposable
{
    public event Action? WillModify;
    public event Action? Modified;
    public T Value
    {
        get
        {
            mForwarder.AssertThread();
            return mGetter();
        }
    }

    public DerivedProperty(ISynthesisForwarder forwarder, Func<T> getter, params IReadOnlyNotifiable[] sources)
    {
        mForwarder = forwarder;
        mGetter = getter;
        mSources = sources;
        mOnWillModify = () => mForwarder.ForwardWill(() => WillModify?.Invoke());
        mOnModified = () => mForwarder.ForwardChange(() => Modified?.Invoke());
        foreach (var source in mSources)
        {
            source.WillModify += mOnWillModify;
            source.Modified += mOnModified;
        }
    }

    public void Dispose()
    {
        foreach (var source in mSources)
        {
            source.WillModify -= mOnWillModify;
            source.Modified -= mOnModified;
        }
    }

    readonly ISynthesisForwarder mForwarder;
    readonly Func<T> mGetter;
    readonly IReadOnlyNotifiable[] mSources;
    readonly Action mOnWillModify;
    readonly Action mOnModified;
}

// —— 属性树守卫：把宿主长寿 property object 包成会话级只读外观。
//    导航逐层包裹（per key 缓存）、读值直透、事件 re-raise 到自身字段。 ——
internal sealed class PropertyObjectGuard : IReadOnlyNotifiablePropertyObject, IDisposable
{
    public event Action? WillModify;
    public event Action? Modified;

    public PropertyObjectGuard(ISynthesisForwarder forwarder, IReadOnlyNotifiablePropertyObject source)
    {
        mForwarder = forwarder;
        mSource = source;
        mOnWillModify = () => mForwarder.ForwardWill(() => WillModify?.Invoke());
        mOnModified = () => mForwarder.ForwardChange(() => Modified?.Invoke());
        mSource.WillModify += mOnWillModify;
        mSource.Modified += mOnModified;
    }

    public IReadOnlyNotifiablePropertyObject Object(string key)
    {
        if (!mChildren.TryGetValue(key, out var child))
        {
            child = new PropertyObjectGuard(mForwarder, mSource.Object(key));
            mChildren.Add(key, child);
        }
        return child;
    }

    public PropertyValue GetValue(string key, PropertyValue defaultValue)
    {
        mForwarder.AssertThread();
        return mSource.GetValue(key, defaultValue);
    }

    public void Dispose()
    {
        mSource.WillModify -= mOnWillModify;
        mSource.Modified -= mOnModified;
        foreach (var kvp in mChildren)
        {
            kvp.Value.Dispose();
        }
        mChildren.Clear();
    }

    readonly ISynthesisForwarder mForwarder;
    readonly IReadOnlyNotifiablePropertyObject mSource;
    readonly Action mOnWillModify;
    readonly Action mOnModified;
    readonly Dictionary<string, PropertyObjectGuard> mChildren = new();
}

// —— 曲线类的会话级活视图：取值经 sampler 委托（part 相对 tick 轴）回宿主数据层，
//    秒↔tick 换算与 part 偏移由 forwarder.ToRelativeTicks 在边界完成；区间事件由 context 换算成全局秒后注入。 ——
internal sealed class AutomationProxy(ISynthesisForwarder forwarder, Func<IReadOnlyList<double>, double[]> sampler) : ILiveAutomation
{
    public event Action<double, double>? RangeModified;

    // 查询轴 = 全局秒：全局秒 → part 相对 tick → 喂数据层 sampler。
    public double[] Evaluate(IReadOnlyList<double> times)
    {
        forwarder.AssertThread();
        return sampler(forwarder.ToRelativeTicks(times));
    }

    internal void NotifyRangeModified(double startSecond, double endSecond)
    {
        forwarder.ForwardChange(() => RangeModified?.Invoke(startSecond, endSecond));
    }
}
