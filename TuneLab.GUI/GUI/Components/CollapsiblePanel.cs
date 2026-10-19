using Avalonia.Controls;
using Avalonia.Media;
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

    // 展开状态及其变更（标题上的三角据此转向，见 SideBarSection）。点标题与代码设置都会触发 ExpandedChanged。
    public bool IsExpanded
    {
        get => mContentPanel.IsVisible;
        set
        {
            if (value == mContentPanel.IsVisible)
                return;
            mContentPanel.IsVisible = value;
            ExpandedChanged?.Invoke();
        }
    }
    public event Action? ExpandedChanged;

    public CollapsiblePanel()
    {
        Children.Add(mTitlePanel);
        Children.Add(mContentPanel);

        mTitlePanel.PointerPressed += (s, e) => IsExpanded = !IsExpanded;
    }

    LayerPanel mTitlePanel = new() { Background = Brushes.Transparent } ;
    LayerPanel mContentPanel = new();
}
