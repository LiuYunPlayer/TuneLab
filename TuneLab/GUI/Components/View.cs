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
using TuneLab.Base.Utils;

namespace TuneLab.GUI.Components;

internal class View : Container
{
    public View()
    {
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
