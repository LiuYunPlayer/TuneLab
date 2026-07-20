namespace TuneLab.Foundation;

// 可索引特化：仅当背后数据结构真能 O(1) 随机访问时实现到这一层。
public interface IReadOnlyNotifiableList<out T> : IReadOnlyNotifiableCollection<T>, IReadOnlyList<T>
{
}
