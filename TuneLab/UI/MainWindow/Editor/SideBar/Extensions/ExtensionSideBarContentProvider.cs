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
        // 卡片上不再有设置齿轮（设置是 per 能力位的，包级卡片无从代表其中某一个），故这里也不必查设置声明；
        // 设置入口在详情窗各 tab 内，见 BuildDetailPages 的 SettingsKey。
        foreach (var result in ExtensionManager.LoadResults)
        {
            var itemView = new ExtensionItemView(result.Name, result.Version, DisplayTypes(result), result.Author, result.Description, result.IconPath, result.DirectoryPath, result.Status, result.Error);
            itemView.UninstallRequested += () => OnUninstallExtension(itemView);
            itemView.CancelUninstallRequested += () => OnCancelUninstall(itemView);
            itemView.OpenDetailRequested += () => OnOpenDetail(result);
            if (ExtensionManager.PendingUninstalls.Contains(result.DirectoryPath))
                itemView.MarkPendingUninstall();
            itemView.SetRestartRequired(NeedsRestart(result));
            mAllExtensions.Add(itemView);
        }
    }

    // 存下来的启停选择与【本次运行的实际状态】是否已经不一致——不一致就得重启才生效。
    // 两侧都要比：包级（整包关/开）与逐条目级，任一处不符即为真。
    private static bool NeedsRestart(ExtensionLoadResult result)
    {
        if (string.IsNullOrEmpty(result.Id))
            return false;

        if (ExtensionActivation.IsPackageDisabled(result.Id) != (result.Status == ExtensionLoadStatus.Disabled))
            return true;

        foreach (var entry in result.Entries)
        {
            if (ExtensionActivation.IsEntryDisabled(result.Id, entry.Kind, entry.Identities)
                != (entry.Status == ExtensionEntryStatus.Disabled))
                return true;
        }
        return false;
    }

    // 重算并回推「需重启」提示到两个视图（卡片 + 正开着的详情窗）。
    private void SyncActivationHints(ExtensionLoadResult result, ExtensionItemView itemView)
    {
        bool needsRestart = NeedsRestart(result);
        itemView.SetRestartRequired(needsRestart);
        if (mDetailWindow != null && mDetailWindowPath == result.DirectoryPath)
            mDetailWindow.SetRestartRequired(needsRestart);
    }

    // 展示用的类别列表（每项渲染成一枚徽标）。无真实类别时退回单项占位。
    private static IReadOnlyList<string> DisplayTypes(ExtensionLoadResult result)
    {
        if (result.Types.Count > 0)
            return result.Types.Select(BadgeLabel).Distinct().ToList();

        return [result.Generation == ExtensionGeneration.Legacy ? "Legacy" : "Extension"];
    }

    // 徽标文本：format 三型合并成一枚 **Format**。
    // 徽标回答的是"这是个什么类型的插件"，而"能读还是能写"是**条目粒度**的事实——一个既导入又导出的包
    // 会挂出两枚只差方向的徽标，占掉整行却没多说什么。方向要在详情窗每个 tab 旁边点明（那里两个 tab 并排、
    // 名字只差括号里一个词，不标方向反而分不清），见 ExtensionDetailPage.Kind。
    private static string BadgeLabel(string kind)
        => FormatsManager.IsFormatKind(kind) ? "Format" : Capitalize(kind);

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
        SyncDetailUninstall(itemView.ExtensionPath);
    }

    // 若详情窗正展示该插件，把其卸载按钮态同步为当前 PendingUninstalls（跨卡片/详情窗一致）。
    private void SyncDetailUninstall(string dirPath)
    {
        if (mDetailWindow != null && mDetailWindowPath == dirPath)
            mDetailWindow.SetUninstallPending(ExtensionManager.PendingUninstalls.Contains(dirPath));
    }

    // 打开设置窗并精确定位到某个【能力位】的设置区（详情窗某个 tab 的齿轮触发）。
    // extensionKey 形如 "voice:MyEngine"，由该 tab 对应的条目算出——设置是 per 能力位的，故不做"落到该包
    // 首个有设置的条目"这类猜测（卡片上原来那个包级齿轮已因此移除）。
    private void OnOpenSettings(ExtensionLoadResult result, string extensionKey)
    {
        SettingsWindow.Open(TopLevel.GetTopLevel(mContentPanel) as Avalonia.Controls.Window, result.Id, extensionKey);
    }

    // 详情窗正文的分页：**逐 manifest 条目恒一页**，不做任何合并、也不按"有没有内容"过滤。
    //
    // 不合并：声明的单位就是条目（一个格式的多个后缀别名写在同一条目里，见 ExtensionInfo.suffixes），
    //   故不会出现两个条目指同一份文档，也就不需要"按文件去重"那种事后补救（那样两条目 name 不同时无从取舍）。
    // 不过滤：tab 一栏如实回答"这个包提供哪些能力位、各自是什么"。没写文档的条目照样占一页（显占位），
    //   否则用户看不到它的存在；也只有恒生成，才有地方承载该条目的齿轮（及后续的启用/禁用开关）。
    // 【也不因禁用而过滤】被关掉的条目照样占一页——那正是用户回来把它重新打开的地方。
    private static List<ExtensionDetailPage> BuildDetailPages(ExtensionLoadResult result)
    {
        var pages = new List<ExtensionDetailPage>();

        // 本包各能力位的设置桶键；齿轮按 (包 id, kind:identity) 精确归属到条目。
        var settingsKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(result.Id))
        {
            foreach (var e in ExtensionSettingsManager.GetEntries())
                if (e.PackageId == result.Id)
                    settingsKeys.Add(e.ExtensionKey);
        }

        foreach (var entry in result.Entries)
        {
            // 被禁用的条目不会注册，故也不在 GetEntries 里 → 本页没有齿轮。这是实情：它这次没加载，
            // 没有实例可配置；重新启用并重启后齿轮自会回来。
            var settingsKey = SettingsKeyOf(entry, settingsKeys);

            string? markdown = null;
            if (!string.IsNullOrEmpty(entry.IntroductionPath))
            {
                try { markdown = File.ReadAllText(entry.IntroductionPath); }
                catch { /* 读取失败按无文档处理 */ }
            }

            pages.Add(new ExtensionDetailPage
            {
                Title = string.IsNullOrEmpty(entry.DisplayName) ? result.Name : entry.DisplayName,
                Kind = Capitalize(entry.Kind),
                EntryKind = entry.Kind,   // 原样 type：启停键要与加载期同口径，不能用上面的展示串
                CanDisable = ExtensionActivation.CanDisableEntry(result.Id, entry.Kind, entry.Identities),
                // format 条目的身份就是文件后缀：如实列在页里，否则用户只看到一个名字、不知道它管哪些文件。
                Identities = entry.Identities,
                IdentitiesAreFileSuffixes = FormatsManager.IsFormatKind(entry.Kind),
                Markdown = markdown,
                FilePath = entry.IntroductionPath,
                SettingsKey = settingsKey,
            });
        }

        return pages;
    }

    // 条目 → 它的设置桶键（没有设置则 null）。
    // engine 类是 1:1，身份即桶键；format 三型是【多后缀共一份实现、也就共一个桶】，键取全部后缀按声明序拼接
    // （与 FormatsManager.EntryId 同口径）——逐后缀去查会全部落空，多后缀 format 的齿轮就没了。
    private static string? SettingsKeyOf(ExtensionEntryInfo entry, HashSet<string> settingsKeys)
    {
        if (entry.Identities.Count == 0)
            return null;

        if (FormatsManager.IsFormatKind(entry.Kind))
        {
            var key = entry.Kind + ":" + FormatsManager.EntryId(entry.Identities);
            return settingsKeys.Contains(key) ? key : null;
        }

        foreach (var id in entry.Identities)
        {
            var key = entry.Kind + ":" + id;
            if (settingsKeys.Contains(key))
                return key;
        }
        return null;
    }

    // 打开扩展详情窗：正文按包内各条目分页渲染其 introduction（都没写则显占位），弹出可缩放详情窗。
    // 单窗：再次打开先关旧窗，避免堆叠。
    private void OnOpenDetail(ExtensionLoadResult result)
    {
        try
        {
            // 齿轮已随各页归属到具体能力位（BuildDetailPages 逐条目挂 SettingsKey），故此处不再算包级 hasSettings。
            var pages = BuildDetailPages(result);

            var info = new ExtensionDetailInfo
            {
                Name = result.Name,
                Version = result.Version,
                Author = result.Author,
                Description = result.Description,
                IconPath = result.IconPath,
                // 类别徽标只在无条目页时用得上（legacy / manifest 坏包）；有 tab 的包由各 tab 自带徽标。
                Types = DisplayTypes(result),
                PackageDir = result.DirectoryPath,
                PackageId = result.Id,
                Pages = pages,
                IsLegacy = result.Generation == ExtensionGeneration.Legacy,
                IsPendingUninstall = ExtensionManager.PendingUninstalls.Contains(result.DirectoryPath),
            };

            mDetailWindow?.Close();
            var win = new ExtensionDetailWindow(info);
            mDetailWindowPath = result.DirectoryPath;
            win.Closed += (_, _) => { if (ReferenceEquals(mDetailWindow, win)) { mDetailWindow = null; mDetailWindowPath = null; } };
            // 齿轮 → 打开设置窗并定位到【当前页那个能力位】的设置区；Uninstall/Cancel → 复用卡片的卸载/撤销流程
            // （含确认对话框 + 待卸载标记）。
            win.SettingsRequested += key => OnOpenSettings(result, key);
            win.UninstallRequested += () =>
            {
                var itemView = mAllExtensions.FirstOrDefault(v => v.ExtensionPath == result.DirectoryPath);
                if (itemView != null)
                    OnUninstallExtension(itemView);
            };
            win.CancelUninstallRequested += () =>
            {
                var itemView = mAllExtensions.FirstOrDefault(v => v.ExtensionPath == result.DirectoryPath);
                if (itemView != null)
                    OnCancelUninstall(itemView);
            };
            // 窗内改了启停（包级或条目级）：卡片上没有开关可同步，但「需重启」提示要跟着亮/灭——
            // 那是卡片列表里唯一能看出"这个包的启停被改过、还没生效"的地方。
            win.ActivationChanged += () =>
            {
                var itemView = mAllExtensions.FirstOrDefault(v => v.ExtensionPath == result.DirectoryPath);
                if (itemView != null)
                    SyncActivationHints(result, itemView);
            };
            mDetailWindow = win;
            win.SetRestartRequired(NeedsRestart(result));

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
                    // 就地同步详情窗（勿放在 await 之后：Dialog 先挂的 Close 会让 ShowDialog 内联恢复、
                    // 使 await 后的代码早于本 handler 运行、读到旧状态）。
                    SyncDetailUninstall(dirPath);
                };
                dialog.AddButton("Restart".Tr(TC.Dialog), TuneLab.GUI.Dialog.ButtonType.Primary).Clicked += () =>
                {
                    // Mark for uninstall + set restart flag; the actual
                    // ExtensionInstaller launch happens in desktop.Exit.
                    ExtensionManager.AddPendingUninstall(dirPath);
                    ExtensionManager.RestartAfterUninstall = true;
                    itemView.MarkPendingUninstall();
                    SyncDetailUninstall(dirPath);

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
    private string? mDetailWindowPath;            // 详情窗当前展示的插件目录（用于跨视图同步卸载态）
}
