using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Setup.Core;
using TuneLab.Utils;
using GuiButton = TuneLab.GUI.Components.Button;
using GuiCheckBox = TuneLab.GUI.Components.CheckBox;
using ButtonContent = TuneLab.GUI.Components.ButtonContent;
using TextInput = TuneLab.GUI.Components.TextInput;
using ComboBoxItem = TuneLab.SDK.ComboBoxItem;   // 消歧：与 Avalonia.Controls.ComboBoxItem 同名

namespace TuneLab.Setup.Views;

public partial class MainWindow : Window
{
    enum Step { Welcome, Options, Progress, Finish }

    const string WelcomeKey = "An extensible singing voice synthesis editor. This wizard will guide you through the installation. Click Next to continue.";

    readonly InstallOptions mOptions = new();
    Step mStep = Step.Welcome;
    CancellationTokenSource? mCts;
    bool mInstalled;

    GuiButton mBackButton = null!;
    GuiButton mNextButton = null!;
    GuiButton mCancelButton = null!;
    ButtonContent mNextText = null!;
    GuiCheckBox mDesktopShortcut = null!;
    GuiCheckBox mStartMenuShortcut = null!;
    GuiCheckBox mFileAssoc = null!;
    GuiCheckBox mLaunchApp = null!;
    TextInput mInstallDir = null!;

    // 语言可即时切换：每处本地化文案登记一个应用闭包，切语言时全部重跑。
    readonly List<Action> mLocalizers = new();
    // Finish 页文案随安装结果而定（含动态路径/错误），单独登记以便切语言时也刷新。
    Action? mFinishLocalizer;

    static string Tr(string s) => s.Tr(SetupI18N.Ctx);

    // 登记一处本地化：立即应用一次，并存起来供 RefreshTexts 重跑。
    void Loc(Action apply) { apply(); mLocalizers.Add(apply); }

    void RefreshTexts()
    {
        foreach (var apply in mLocalizers)
            apply();
        mFinishLocalizer?.Invoke();
    }

    public MainWindow()
    {
        InitializeComponent();

        ApplyTheme();
        BuildLanguageSelector();
        BuildTitleBar();
        BuildNavButtons();
        BuildOptions();

        Loc(() => WelcomeBody.Text = Tr(WelcomeKey));
        Loc(() => WelcomeVersion.Text = Tr("Version") + " " + ProductInfo.VersionString);
        Loc(() => LocationLabel.Text = Tr("Install location"));

        GoTo(Step.Welcome);
    }

    // ---- 外观（复用 TuneLab 的 Style 调色板） ----

    void ApplyTheme()
    {
        Background = Style.BACK.ToBrush();
        TitleBar.Background = Style.INTERFACE.ToBrush();
        FooterBar.Background = Style.INTERFACE.ToBrush();
        TitleText.Foreground = Style.LIGHT_WHITE.ToBrush();

        foreach (var tb in new[] { WelcomeTitle, WelcomeBody, LocationLabel, ProgressStatus, FinishTitle, FinishMessage })
            tb.Foreground = Style.TEXT_LIGHT.ToBrush();
        WelcomeVersion.Foreground = Style.HIGH_LIGHT.ToBrush();
        ProgressEntry.Foreground = Style.LIGHT_WHITE.ToBrush();

        ProgressBar.Foreground = Style.HIGH_LIGHT.ToBrush();

        Loc(() => TitleText.Text = Tr("TuneLab Setup"));
    }

    // ---- 语言下拉（复用 GUI 的 ComboBoxController；切换即时刷新全窗文案） ----

    void BuildLanguageSelector()
    {
        // 内容区背景是 Style.BACK，与下拉默认面色同色会糊在一起——覆盖成较亮的 INTERFACE，使其像一个可见的角落控件。
        var combo = new ComboBoxController { Width = 132, FaceBackground = Style.INTERFACE.ToBrush() };
        combo.SetConfig(ComboBoxConfig.Create(
            TranslationManager.Languages.Select(l => new ComboBoxItem(l, TranslationManager.GetDisplayName(l))).ToList()));

        var current = TranslationManager.CurrentLanguage.Value;
        if (string.IsNullOrEmpty(current))
            current = "en-US";
        combo.Display(PropertyValue.Create(current));

        combo.ValueChanged.Subscribe(() =>
        {
            var lang = combo.Value.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(lang))
                return;
            TranslationManager.CurrentLanguage.Value = lang;
            RefreshTexts();
            // 用户在安装器里改语言即写回主程序设置（与主程序共用 Settings.json），使其首启/下次启动即用该语言。
            UserSettings.WriteLanguage(lang);
        });

        LanguageSlot.Children.Add(combo);
    }

    void BuildTitleBar()
    {
        var close = new GuiButton { Width = 46, Height = 36 };
        close.AddContent(new() { Item = new BorderItem(), ColorSet = new() { Color = Style.TRANSPARENT, HoveredColor = Style.SYNTHESIS_FAILED } });
        close.AddContent(new() { Item = new IconItem() { Icon = Assets.WindowClose }, ColorSet = new() { Color = Style.LIGHT_WHITE, HoveredColor = Colors.White } });
        close.Clicked += Close;
        TitleButtons.Children.Add(close);

        // 拖动窗口——但点在标题栏内的按钮上时不拖，否则 BeginMoveDrag 会吞掉按钮点击。
        TitleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual v && v.FindAncestorOfType<GuiButton>(includeSelf: true) != null) return;
            BeginMoveDrag(e);
        };
    }

    // ---- 导航按钮（复用 GUI.Components.Button） ----

    GuiButton MakeButton(string key, bool primary, double width = 96)
    {
        var b = new GuiButton { Width = width, Height = 34, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        b.AddContent(new()
        {
            Item = new BorderItem() { CornerRadius = 6 },
            ColorSet = new()
            {
                Color = primary ? Style.BUTTON_PRIMARY : Style.BUTTON_NORMAL,
                HoveredColor = primary ? Style.BUTTON_PRIMARY_HOVER : Style.BUTTON_NORMAL_HOVER,
            },
        });
        var content = new ButtonContent { Item = new TextItem() { Text = Tr(key) }, ColorSet = new() { Color = primary ? Colors.White : Style.LIGHT_WHITE } };
        b.AddContent(content);
        Loc(() => content.Item = new TextItem() { Text = Tr(key) });
        return b;
    }

    void BuildNavButtons()
    {
        mCancelButton = MakeButton("Cancel", primary: false);
        mBackButton = MakeButton("Back", primary: false);

        // Next 文案随步骤变（Next/Install/Finish）：保留其文字内容项，改 Item 即触发重绘。
        mNextButton = new GuiButton { Width = 96, Height = 34, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        mNextButton.AddContent(new()
        {
            Item = new BorderItem() { CornerRadius = 6 },
            ColorSet = new() { Color = Style.BUTTON_PRIMARY, HoveredColor = Style.BUTTON_PRIMARY_HOVER },
        });
        mNextText = new ButtonContent { Item = new TextItem() { Text = NextText() }, ColorSet = new() { Color = Colors.White } };
        mNextButton.AddContent(mNextText);
        Loc(() => SetNextText(NextText()));

        mCancelButton.Clicked += OnCancel;
        mBackButton.Clicked += GoBack;
        mNextButton.Clicked += OnNext;

        CancelSlot.Children.Add(mCancelButton);
        BackSlot.Children.Add(mBackButton);
        NextSlot.Children.Add(mNextButton);
    }

    void SetNextText(string text) => mNextText.Item = new TextItem() { Text = text };

    string NextText() => mStep switch
    {
        Step.Options => Tr("Install"),
        Step.Finish => Tr("Finish"),
        _ => Tr("Next"),
    };

    // ---- 选项复选框（复用 GUI.Components.CheckBox） ----

    (GuiCheckBox cb, Control row) MakeOption(string key, bool initial)
    {
        var cb = new GuiCheckBox();
        cb.Display(initial);
        var text = new TextBlock
        {
            Text = Tr(key),
            Foreground = Style.TEXT_NORMAL.ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new(10, 0, 0, 0),
        };
        text.PointerPressed += (_, _) => cb.IsChecked = !cb.IsChecked;
        Loc(() => text.Text = Tr(key));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { cb, text } };
        return (cb, row);
    }

    void BuildOptions()
    {
        // TextInput 默认底色是 Style.BACK，与窗口背景同色会糊在一起；换成更深的 DARK 做下沉输入框。
        mInstallDir = new TextInput { Height = 34, Background = Style.DARK.ToBrush() };
        mInstallDir.Text = mOptions.InstallDir;
        InstallDirSlot.Children.Add(mInstallDir);

        var browse = MakeButton("Browse…", primary: false, width: 92);
        browse.Clicked += OnBrowse;
        BrowseSlot.Children.Add(browse);

        Control r1, r2, r3, r4;
        (mDesktopShortcut, r1) = MakeOption("Create a desktop shortcut", true);
        (mStartMenuShortcut, r2) = MakeOption("Create a Start Menu shortcut", true);
        (mFileAssoc, r3) = MakeOption("Associate .tlp / .tlpx project files with TuneLab", true);
        OptionsBox.Children.Add(r1);
        OptionsBox.Children.Add(r2);
        OptionsBox.Children.Add(r3);

        (mLaunchApp, r4) = MakeOption("Launch TuneLab now", true);
        LaunchSlot.Children.Add(r4);
    }

    // ---- 导航 ----

    void GoTo(Step step)
    {
        mStep = step;
        WelcomePanel.IsVisible = step == Step.Welcome;
        OptionsPanel.IsVisible = step == Step.Options;
        ProgressPanel.IsVisible = step == Step.Progress;
        FinishPanel.IsVisible = step == Step.Finish;

        mBackButton.IsVisible = step == Step.Options;
        mCancelButton.IsVisible = step is Step.Welcome or Step.Options;
        mNextButton.IsVisible = step != Step.Progress;

        SetNextText(NextText());
    }

    void GoBack()
    {
        if (mStep == Step.Options) GoTo(Step.Welcome);
    }

    async void OnNext()
    {
        switch (mStep)
        {
            case Step.Welcome: GoTo(Step.Options); break;
            case Step.Options:
                CollectOptions();
                await RunInstallAsync();
                break;
            case Step.Finish:
                if (mInstalled && mLaunchApp.IsChecked && OperatingSystem.IsWindows())
                    new Installer(mOptions).Launch();
                Close();
                break;
        }
    }

    void OnCancel()
    {
        mCts?.Cancel();
        Close();
    }

    async void OnBrowse()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Tr("Choose install location"),
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            mInstallDir.Text = Path.Combine(path, ProductInfo.ProductName);
    }

    void CollectOptions()
    {
        mOptions.InstallDir = string.IsNullOrWhiteSpace(mInstallDir.Text)
            ? ProductInfo.DefaultInstallDir : mInstallDir.Text.Trim();
        mOptions.CreateDesktopShortcut = mDesktopShortcut.IsChecked;
        mOptions.CreateStartMenuShortcut = mStartMenuShortcut.IsChecked;
        mOptions.RegisterFileAssociations = mFileAssoc.IsChecked;
    }

    async Task RunInstallAsync()
    {
        GoTo(Step.Progress);
        mCts = new CancellationTokenSource();

        var progress = new Progress<InstallStatus>(s =>
        {
            ProgressBar.Value = s.Fraction;
            ProgressStatus.Text = s.Message;
            ProgressEntry.Text = s.Fraction < 1 ? s.Message : "";
        });

        try
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("The TuneLab installer runs on Windows only.");

            // 若 TuneLab 正在运行，安装会阻塞在“等它退出”上（文件占用）。此时明确提示用户去关闭，
            // 而不是让进度停在无从解释的“Preparing…”。关闭后 InstallAsync 内的等待自然放行、继续铺文件。
            ProgressStatus.Text = Installer.IsAppRunning()
                ? Tr("TuneLab is running. Please close it to continue.")
                : Tr("Preparing…");

            await new Installer(mOptions).InstallAsync(progress, mCts.Token);
            mInstalled = true;
            var installDir = mOptions.InstallDir;
            mFinishLocalizer = () =>
            {
                FinishTitle.Text = Tr("Installation complete");
                FinishMessage.Text = Tr("TuneLab has been installed to:") + "\n" + installDir;
            };
            // 若安装时 TuneLab 原本在运行，它退出时会用自己内存里的旧语言写一次 Settings.json，
            // 覆盖安装器早先（切换即写）写入的语言。安装完成 = 目标已确认退出（InstallAsync 内部等过锁释放，
            // 其退出保存已落盘），此处再写一次即稳压过那次覆盖，保证新启动的 TuneLab 用安装器所选语言。
            UserSettings.WriteLanguage(TranslationManager.CurrentLanguage.Value);
        }
        catch (OperationCanceledException)
        {
            mFinishLocalizer = () =>
            {
                FinishTitle.Text = Tr("Installation cancelled");
                FinishMessage.Text = Tr("The installation was cancelled. No changes were finalized.");
            };
            LaunchSlot.IsVisible = false;
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            mFinishLocalizer = () =>
            {
                FinishTitle.Text = Tr("Installation failed");
                FinishMessage.Text = message;   // 异常信息不本地化
            };
            LaunchSlot.IsVisible = false;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            mFinishLocalizer?.Invoke();
            GoTo(Step.Finish);
        });
    }
}
