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
        this.AddDock(mPageHost);
    }

    // 每页（key 区分）一个独立 ListView，各自持有自己的 ScrollView / 滚动轴；切页只切可见性。
    // 故各页滚动位置天然各记各的、互不共享，无需存取偏移、也无切页布局时序问题。
    // 内容控件是 provider 的稳定成员（只重挂载、不重建），故每页只在首次填充一次。
    public void SetContent(SideBarTab key, SideBarContent content)
    {
        mIcon.Source = content.Icon;
        mName.Content = content.Name;

        if (!mPages.TryGetValue(key, out var page))
        {
            page = new ListView() { IsVisible = false };

            // 底部占位：高度跟随该页视口高，把滚动范围撑大一整屏，使折叠面板展开/收起不撞底、任意面板能滚到顶。
            // 透明背景而非 null：否则空控件不参与命中测试，滚轮事件无目标。每页各一个。
            var spacer = new Border() { Background = Brushes.Transparent };
            page.PropertyChanged += (_, e) =>
            {
                if (e.Property == BoundsProperty)
                    spacer.Height = page.Bounds.Height;
            };

            foreach (var child in content.Items)
                page.Content.Children.Add(child);
            page.Content.Children.Add(spacer);

            mPageHost.Children.Add(page);
            mPages.Add(key, page);
        }

        if (!ReferenceEquals(mCurrent, page))
        {
            if (mCurrent != null)
                mCurrent.IsVisible = false;
            page.IsVisible = true;
            mCurrent = page;
        }
    }

    readonly Image mIcon;
    readonly Label mName;
    // 填充区承载所有页（Panel 叠放），仅当前页可见；隐藏页保留各自滚动状态。
    readonly Panel mPageHost = new();
    readonly Dictionary<SideBarTab, ListView> mPages = new();
    ListView? mCurrent;
}
