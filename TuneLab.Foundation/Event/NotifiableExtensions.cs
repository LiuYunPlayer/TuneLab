namespace TuneLab.Foundation;

public static class NotifiableExtensions
{
    // 订阅"集合任一现有/未来成员"：当下成员逐个 subscribe，此后增删自动接线/退订；
    // 返回的 IDisposable 一次性整体退订（集合结构事件 + 全部成员订阅）。
    // 宿主与插件共用这一份实现，杜绝两套成员生命周期管理漂移。
    public static IDisposable WhenAny<TItem>(this IReadOnlyNotifiableCollection<TItem> list,
        Action<TItem> subscribe, Action<TItem> unsubscribe)
    {
        return new ListSubscription<TItem>(list, subscribe, unsubscribe);
    }

    sealed class ListSubscription<TItem> : IDisposable
    {
        public ListSubscription(IReadOnlyNotifiableCollection<TItem> list, Action<TItem> subscribe, Action<TItem> unsubscribe)
        {
            mList = list;
            mSubscribe = subscribe;
            mUnsubscribe = unsubscribe;
            foreach (var item in list)
            {
                mSubscribe(item);
                mItems.Add(item);
            }
            mList.ItemAdded += OnItemAdded;
            mList.ItemRemoved += OnItemRemoved;
        }

        public void Dispose()
        {
            mList.ItemAdded -= OnItemAdded;
            mList.ItemRemoved -= OnItemRemoved;
            foreach (var item in mItems)
            {
                mUnsubscribe(item);
            }
            mItems.Clear();
        }

        void OnItemAdded(TItem item)
        {
            mSubscribe(item);
            mItems.Add(item);
        }

        void OnItemRemoved(TItem item)
        {
            mUnsubscribe(item);
            mItems.Remove(item);
        }

        readonly IReadOnlyNotifiableCollection<TItem> mList;
        readonly Action<TItem> mSubscribe;
        readonly Action<TItem> mUnsubscribe;
        readonly List<TItem> mItems = [];
    }
}
