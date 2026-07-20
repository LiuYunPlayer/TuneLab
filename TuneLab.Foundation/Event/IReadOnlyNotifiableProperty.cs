namespace TuneLab.Foundation;

// 叶子属性面：事件 + 廉价可读值。只有"读值是 O(1) 属性"的叶子才实现到这一层——
// 复合对象（值 = 整棵 info 树）停在 IReadOnlyNotifiable，不把 O(n) 序列化伪装成属性 getter。
public interface IReadOnlyNotifiableProperty<out T> : IReadOnlyNotifiable
{
    T Value { get; }
}
