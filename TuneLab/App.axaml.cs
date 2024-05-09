using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TuneLab.Audio;
using TuneLab.Animation;
using TuneLab.Extensions;
using TuneLab.Views;
using TuneLab.Utils;
using System.Diagnostics;

namespace TuneLab;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            mLockFile = LockFile.Create(PathManager.LockFilePath);
            if (mLockFile == null)
            {
                // TODO: 传递启动参数给当前运行的app
                Process.GetCurrentProcess().Kill();
                Process.GetCurrentProcess().WaitForExit();
                return;
            }

            desktop.Startup += (s, e) =>
            {
                AnimationManager.SharedManager.Init();
            };
            desktop.Exit += (s, e) =>
            {
                ExtensionManager.Destroy();
                AudioEngine.Destroy();
                mLockFile?.Dispose();
            };

            AudioEngine.Init();
            ExtensionManager.LoadExtensions();
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    LockFile? mLockFile;
}
