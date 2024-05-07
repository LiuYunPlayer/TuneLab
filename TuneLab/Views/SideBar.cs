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

namespace TuneLab.Views;

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
        var title = new DockPanel() { Height = 48, Background = Style.INTERFACE.ToBrush(), Margin = new(1, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        {
            var border = new Border() { Height = 1, Background = Style.BACK.ToBrush() };
            title.Children.Add(border);
            DockPanel.SetDock(border, Dock.Bottom);
            mIcon = new Image() { Width = 16, Height = 16, Margin = new(24, 16, 16, 16) };
            title.Children.Add(mIcon);
            DockPanel.SetDock(mIcon, Dock.Left);
            mName = new Label() { FontSize = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.TEXT_LIGHT.ToBrush() };
            title.Children.Add(mName);
        }
        Children.Add(title);
        DockPanel.SetDock(title, Dock.Top);

        mListView.Content.Margin = new Thickness(1, 0);
        Children.Add(mListView);
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
    }

    readonly Image mIcon;
    readonly Label mName;
    readonly ListView mListView = new();
}
