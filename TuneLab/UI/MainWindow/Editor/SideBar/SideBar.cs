using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI.Components;
using TuneLab.GUI;
using TuneLab.Utils;

namespace TuneLab.UI;

internal class SideBar : DockPanel
{
    public struct SideBarContent
    {
        public IImage Icon;
        public string Name;
        public IEnumerable<Control> Items;
    }

    public SideBar()
    {
        Focusable = true;
        IsTabStop = false;
        var title = new DockPanel() { Height = 48, Background = Style.INTERFACE.ToBrush(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        {
            mIcon = new Image() { Width = 24, Height = 24, Margin = new(24, 12, 16, 12) };
            title.AddDock(mIcon, Dock.Left);
            mName = new Label() { FontSize = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.TEXT_LIGHT.ToBrush() };
            title.AddDock(mName);
        }
        this.AddDock(title, Dock.Top);
        this.AddDock(new Border() { Height = 1, Background = Style.BACK.ToBrush() }, Dock.Top);
        this.AddDock(mListView);

        // 底部占位：高度跟随视口（ListView）高，把内容的滚动范围撑大一整屏，使折叠面板展开/收起时
        // 不撞底、不猛弹，任意面板都能滚到视口顶。ListView 填满 dock 区，其 Bounds.Height 即视口高。
        mListView.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
                mBottomSpacer.Height = mListView.Bounds.Height;
        };
    }

    public void SetContent(SideBarContent content)
    {
        mIcon.Source = content.Icon;
        mName.Content = content.Name;
        mListView.Content.Children.Clear();
        foreach (var child in content.Items)
        {
            mListView.Content.Children.Add(child);
        }
        mListView.Content.Children.Add(mBottomSpacer);
    }

    readonly Image mIcon;
    readonly Label mName;
    readonly ListView mListView = new();
    // 透明背景而非 null：否则空控件不参与命中测试，鼠标在其上时滚轮事件无目标、滚不动。
    readonly Border mBottomSpacer = new() { Background = Brushes.Transparent };
}
