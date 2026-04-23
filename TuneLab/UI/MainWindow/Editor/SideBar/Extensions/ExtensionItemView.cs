using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.UI;

internal class ExtensionItemView : Border
{
    public event Action? UninstallRequested;
    public string ExtensionName { get; }
    public string ExtensionVersion { get; }
    public string ExtensionType { get; }
    public string ExtensionPath { get; }
    public bool IsPendingUninstall { get; private set; }

    public ExtensionItemView(string name, string version, string type, string extensionPath)
    {
        ExtensionName = name;
        ExtensionVersion = version;
        ExtensionType = type;
        ExtensionPath = extensionPath;

        Background = Style.INTERFACE.ToBrush();
        Padding = new Thickness(12, 10);
        BorderBrush = Style.BACK.ToBrush();
        BorderThickness = new Thickness(0, 0, 0, 1);
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        ClipToBounds = true;
        MaxWidth = 280;

        var mainPanel = new DockPanel();

        // Left: Large icon area - dark rounded rectangle with abbreviation text
        var iconSize = 64.0;
        var iconBorder = new Border
        {
            Width = iconSize,
            Height = iconSize,
            CornerRadius = new CornerRadius(8),
            Background = Style.DARK.ToBrush(),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = GetIconText(name),
                FontSize = GetIconFontSize(name),
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            }
        };
        mainPanel.AddDock(iconBorder, Dock.Left);

        // Right side: info + action area
        var rightPanel = new DockPanel
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };

        // Bottom row: Type tag + Uninstall button - aligned to icon bottom edge
        var bottomRow = new DockPanel
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
        };
        {
            // Uninstall button on the right - aligned with type badge
            mUninstallBtnText = new TextBlock
            {
                Text = "Uninstall".Tr(TC.Dialog),
                FontSize = 11,
                Foreground = Style.LIGHT_WHITE.ToBrush(),
            };
            mUninstallBtn = new Border
            {
                Background = Style.BUTTON_NORMAL.ToBrush(),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2),
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = mUninstallBtnText,
            };
            mUninstallBtn.PointerEntered += (s, e) => { if (!IsPendingUninstall) mUninstallBtn.Background = Style.BUTTON_NORMAL_HOVER.ToBrush(); };
            mUninstallBtn.PointerExited += (s, e) => { if (!IsPendingUninstall) mUninstallBtn.Background = Style.BUTTON_NORMAL.ToBrush(); };
            mUninstallBtn.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                if (!IsPendingUninstall)
                    UninstallRequested?.Invoke();
            };
            bottomRow.AddDock(mUninstallBtn, Dock.Right);

            // Type tag on the left - rounded rectangle badge
            var typeBadge = new Border
            {
                Background = Style.BACK.ToBrush(),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = type,
                    FontSize = 11,
                    Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(),
                }
            };
            bottomRow.AddDock(typeBadge);
        }
        rightPanel.AddDock(bottomRow, Dock.Bottom);

        // Top area: Name + Version + empty description
        var topPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };

        // Row 1: Name + Version badge (Grid for proper text truncation)
        var nameRow = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        nameRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        {
            // Extension name
            var nameBlock = new TextBlock
            {
                Text = name,
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                Foreground = Style.TEXT_LIGHT.ToBrush(),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(nameBlock, 0);
            nameRow.Children.Add(nameBlock);

            // Version badge on the right
            var versionBadge = new Border
            {
                Background = Style.BACK.ToBrush(),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 2),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "v" + version,
                    FontSize = 11,
                    Foreground = Style.LIGHT_WHITE.Opacity(0.7).ToBrush(),
                }
            };
            Grid.SetColumn(versionBadge, 1);
            nameRow.Children.Add(versionBadge);
        }
        topPanel.Children.Add(nameRow);

        // Row 2: Description (empty for now)
        var descBlock = new TextBlock
        {
            Text = "",
            FontSize = 12,
            Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        topPanel.Children.Add(descBlock);

        rightPanel.AddDock(topPanel);
        mainPanel.AddDock(rightPanel);
        Child = mainPanel;
    }

    private static string GetIconText(string name)
    {
        if (name.Length <= 5)
            return name;

        var words = name.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
            return (words[0][..1] + words[1][..1]).ToUpperInvariant();

        return name[..1].ToUpperInvariant();
    }

    private static double GetIconFontSize(string name)
    {
        var text = GetIconText(name);
        if (text.Length <= 1) return 28;
        if (text.Length <= 2) return 22;
        if (text.Length <= 3) return 18;
        if (text.Length <= 4) return 15;
        return 13;
    }

    public void MarkPendingUninstall()
    {
        if (IsPendingUninstall)
            return;

        IsPendingUninstall = true;
        mUninstallBtn.Background = Style.BACK.ToBrush();
        mUninstallBtn.Cursor = Cursor.Default;
        mUninstallBtnText.Text = "Pending Uninstall".Tr(TC.Dialog);
        mUninstallBtnText.Foreground = Style.LIGHT_WHITE.Opacity(0.4).ToBrush();
    }

    readonly Border mUninstallBtn;
    readonly TextBlock mUninstallBtnText;
}
