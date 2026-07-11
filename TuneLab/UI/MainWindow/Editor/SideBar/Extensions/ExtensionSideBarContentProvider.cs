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
using TuneLab.SDK;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;

using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Voices;
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
        mContentPanel.ClipToBounds = true;
        // 内容区底色与搜索栏一致（INTERFACE），使按钮下方的列表区不再露出更暗的宿主背景。
        mContentPanel.Background = Style.INTERFACE.ToBrush();

        // 列表宽度优先于 item：ScrollView 用无限宽测量，item 会按内容自然全宽算 desired 而撑宽列表。
        // 以内容面板实测宽（= 侧栏宽，由 ListView FitWidth 排布保证）作为每个 item 的 MaxWidth，在 measure 期就钉死宽度，
        // 名称等随之省略、不再撑宽列表；侧栏拖宽即时更新。
        mContentPanel.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Visual.BoundsProperty)
                return;
            mItemMaxWidth = mContentPanel.Bounds.Width;
            foreach (var c in mExtensionListPanel.Children)
                c.MaxWidth = mItemMaxWidth;
        };

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

        // Action area: Install Extension + Open Extensions Folder buttons
        var actionPanel = new StackPanel
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
        actionPanel.Children.Add(installBtn);

        var openFolderBtn = CreateBottomButton("Open Extensions Folder".Tr(TC.Dialog));
        openFolderBtn.PointerPressed += (s, e) =>
        {
            e.Handled = true;
            OpenExtensionsFolder();
        };
        actionPanel.Children.Add(openFolderBtn);

        mContentPanel.Children.Add(actionPanel);
        mContentPanel.Children.Add(new Border { Height = 1, Background = Style.BACK.ToBrush() });

        // Extension count label（放在安装/打开文件夹按钮栏之下）；整条 BACK 底色（Padding 取代 Margin 使深色铺满）。
        mCountLabel = new TextBlock
        {
            FontSize = 11,
            Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
            Background = Style.BACK.ToBrush(),
            Padding = new Thickness(12, 6),
        };
        mContentPanel.Children.Add(mCountLabel);
        mContentPanel.Children.Add(new Border { Height = 1, Background = Style.BACK.ToBrush() });

        // Extension list container
        mExtensionListPanel = new StackPanel { Orientation = Orientation.Vertical };
        mContentPanel.Children.Add(mExtensionListPanel);

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
        // 直接消费 ExtensionManager 的结构化加载结果，不再重复解析 manifest.json
        // 或靠字符串匹配猜类型——类型/名称/版本/代际都来自真实加载结果。
        // 声明了扩展设置的包 id 集合（决定卡片/详情窗是否显示齿轮）。一次取用、逐项判成员即可。
        var settingsPackages = ExtensionSettingsManager.GetEntries().Select(e => e.PackageId).ToHashSet();

        foreach (var result in ExtensionManager.LoadResults)
        {
            bool hasSettings = !string.IsNullOrEmpty(result.Id) && settingsPackages.Contains(result.Id);
            var itemView = new ExtensionItemView(result.Name, result.Version, DisplayTypes(result), result.Author, result.Description, result.IconPath, result.DirectoryPath, result.Status, result.Error, hasSettings);
            itemView.UninstallRequested += () => OnUninstallExtension(itemView);
            itemView.CancelUninstallRequested += () => OnCancelUninstall(itemView);
            itemView.OpenDetailRequested += () => OnOpenDetail(result);
            itemView.OpenSettingsRequested += () => OnOpenSettings(result);
            if (ExtensionManager.PendingUninstalls.Contains(result.DirectoryPath))
                itemView.MarkPendingUninstall();
            mAllExtensions.Add(itemView);
        }
    }

    // 展示用的类别列表（每项渲染成一枚徽标）。无真实类别时退回单项占位。
    private static IReadOnlyList<string> DisplayTypes(ExtensionLoadResult result)
    {
        if (result.Types.Count > 0)
            return result.Types.Select(Capitalize).ToList();

        return [result.Generation == ExtensionGeneration.Legacy ? "Legacy" : "Extension"];
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

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
            ext.MaxWidth = mItemMaxWidth; // 钉死 ≤ 列表宽，避免内容撑宽列表
            mExtensionListPanel.Children.Add(ext);
        }

        UpdateCountLabel(filtered.Count, mAllExtensions.Count);
    }

    private void UpdateCountLabel(int shown, int total)
    {
        if (shown == total)
            mCountLabel.Text = string.Format("{0} extension(s) installed".Tr(TC.Dialog), total);
        else
            mCountLabel.Text = string.Format("{0} of {1} extension(s)".Tr(TC.Dialog), shown, total);
    }

    private void OnCancelUninstall(ExtensionItemView itemView)
    {
        ExtensionManager.RemovePendingUninstall(itemView.ExtensionPath);
        itemView.UnmarkPendingUninstall();
    }

    // 打开设置窗并定位到该插件的扩展设置区（详情窗齿轮触发）。
    private void OnOpenSettings(ExtensionLoadResult result)
    {
        var settings = new SettingsWindow(result.Id);
        if (TopLevel.GetTopLevel(mContentPanel) is Avalonia.Controls.Window owner)
            settings.Show(owner);
        else
            settings.Show();
    }

    // 打开扩展详情窗：按当前语言解析包级 README（无则显占位），弹出可缩放详情窗。
    // 单窗：再次打开先关旧窗，避免堆叠。
    private void OnOpenDetail(ExtensionLoadResult result)
    {
        try
        {
            var readmePath = ExtensionReadme.Resolve(result.DirectoryPath, TranslationManager.CurrentLanguage.Value);
            string? markdown = null;
            if (readmePath != null)
            {
                try { markdown = File.ReadAllText(readmePath); }
                catch { /* 读取失败按无文档处理 */ }
            }

            // 该插件是否声明了扩展设置（决定详情窗是否显示齿轮）：按包 id 匹配设置条目。
            bool hasSettings = !string.IsNullOrEmpty(result.Id)
                && ExtensionSettingsManager.GetEntries().Any(e => e.PackageId == result.Id);

            var info = new ExtensionDetailInfo
            {
                Name = result.Name,
                Version = result.Version,
                Author = result.Author,
                Summary = result.Description,
                IconPath = result.IconPath,
                Types = DisplayTypes(result),
                PackageDir = result.DirectoryPath,
                ReadmeMarkdown = markdown,
                ReadmePath = readmePath,
                HasSettings = hasSettings,
            };

            mDetailWindow?.Close();
            var win = new ExtensionDetailWindow(info);
            win.Closed += (_, _) => { if (ReferenceEquals(mDetailWindow, win)) mDetailWindow = null; };
            // 齿轮 → 打开设置窗并定位到该插件；Uninstall → 复用卡片的卸载流程（含确认对话框 + 待卸载标记）。
            win.SettingsRequested += () => OnOpenSettings(result);
            win.UninstallRequested += () =>
            {
                var itemView = mAllExtensions.FirstOrDefault(v => v.ExtensionPath == result.DirectoryPath);
                if (itemView != null)
                    OnUninstallExtension(itemView);
            };
            mDetailWindow = win;

            if (TopLevel.GetTopLevel(mContentPanel) is Avalonia.Controls.Window owner)
                win.Show(owner);
            else
                win.Show();
        }
        catch { }
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

    private double mItemMaxWidth = double.PositiveInfinity; // item 宽度上限 = 列表实测宽，随侧栏宽更新
    private readonly StackPanel mContentPanel = new();
    private readonly StackPanel mExtensionListPanel = new();
    private readonly TextBlock mCountLabel;
    private readonly TextInput mSearchBox;
    private readonly List<ExtensionItemView> mAllExtensions = new();
    private ExtensionDetailWindow? mDetailWindow; // 当前详情窗（单窗），关闭时置空
}
