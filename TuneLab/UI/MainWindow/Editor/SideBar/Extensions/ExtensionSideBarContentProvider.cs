using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using TuneLab.Extensions;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Voices;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.UI;

internal class ExtensionSideBarContentProvider : ISideBarContentProvider
{
    public event Action? InstallRequested;

    public SideBar.SideBarContent Content => new()
    {
        Icon = Assets.Extensions.GetImage(Style.LIGHT_WHITE),
        Name = "Extensions".Tr(TC.Dialog),
        Items = [mContentPanel],
    };

    public ExtensionSideBarContentProvider()
    {
        mContentPanel.Orientation = Orientation.Vertical;
        mContentPanel.MaxWidth = 280;
        mContentPanel.ClipToBounds = true;

        // Search bar area
        var searchPanel = new Border
        {
            Padding = new Thickness(12, 8),
            Background = Style.INTERFACE.ToBrush(),
        };
        {
            var searchBox = new TextInput
            {
                Watermark = "Search Extensions...".Tr(TC.Dialog),
                Height = 28,
            };
            searchBox.TextChanged.Subscribe(() => FilterExtensions(searchBox.Text));
            mSearchBox = searchBox;
            searchPanel.Child = searchBox;
        }
        mContentPanel.Children.Add(searchPanel);
        mContentPanel.Children.Add(new Border { Height = 1, Background = Style.BACK.ToBrush() });

        // Extension count label
        mCountLabel = new TextBlock
        {
            FontSize = 11,
            Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
            Margin = new Thickness(12, 6),
        };
        mContentPanel.Children.Add(mCountLabel);
        mContentPanel.Children.Add(new Border { Height = 1, Background = Style.BACK.ToBrush() });

        // Extension list container
        mExtensionListPanel = new StackPanel { Orientation = Orientation.Vertical };
        mContentPanel.Children.Add(mExtensionListPanel);

        // Bottom area: Open Extensions Folder + Refresh buttons
        var bottomPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(12, 8),
        };

        var installBtn = CreateBottomButton("Install Extension".Tr(TC.Dialog));
        installBtn.PointerPressed += (s, e) =>
        {
            e.Handled = true;
            InstallRequested?.Invoke();
        };
        bottomPanel.Children.Add(installBtn);

        var openFolderBtn = CreateBottomButton("Open Extensions Folder".Tr(TC.Dialog));
        openFolderBtn.PointerPressed += (s, e) =>
        {
            e.Handled = true;
            OpenExtensionsFolder();
        };
        bottomPanel.Children.Add(openFolderBtn);

        mContentPanel.Children.Add(new Border { Height = 1, Background = Style.BACK.ToBrush(), Margin = new Thickness(0, 4, 0, 0) });
        mContentPanel.Children.Add(bottomPanel);

        // Initial load
        RefreshExtensions();
    }

    public void RefreshExtensions()
    {
        mAllExtensions.Clear();
        ScanExtensions();
        FilterExtensions(mSearchBox?.Text ?? string.Empty);
    }

    private void ScanExtensions()
    {
        var extensionsFolder = PathManager.ExtensionsFolder;
        if (!Directory.Exists(extensionsFolder))
            return;

        var voiceEngineTypes = VoicesManager.GetAllVoiceEngines().ToHashSet();
        var importFormats = FormatsManager.GetAllImportFormats().ToHashSet();
        var exportFormats = FormatsManager.GetAllExportFormats().ToHashSet();

        foreach (var dir in Directory.GetDirectories(extensionsFolder))
        {
            var folderName = Path.GetFileName(dir);
            var name = folderName;
            var version = "1.0.0";
            var type = "Extension";

            // Try to read description.json
            var descPath = Path.Combine(dir, "description.json");
            if (File.Exists(descPath))
            {
                try
                {
                    var json = File.ReadAllText(descPath);
                    var desc = JsonSerializer.Deserialize<ExtensionDescription>(json);
                    if (desc != null)
                    {
                        if (!string.IsNullOrEmpty(desc.name))
                            name = desc.name;
                        if (!string.IsNullOrEmpty(desc.version))
                            version = desc.version;
                    }
                }
                catch { }
            }

            // Determine type based on loaded engines/formats
            if (voiceEngineTypes.Contains(name) || voiceEngineTypes.Contains(folderName))
            {
                type = "Voice Engine";
            }
            else
            {
                // Check if any format assemblies were loaded from this folder
                type = DetectExtensionType(dir);
            }

            var itemView = new ExtensionItemView(name, version, type, dir);
            itemView.UninstallRequested += () => OnUninstallExtension(itemView);
            if (ExtensionManager.PendingUninstalls.Contains(dir))
                itemView.MarkPendingUninstall();
            mAllExtensions.Add(itemView);
        }
    }

    private string DetectExtensionType(string extensionDir)
    {
        // Check for DLLs that might indicate voice or format extension
        var descPath = Path.Combine(extensionDir, "description.json");
        if (File.Exists(descPath))
        {
            try
            {
                var json = File.ReadAllText(descPath);
                var lower = json.ToLowerInvariant();
                if (lower.Contains("voice") || lower.Contains("engine") || lower.Contains("synth"))
                    return "Voice Engine";
                if (lower.Contains("format") || lower.Contains("import") || lower.Contains("export"))
                    return "Format";
            }
            catch { }
        }

        return "Extension";
    }

    private void FilterExtensions(string searchText)
    {
        mExtensionListPanel.Children.Clear();

        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? mAllExtensions
            : mAllExtensions.Where(e =>
                e.ExtensionName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                e.ExtensionType.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var ext in filtered)
        {
            mExtensionListPanel.Children.Add(ext);
        }

        UpdateCountLabel(filtered.Count, mAllExtensions.Count);
    }

    private void UpdateCountLabel(int shown, int total)
    {
        if (shown == total)
            mCountLabel.Text = string.Format("{0} extension(s) installed", total);
        else
            mCountLabel.Text = string.Format("{0} of {1} extension(s)", shown, total);
    }

    private async void OnUninstallExtension(ExtensionItemView itemView)
    {
        // We delegate the actual deletion to ExtensionInstaller, which waits
        // for TuneLab to exit (lock file released) before deleting the folder.
        try
        {
            var topLevel = TopLevel.GetTopLevel(mContentPanel);
            if (topLevel is Avalonia.Controls.Window window)
            {
                var name = itemView.ExtensionName;
                var dirPath = itemView.ExtensionPath;

                var dialog = new TuneLab.GUI.Dialog();
                dialog.SetTitle("Uninstall Extension".Tr(TC.Dialog));
                dialog.SetMessage(string.Format("The extension \"{0}\" will be uninstalled after the editor is closed.\nWould you like to restart now?", name));
                dialog.AddButton("Cancel".Tr(TC.Dialog), TuneLab.GUI.Dialog.ButtonType.Normal);
                dialog.AddButton("Later".Tr(TC.Dialog), TuneLab.GUI.Dialog.ButtonType.Normal).Clicked += () =>
                {
                    // Record the extension for uninstall when TuneLab exits naturally.
                    ExtensionManager.AddPendingUninstall(dirPath);
                    itemView.MarkPendingUninstall();
                };
                dialog.AddButton("Restart".Tr(TC.Dialog), TuneLab.GUI.Dialog.ButtonType.Primary).Clicked += () =>
                {
                    // Mark for uninstall + set restart flag; the actual
                    // ExtensionInstaller launch happens in desktop.Exit.
                    ExtensionManager.AddPendingUninstall(dirPath);
                    ExtensionManager.RestartAfterUninstall = true;
                    itemView.MarkPendingUninstall();

                    // Close the app so the exit handler fires
                    window.Close();
                };
                await dialog.ShowDialog(window);
            }
        }
        catch { }
    }

    private Border CreateBottomButton(string text)
    {
        var btn = new Border
        {
            Background = Style.BUTTON_NORMAL.ToBrush(),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(0, 6),
            Margin = new Thickness(0, 3),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = Style.TEXT_LIGHT.ToBrush(),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            }
        };
        btn.PointerEntered += (s, e) => btn.Background = Style.BUTTON_NORMAL_HOVER.ToBrush();
        btn.PointerExited += (s, e) => btn.Background = Style.BUTTON_NORMAL.ToBrush();
        return btn;
    }

    private void OpenExtensionsFolder()
    {
        try
        {
            PathManager.MakeSureExist(PathManager.ExtensionsFolder);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start("explorer.exe", PathManager.ExtensionsFolder);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", PathManager.ExtensionsFolder);
            else
                Process.Start("xdg-open", PathManager.ExtensionsFolder);
        }
        catch { }
    }

    private readonly StackPanel mContentPanel = new();
    private readonly StackPanel mExtensionListPanel = new();
    private readonly TextBlock mCountLabel;
    private readonly TextInput mSearchBox;
    private readonly List<ExtensionItemView> mAllExtensions = new();
}
