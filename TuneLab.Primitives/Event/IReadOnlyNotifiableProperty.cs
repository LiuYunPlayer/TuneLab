namespace TuneLab.Primitives.Event;

// 跨 SDK 边界的最小订阅侧契约：可读值 + 改前/改后事件。
// - WillModified 在值变更前触发，handler 内读 Value 得旧值（用于作废"被腾空的旧区域"）；
//   Modified 在变更后触发，读 Value 得新值。
// - 事件恒在数据线程触发与处理，订阅者只做廉价记录/标脏；本接口不承诺任何跨线程能力
//   （可跨线程的是无事件的纯值快照，不是它）。
// - 宿主数据层的富属性实现本接口；插件经会话级 context 订阅，不直接触及宿主长寿对象。
public interface IReadOnlyNotifiableProperty<out T>
{
    T Value { get; }
    event Action? WillModified;
    event Action? Modified;
}
