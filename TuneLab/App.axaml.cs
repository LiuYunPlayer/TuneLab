using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TuneLab.Audio;
using TuneLab.Animation;
using TuneLab.Extensions;
using TuneLab.Views;

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
            desktop.Startup += (s, e) =>
            {
                AnimationManager.SharedManager.Init();
            };
            desktop.Exit += (s, e) =>
            {
                ExtensionManager.Destroy();
                AudioEngine.Destroy();
            };

            AudioEngine.Init();
            ExtensionManager.LoadExtensions();
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
