using System;
using System.IO;
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
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.Setup.Core;
using TuneLab.Utils;
using GuiButton = TuneLab.GUI.Components.Button;
using GuiCheckBox = TuneLab.GUI.Components.CheckBox;
using ButtonContent = TuneLab.GUI.Components.ButtonContent;
using TextInput = TuneLab.GUI.Components.TextInput;

namespace TuneLab.Setup.Views;

public partial class MainWindow : Window
{
    enum Step { Welcome, Options, Progress, Finish }

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

    static string Tr(string s) => s.Tr(SetupI18N.Ctx);

    public MainWindow()
    {
        InitializeComponent();

        ApplyTheme();
        BuildTitleBar();
        BuildNavButtons();
        BuildOptions();

        WelcomeBody.Text = Tr("A lightweight singing voice synthesis editor. This wizard will guide you through the installation. Click Next to continue.");
        WelcomeVersion.Text = Tr("Version") + " " + ProductInfo.VersionString;
        LocationLabel.Text = Tr("Install location");

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

        TitleText.Text = Tr("TuneLab Setup");
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

    GuiButton MakeButton(string text, bool primary, double width = 96)
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
        b.AddContent(new()
        {
            Item = new TextItem() { Text = text },
            ColorSet = new() { Color = primary ? Colors.White : Style.LIGHT_WHITE },
        });
        return b;
    }

    void BuildNavButtons()
    {
        mCancelButton = MakeButton(Tr("Cancel"), primary: false);
        mBackButton = MakeButton(Tr("Back"), primary: false);

        // Next 文案随步骤变（Next/Install/Finish）：保留其文字内容项，改 Item 即触发重绘。
        mNextButton = new GuiButton { Width = 96, Height = 34, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        mNextButton.AddContent(new()
        {
            Item = new BorderItem() { CornerRadius = 6 },
            ColorSet = new() { Color = Style.BUTTON_PRIMARY, HoveredColor = Style.BUTTON_PRIMARY_HOVER },
        });
        mNextText = new ButtonContent { Item = new TextItem() { Text = Tr("Next") }, ColorSet = new() { Color = Colors.White } };
        mNextButton.AddContent(mNextText);

        mCancelButton.Clicked += OnCancel;
        mBackButton.Clicked += GoBack;
        mNextButton.Clicked += OnNext;

        CancelSlot.Children.Add(mCancelButton);
        BackSlot.Children.Add(mBackButton);
        NextSlot.Children.Add(mNextButton);
    }

    void SetNextText(string text) => mNextText.Item = new TextItem() { Text = text };

    // ---- 选项复选框（复用 GUI.Components.CheckBox） ----

    (GuiCheckBox cb, Control row) MakeOption(string label, bool initial)
    {
        var cb = new GuiCheckBox();
        cb.Display(initial);
        var text = new TextBlock
        {
            Text = label,
            Foreground = Style.TEXT_NORMAL.ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new(10, 0, 0, 0),
        };
        text.PointerPressed += (_, _) => cb.IsChecked = !cb.IsChecked;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { cb, text } };
        return (cb, row);
    }

    void BuildOptions()
    {
        // TextInput 默认底色是 Style.BACK，与窗口背景同色会糊在一起；换成更深的 DARK 做下沉输入框。
        mInstallDir = new TextInput { Height = 34, Background = Style.DARK.ToBrush() };
        mInstallDir.Text = mOptions.InstallDir;
        InstallDirSlot.Children.Add(mInstallDir);

        var browse = MakeButton(Tr("Browse…"), primary: false, width: 92);
        browse.Clicked += OnBrowse;
        BrowseSlot.Children.Add(browse);

        Control r1, r2, r3, r4;
        (mDesktopShortcut, r1) = MakeOption(Tr("Create a desktop shortcut"), true);
        (mStartMenuShortcut, r2) = MakeOption(Tr("Create a Start Menu shortcut"), true);
        (mFileAssoc, r3) = MakeOption(Tr("Associate .tlp / .tlpx project files with TuneLab"), true);
        OptionsBox.Children.Add(r1);
        OptionsBox.Children.Add(r2);
        OptionsBox.Children.Add(r3);

        (mLaunchApp, r4) = MakeOption(Tr("Launch TuneLab now"), true);
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

        SetNextText(step switch
        {
            Step.Options => Tr("Install"),
            Step.Finish => Tr("Finish"),
            _ => Tr("Next"),
        });
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
        ProgressStatus.Text = Tr("Preparing…");
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

            await new Installer(mOptions).InstallAsync(progress, mCts.Token);
            mInstalled = true;
            FinishTitle.Text = Tr("Installation complete");
            FinishMessage.Text = Tr("TuneLab has been installed to:") + "\n" + mOptions.InstallDir;
        }
        catch (OperationCanceledException)
        {
            FinishTitle.Text = Tr("Installation cancelled");
            FinishMessage.Text = Tr("The installation was cancelled. No changes were finalized.");
            LaunchSlot.IsVisible = false;
        }
        catch (Exception ex)
        {
            FinishTitle.Text = Tr("Installation failed");
            FinishMessage.Text = ex.Message;
            LaunchSlot.IsVisible = false;
        }

        await Dispatcher.UIThread.InvokeAsync(() => GoTo(Step.Finish));
    }
}
