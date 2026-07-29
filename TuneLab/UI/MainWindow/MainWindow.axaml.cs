using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using TuneLab.Foundation;
using TuneLab.Data;
using TuneLab.SDK;
using TuneLab.GUI;
using TuneLab.Configs;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Input;
using TuneLab.Utils;
using static TuneLab.GUI.Dialog;
using Button = TuneLab.GUI.Components.Button;
using Style = TuneLab.GUI.Style;

using TuneLab.Extensions.Formats;
namespace TuneLab.UI;

public partial class MainWindow : Window
{
    internal Editor Editor => mEditor;

    const int UnsetWindowPosition = int.MinValue;
    private PlatformID platform;
    private bool isCloseConfirm;
    // 下载好待应用的更新安装器路径：走正常关闭流程退出时才拉起它（覆盖+重启），取消退出则清除。
    private string? mPendingUpdateInstaller;
    private bool mApplyingSavedWindowPlacement;
    private TextBlock TitleLabel;
    private Button maximizeButton;
    private ButtonContent maximizeIconContent = new() { Item = new IconItem() { Icon = Assets.WindowRestore }, ColorSet = new() { Color = Style.TEXT_LIGHT.Opacity(0.6) } };

    public MainWindow()
    {
        InitializeComponent();
        platform = Environment.OSVersion.Platform;
        ApplySavedWindowPlacement();

        this.KeyDown += OnKeyDown;
        RegisterKeyCommands();

#if AVALONIA
        this.AttachDevTools();
#endif

        Focusable = true;
        IsTabStop = false;
        isCloseConfirm = false;
        AppFont.Bind(this);   // 界面字体实时应用（控件树经继承 + 自绘层整树重绘）
        Background = Style.BACK.ToBrush();
        Content.Margin = new(1, 0);

	    TitleLabel = new() { Text="", FontSize=12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.TEXT_LIGHT.ToBrush()};
	    TitleLabel.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding{Path="Title",Source=this});
 
        var binimizeButton = new Button() { Width = 48, Height = 40 }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 0 }, ColorSet = new() { HoveredColor = Colors.White.Opacity(0.2), PressedColor = Colors.White.Opacity(0.2) } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.WindowMin }, ColorSet = new() { Color = Style.TEXT_LIGHT.Opacity(0.7) } });
        binimizeButton.Clicked += () => WindowState = WindowState.Minimized;

        maximizeButton = new Button() { Width = 48, Height = 40 }
           .AddContent(new() { Item = new BorderItem() { CornerRadius = 0 }, ColorSet = new() { HoveredColor = Colors.White.Opacity(0.2), PressedColor = Colors.White.Opacity(0.2) } })
           .AddContent(maximizeIconContent);
        maximizeButton.Clicked += () => WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;

        var closeButton = new Button() { Width = 48, Height = 40 }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 0 }, ColorSet = new() { HoveredColor = new(255, 232, 17, 35), PressedColor = new(255, 232, 17, 35) } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.WindowClose }, ColorSet = new() { Color = Style.TEXT_LIGHT.Opacity(0.7) } });
        closeButton.Clicked += () => Close();

        bool UseSystemTitle = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux); //Is Only Linux have double title case X11?
	    if(!UseSystemTitle){
        	WindowControl.Children.Add(binimizeButton);
        	WindowControl.Children.Add(maximizeButton);
        	WindowControl.Children.Add(closeButton);
		    TitleBar.Children.Add(TitleLabel);
	    }
 
        this.AttachWindowStateHandler();

        mEditor = new Editor();
        mEditor.Document.ProjectNameChanged.Subscribe(UpdateTitle);
        mEditor.Document.StatusChanged += UpdateTitle;
        MenuBar.Children.Add(mEditor.Menu);

        var dockPanelEditor = new DockPanel();

        dockPanelEditor.Children.Add(mEditor);

        Content.Content = dockPanelEditor;

        MinHeight = mEditor.MinHeight;

        UpdateTitle();

        PositionChanged += (_, _) => SaveCurrentWindowBounds();
        this.Closing += MainWindow_Closing;
    }

    protected override async void OnOpened(EventArgs e)
    {
        // 崩溃检测：哨兵即自动保存文件本身，存在即上次异常退出（正常退出 / 保存时会清除）。
        var record = mEditor.AutoSaveStore.FindLatest();
        if (record == null)
            return;

        // 展示名与原工程路径来自 sidecar 元数据，不再按固定长度切文件名前缀（那种切法格式一变就错位）。
        // 三种情形分开处理：
        //  · 无元数据（缺失 / 配对校验不通过）→ 只有自动保存的文件名可用；且拿不到原路径，相对音频引用
        //    因此无法解析——那是"未保存工程没有基准目录"的必然结果，如实降级。
        //  · 元数据里名字为空 → 工程从未保存过。元数据里刻意不存本地化文本，故按【当前】语言渲染，
        //    这样换过界面语言之后恢复出来的名字也是对的。
        //  · 否则用元数据里那个语言无关的文件名。
        var displayName =
            record.Meta == null ? Path.GetFileName(record.FilePath) :
            string.IsNullOrEmpty(record.Meta.ProjectName) ? "Untitled Project".Tr(TC.Document) :
            record.Meta.ProjectName;

        var modal = new Dialog();
        modal.SetTitle("Tips".Tr(TC.Dialog));
        // 指明是哪个工程：只说"上次崩溃了"会让用户必须先恢复出来才知道它指什么，平时开好几个工程时尤其如此。
        // 名字与路径是数据、不是文案，所以这里不引入新的待译串。原路径在"工程从未保存过"时本就没有，此时只给名字。
        var message = "Program crashed last time. Open auto-backup file?".Tr(TC.Dialog) + "\n\n" + displayName;
        if (!string.IsNullOrEmpty(record.Meta?.OriginalPath))
            message += "\n" + record.Meta!.OriginalPath;
        modal.SetMessage(message);
        modal.AddButton("No".Tr(TC.Dialog), ButtonType.Normal);
        modal.AddButton("OK".Tr(TC.Dialog), ButtonType.Primary).Clicked += () =>
        {
            // 崩溃恢复打开的是完整 native 工程（autosave .tlp/.tlpx）：走 native 路径恢复 editor/export 元数据。
            if (!FormatsManager.DeserializeNative(record.FilePath, out var file, out var error))
            {
                Log.Error("Open file error: " + error);
                return;
            }

            // 必须先于 SetInfo：恢复出来的工程【保持未保存态】但要带上基准目录，而随后 SetInfo 新建出来的
            // 音频 part 是靠 ProjectDocument 的 ItemAdded 订阅拿到基准目录的，此刻它必须已经就位。
            mEditor.Document.SetRecovered(displayName, record.Meta?.OriginalPath);
            if (mEditor.Project == null)
                return;

            mEditor.Project.SetInfo(file.Project);
            mEditor.Project.SetExportConfig(file.Export);
            mEditor.Playhead.Pos = Math.Max(0, file.Editor.PlayheadPos);
            mEditor.Project.Commit();
            foreach (var part in mEditor.Project.Tracks.SelectMany(track => track.Parts))
            {
                if (part is MidiPart midiPart)
                {
                    mEditor.SwitchEditingPart(midiPart);
                    break;
                }
            }

        };
        modal.Topmost = true;
        await modal.ShowDialog(this);
    }

    private void AttachWindowStateHandler()
    {
        this.GetObservable(Window.WindowStateProperty).Subscribe(state =>
        {
            if (!mApplyingSavedWindowPlacement)
            {
                EditorState.MainWindowMaximized.Value = state == WindowState.Maximized;
            }

            switch (state)
            {
                case WindowState.Normal:
                    maximizeIconContent.Item = new IconItem() { Icon = Assets.WindowMax };
                    break;
                case WindowState.Maximized:
                    maximizeIconContent.Item = new IconItem() { Icon = Assets.WindowRestore };
                    if (platform == PlatformID.Win32NT || platform == PlatformID.Win32Windows)
                    {
                        ExtendClientAreaTitleBarHeightHint = 50;
                    }
                    break;
                case WindowState.Minimized:
                    break;
                case WindowState.FullScreen:
                    maximizeIconContent.Item = new IconItem() { Icon = Assets.WindowRestore };
                    break;
            }

            if (CustomTitleBar.ColumnDefinitions[0] != null && CustomTitleBar.ColumnDefinitions[2] != null)
            {
                TitleLabel.Margin = new(0, 0, CustomTitleBar.ColumnDefinitions[0].ActualWidth - CustomTitleBar.ColumnDefinitions[2].ActualWidth, 0);
            }
        });
        this.GetObservable(TopLevel.ClientSizeProperty).Subscribe(_ => SaveCurrentWindowBounds());
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SaveCurrentWindowBounds();

        if (CustomTitleBar.ColumnDefinitions[0] != null && CustomTitleBar.ColumnDefinitions[2] != null)
        {
            TitleLabel.Margin = new(0, 0, CustomTitleBar.ColumnDefinitions[0].ActualWidth - CustomTitleBar.ColumnDefinitions[2].ActualWidth, 0);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.WindowState != WindowState.Maximized)
        {
            // 最大化窗口
            this.WindowState = WindowState.Maximized;
        }
        else
        {
            // 还原窗口大小
            this.WindowState = WindowState.Normal;
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        // 最小化窗口
        this.WindowState = WindowState.Minimized;
    }

    // 下载完更新后由 Editor 调用：记下安装器、走正常关闭流程（含未保存提示）。取消退出则不更新。
    public void RequestUpdateRestart(string installerPath)
    {
        mPendingUpdateInstaller = installerPath;
        Close();
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!isCloseConfirm && !mEditor.Document.IsSaved)
        {
            e.Cancel = true;
            var modal = new Dialog();
            modal.SetTitle("Tips".Tr(TC.Dialog));
            modal.SetMessage("The project has not been saved.\n Do you want to save it?".Tr(TC.Dialog));
            // 取消退出：连带取消待应用的更新。
            modal.AddButton("Cancel".Tr(TC.Dialog), ButtonType.Normal).Clicked += () => { mPendingUpdateInstaller = null; };
            modal.AddButton("No".Tr(TC.Dialog), ButtonType.Normal).Clicked += () => { isCloseConfirm = true; Close(); };
            modal.AddButton("Save".Tr(TC.Dialog), ButtonType.Primary).Clicked += async () => { await mEditor.SaveProject(); isCloseConfirm = true; Close(); };
            modal.Topmost = true;
            await modal.ShowDialog(this);
            return;
        }

        // 正常退出
        EditorState.MainWindowMaximized.Value = WindowState == WindowState.Maximized;
        SaveCurrentWindowBounds();
        Settings.Save(PathManager.SettingsFilePath);
        EditorState.Save(PathManager.EditorStateFilePath);
        mEditor.ClearAutoSaveFile();

        // 有待应用的更新：在真正退出前拉起安装器（它会等本进程退出释放锁，再覆盖并重启）。
        if (mPendingUpdateInstaller != null)
            AppUpdateManager.LaunchInstallerUpdate(mPendingUpdateInstaller);
    }

    void ApplySavedWindowPlacement()
    {
        mApplyingSavedWindowPlacement = true;
        try
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            if (EditorState.MainWindowWidth.Value > 0 && EditorState.MainWindowWidth.Value >= MinWidth)
            {
                Width = EditorState.MainWindowWidth;
            }
            if (EditorState.MainWindowHeight.Value > 0 && EditorState.MainWindowHeight.Value >= MinHeight)
            {
                Height = EditorState.MainWindowHeight;
            }

            if (EditorState.MainWindowX.Value != UnsetWindowPosition && EditorState.MainWindowY.Value != UnsetWindowPosition)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint(EditorState.MainWindowX, EditorState.MainWindowY);
            }

            WindowState = EditorState.MainWindowMaximized ? WindowState.Maximized : WindowState.Normal;
        }
        finally
        {
            mApplyingSavedWindowPlacement = false;
        }
    }

    void SaveCurrentWindowBounds()
    {
        if (mApplyingSavedWindowPlacement || WindowState != WindowState.Normal)
            return;

        EditorState.MainWindowX.Value = Position.X;
        EditorState.MainWindowY.Value = Position.Y;

        if (ClientSize.Width >= MinWidth)
        {
            EditorState.MainWindowWidth.Value = ClientSize.Width;
        }

        if (ClientSize.Height >= MinHeight)
        {
            EditorState.MainWindowHeight.Value = ClientSize.Height;
        }
    }

    void UpdateTitle()
    {
        Title = "TuneLab - " + mEditor.Document.Name + (mEditor.Document.IsSaved ? string.Empty : "*");
    }

    void OnKeyDown(object? sender, KeyEventArgs args)
    {
        args.Handled = Keymap.TryHandle(KeyScope.Global, args);
    }

    // Global 作用域的内置快捷键命令（跨全窗口生效，由最外层 Window.KeyDown 兜底分发）。
    void RegisterKeyCommands()
    {
        Keymap.Register(new()
        {
            Id = "app.fullscreen",
            DisplayName = () => "Full Screen".Tr(TC.Menu),
            Scope = KeyScope.Global,
            // 按平台走原生约定：Windows=F11，Mac=⌃⌘F（物理 Control+Meta+F，改用物理修饰后可表达；见
            // docs/keybinding-system.md §1.2）。若 macOS 自身截走 ⌃⌘F 做系统全屏，效果一致；用户仍可重绑。
            DefaultGesture = OperatingSystem.IsMacOS()
                ? new(Key.F, KeyModifiers.Control | KeyModifiers.Meta)
                : new(Key.F11),
            Execute = () => OnMenuFullScreen(this, new RoutedEventArgs()),
        });
    }
    
    void OnMenuFullScreen(object sender, RoutedEventArgs args) {
            this.WindowState = this.WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
    }
    
    readonly Editor mEditor;
}
