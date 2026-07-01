using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI.Input;
using TuneLab.Foundation;
using TuneLab.Utils;

using Point = Avalonia.Point;

namespace TuneLab.GUI.Components;

internal class View : Container
{
    public View()
    {
        // 视图被标脏后，异步用「当前鼠标位置」补派一次相对 Move：本意是 scrollview 滚动/重布局后鼠标没动、
        // 但鼠标相对内容的位置变了的场景，让进行中的操作（resize/draw/move）继续跟随内容。
        // 副作用：任何使视图变脏的数据变更（创建音符/颤音、合成进度、播放头推进…）都会重派一次 Move，
        // 故所有 OnMouseRelativeMoveToView 的操作 Move 必须对「同位置重复调用」幂等（现有 op 靠 DiscardTo(mHead)+绝对位置重算满足）。
        // 潜在竞态（待统一优化）：靠"创建→变脏→这次补派的 Move 使 head 前进→松手时 Commit"来留存离散动作（如单击创建颤音并接拖拽），
        // 依赖这条异步 post 在 mouse-up 之前执行；理论上若 up 抢先则会走 Discard 把刚创建的对象丢掉。日常手速下不触发，
        // 后续应改为离散动作显式提交、不依赖刷新驱动的 Move。
        mDirtyHandler.OnDirty += () =>
        {
            InvalidateVisual();
            Dispatcher.UIThread.Post(() =>
            {
                CallOnMouseRelativeMoveToView(new()
                {
                    KeyModifiers = Modifiers,
                    Position = MousePosition
                });
                mDirtyHandler.Reset();
            }, DispatcherPriority.Normal);
        };
    }

    protected virtual void OnRender(DrawingContext context) { }
    protected virtual void OnMouseAbsoluteMove(MouseMoveEventArgs e) { }
    protected virtual void OnMouseRelativeMoveToView(MouseMoveEventArgs e) { }
    protected virtual void UpdateItems(IItemCollection items) { }

    public sealed override void Render(DrawingContext context)
    {
        mDetectedHover = false;
        mItemCollection.Clear();
        UpdateItems(mItemCollection);
        OnRender(context);
        foreach (var item in mItemCollection)
        {
            item.Render(context);
        }
    }

    protected sealed override void OnMouseMove(MouseMoveEventArgs e)
    {
        OnMouseAbsoluteMove(e);
        CallOnMouseRelativeMoveToView(e);
    }

    protected void Update()
    {
        mDirtyHandler.SetDirty();
    }

    protected Item? HoverItem()
    {
        if (!IsHover)
            return null;

        if (!this.Rect().Contains(MousePosition))
            return null;

        if (!mDetectedHover)
        {
            mHoverItem = ItemAt(MousePosition);
            mDetectedHover = true;
        }

        return mHoverItem;
    }

    bool mDetectedHover = false;
    Item? mHoverItem;

    protected Item? ItemAt(Point point)
    {
        for (int i = mItemCollection.Count - 1; i >= 0; i--)
        {
            var item = mItemCollection[i];
            if (item.Raycast(point))
                return item;
        }
        
        return null;
    }

    void CallOnMouseRelativeMoveToView(MouseMoveEventArgs e)
    {
        OnMouseRelativeMoveToView(e);
    }

    protected class Item
    {
        public virtual bool Raycast(Point point)
        {
            return false;
        }

        public virtual void Render(DrawingContext context) { }
    }

    protected interface IItemCollection : IEnumerable<Item>
    {
        void Add(Item item);
    }

    class ItemCollection : List<Item>, IItemCollection { }

    protected IEnumerable<Item> Items => mItemCollection;

    readonly ItemCollection mItemCollection = new();
    readonly DirtyHandler mDirtyHandler = new();
}
