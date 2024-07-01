using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using TuneLab.Base.Data;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Data;
using TuneLab.Utils;
using TuneLab.Extensions.Formats;
using TuneLab.Base.Utils;
using static TuneLab.GUI.Dialog;
using Button = TuneLab.GUI.Components.Button;
using Style = TuneLab.GUI.Style;
using TuneLab.I18N;

namespace TuneLab.Views
{
    public partial class MainWindow : Window
    {
        private PlatformID platform;
        private bool isCloseConfirm;
        private Button maximizeButton;
        private ButtonContent maximizeIconContent = new() { Item = new IconItem() { Icon = Assets.WindowRestore }, ColorSet = new() { Color = Style.TEXT_LIGHT.Opacity(0.6) } };

        public MainWindow()
        {
            InitializeComponent();
            platform = Environment.OSVersion.Platform;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

#if AVALONIA
            this.AttachDevTools();
#endif

            Focusable = true;
            IsTabStop = false;
            isCloseConfirm = false;
            Background = Style.BACK.ToBrush();
            Content.Margin = new(1, 0);

            var binimizeButton = new Button() { Width = 48, Height = 40 }
                .AddContent(new() { Item = new BorderItem() { CornerRadius = 0 }, ColorSet = new() { HoveredColor = Colors.White.Opacity(0.2), PressedColor = Colors.White.Opacity(0.2) } })
                .AddContent(new() { Item = new IconItem() { Icon = Assets.WindowMin }, ColorSet = new() { Color = Style.TEXT_LIGHT.Opacity(0.7) } });
            binimizeButton.Clicked += () => WindowState = WindowState.Minimized;

             maximizeButton = new Button() { Width = 48, Height = 40 }
                .AddContent(new() { Item = new BorderItem() { CornerRadius = 0 }, ColorSet = new() { HoveredColor = Colors.White.Opacity(0.2), PressedColor = Colors.White.Opacity(0.2) } })
                .AddContent(maximizeIconContent);
            maximizeButton.Clicked += () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

            var closeButton = new Button() { Width = 48, Height = 40 }
                .AddContent(new() { Item = new BorderItem() { CornerRadius = 0 }, ColorSet = new() { HoveredColor = new(255, 232, 17, 35), PressedColor = new(255, 232, 17, 35) } })
                .AddContent(new() { Item = new IconItem() { Icon = Assets.WindowClose }, ColorSet = new() { Color = Style.TEXT_LIGHT.Opacity(0.7) } });
            closeButton.Clicked += () => Close();

            WindowControl.Children.Add(binimizeButton);
            WindowControl.Children.Add(maximizeButton);
            WindowControl.Children.Add(closeButton);

            this.AttachWindowStateHandler();

            TitleLabel.Foreground = Style.TEXT_LIGHT.ToBrush();

            mEditor = new Editor();
            mEditor.Document.ProjectNameChanged.Subscribe(UpdateTitle);
            mEditor.Document.StatusChanged += UpdateTitle;
            MenuBar.Children.Add(mEditor.Menu);

            var dockPanelEditor = new DockPanel();

            dockPanelEditor.Children.Add(mEditor);

            Content.Content = dockPanelEditor;

            MinHeight = mEditor.MinHeight;

            UpdateTitle();

            this.Closing += MainWindow_Closing;
        }

        protected override async void OnOpened(EventArgs e)
        {
            // 崩溃检测
            using var files = Directory.GetFiles(PathManager.AutoSaveFolder).Where(file => Path.GetExtension(file) == ".tlp").GetEnumerator();
            if (files.MoveNext())
            {
                var path = files.Current;
                var modal = new Dialog();
                modal.SetTitle("Tips".Tr(TC.Dialog));
                modal.SetMessage("Program crashed last time. Open auto-backup file?".Tr(TC.Dialog));
                modal.AddButton("No".Tr(TC.Dialog), ButtonType.Normal);
                modal.AddButton("OK".Tr(TC.Dialog), ButtonType.Primary).Clicked += () => 
                {
                    if (!FormatsManager.Deserialize(path, out var info, out var error))
                    {
                        Log.Error("Open file error: " + error);
                        return;
                    }

                    mEditor.Document.SetSavePath(path);
                    mEditor.Project?.Set(info);
                    mEditor.Project?.Commit();
                };
                modal.Topmost = true;
                await modal.ShowDialog(this);
            }
        }

        private void AttachWindowStateHandler()
        {
            this.GetObservable(Window.WindowStateProperty).Subscribe(state =>
            {
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
                }

                if (CustomTitleBar.ColumnDefinitions[0] != null && CustomTitleBar.ColumnDefinitions[2] != null)
                {
                    TitleLabel.Margin = new(0, 0, CustomTitleBar.ColumnDefinitions[0].ActualWidth - CustomTitleBar.ColumnDefinitions[2].ActualWidth, 0);
                }
            });
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
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

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!isCloseConfirm && !mEditor.Document.IsSaved)
            {
                e.Cancel = true;
                var modal = new Dialog();
                modal.SetTitle("Tips".Tr(TC.Dialog));
                modal.SetMessage("The project has not been saved.\n Do you want to save it?".Tr(TC.Dialog));
                modal.AddButton("Cancel".Tr(TC.Dialog), ButtonType.Normal);
                modal.AddButton("No".Tr(TC.Dialog), ButtonType.Normal).Clicked += () => { isCloseConfirm = true; Close(); };
                modal.AddButton("Save".Tr(TC.Dialog), ButtonType.Primary).Clicked += async () => { await mEditor.SaveProject(); isCloseConfirm = true; Close(); };
                modal.Topmost = true;
                await modal.ShowDialog(this);
                return;
            }

            // 正常退出
            mEditor.ClearAutoSaveFile();
        }

        void UpdateTitle()
        {
            Title = "TuneLab - " + mEditor.Document.Name + (mEditor.Document.IsSaved ? string.Empty : "*");
        }

        readonly Editor mEditor;
    }
}
