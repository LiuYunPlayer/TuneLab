namespace TuneLab.Foundation;

// 跨 SDK 边界的最小订阅侧契约（事件面）：改前/改后事件。
// - WillModify 在变更前触发，handler 内读值得旧值（用于作废"被腾空的旧区域"）；
//   Modified 在变更后触发，读值得新值。
// - 事件恒在数据线程触发与处理，订阅者只做廉价记录/标脏；本接口不承诺任何跨线程能力
//   （可跨线程的是无事件的纯值快照，不是它）。
// - 宿主数据层对象统一实现本接口（一切数据对象天生可订阅）；插件经会话级 context 订阅，
//   不直接触及宿主长寿对象。
public interface IReadOnlyNotifiable
{
    IActionEvent WillModify { get; }
    IActionEvent Modified { get; }
}

// 叶子属性面：事件 + 廉价可读值。只有"读值是 O(1) 属性"的叶子才实现到这一层——
// 复合对象（值 = 整棵 info 树）停在 IReadOnlyNotifiable，不把 O(n) 序列化伪装成属性 getter。
public interface IReadOnlyNotifiableProperty<out T> : IReadOnlyNotifiable
{
    T Value { get; }
}
