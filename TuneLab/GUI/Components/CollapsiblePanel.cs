using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.GUI.Components;

internal class CollapsiblePanel : StackPanel
{
    public Control? Title
    {
        get => mTitlePanel.Children.FirstOrDefault();
        set { mTitlePanel.Children.Clear(); if (value != null) mTitlePanel.Children.Add(value); }
    }

    public Control? Content
    {
        get => mContentPanel.Children.FirstOrDefault();
        set { mContentPanel.Children.Clear(); if (value != null) mContentPanel.Children.Add(value); }
    }

    public CollapsiblePanel()
    {
        Children.Add(mTitlePanel);
        Children.Add(mContentPanel);

        mTitlePanel.PointerPressed += (s, e) => { mContentPanel.IsVisible = !mContentPanel.IsVisible; };
    }

    LayerPanel mTitlePanel = new();
    LayerPanel mContentPanel = new();
}
