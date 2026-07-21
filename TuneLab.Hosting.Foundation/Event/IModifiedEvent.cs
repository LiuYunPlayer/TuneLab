using System;
using System.Collections.Generic;
using System.Linq;

namespace TuneLab.Foundation;

// 数据层修改事件：单一事件源、两张脸。
// - 默认脸（结果态）：本接口 IS-A IActionEvent，无参 Subscribe(Action) 只在结果态触发（canIgnore==false），
//   与旧 Modified.Subscribe(()=>…) 语义一致；亦是跨 SDK 边界的最小面。
// - 全量脸（AsEverytime）：同一事件以 IActionEvent<bool> 示人，每次都触发，参数即 canIgnore
//   （true=可忽略的调整中间态，false=结果态），订一次拿全信息。出参给具象的 IActionEvent<bool>（非基类
//   IEvent<Action<bool>>）守「冻结 ABI 出参尽量高级」——它能喂进要求 IActionEvent<bool> 的接口，调用方亦可自由上转。
// 之所以用 AsEverytime() 方法返回 IActionEvent<bool>、而非让本接口继承 IActionEvent<bool>：
// 后者会令本接口同时实现两个 IEvent<>，使 When/Any 的 IEvent<TEvent> 类型推断产生歧义。
// 方法形让全量脸成为一等可组合事件（直接进 When/Any/WhenAny），且两脸订阅退订均走原生委托身份、无过滤包装。
public interface IModifiedEvent : IActionEvent
{
    IActionEvent<bool> AsEverytime();
}

public static class IModifiedEventExtensions
{
    // 合并多个 IModifiedEvent 成一个：结果态扇出到各成员无参面；全量脸 = 各成员 AsEverytime() 经通用 Merge 合并。
    public static IModifiedEvent MergeModified(this IEnumerable<IModifiedEvent> sources) => new Merged(sources);

    // 构造时把 sources 固化为数组（同 IEventExtensions.Merge 纪律）：MergeModified 取一批固定源的快照，
    // 结果态 Subscribe/Unsubscribe 与全量脸 AsEverytime 须枚举到同一批源，否则持惰性查询两次枚举不同 → 退订失配泄漏。
    // 需要「动态成员、增删自动接线」用 IReadOnlyNotifiableEnumerable.WhenAny/WhenAnyItem，不要往此塞惰性查询。
    sealed class Merged : IModifiedEvent
    {
        public Merged(IEnumerable<IModifiedEvent> sources) => mSources = sources.ToArray();
        public void Subscribe(Action invokable) { foreach (var source in mSources) source.Subscribe(invokable); }
        public void Unsubscribe(Action invokable) { foreach (var source in mSources) source.Unsubscribe(invokable); }
        public IActionEvent<bool> AsEverytime() => mSources.Select(source => source.AsEverytime()).Merge();
        readonly IModifiedEvent[] mSources;
    }
}
