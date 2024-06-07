using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TuneLab.Audio;
using TuneLab.Animation;
using TuneLab.Extensions;
using TuneLab.Views;
using TuneLab.Utils;
using System.Diagnostics;
using System;
using TuneLab.GUI;
using TuneLab.Extensions.Voices;
using TuneLab.Audio.NAudio;
using TuneLab.Audio.SDL2;

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
            try
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

                AudioUtils.Init(new NAudioCodec());
                AudioEngine.Init(new SDLAudioEngine());
                ExtensionManager.LoadExtensions();
                desktop.MainWindow = new MainWindow();
            }
            catch (Exception ex)
            {
                var dialog = new Dialog();
                dialog.SetTitle("Launch Failed");
                dialog.SetMessage(ex.ToString());
                dialog.AddButton("Quit", Dialog.ButtonType.Primary).Clicked += () => { Process.GetCurrentProcess().Kill(); };
                dialog.Show();
            }

            // 暂时改为提前初始化，使Set Voice右键菜单能更快弹出
            foreach (var engine in VoicesManager.GetAllVoiceEngines())
            {
                try
                {
                    VoicesManager.InitEngine(engine);
                }
                catch (Exception ex)
                {
                    var dialog = new Dialog();
                    dialog.SetTitle("Error");
                    dialog.SetMessage(string.Format("Voice engine [{0}] failed to init:\n{1}", engine, ex.Message));
                    dialog.AddButton("OK", Dialog.ButtonType.Primary);
                    dialog.Show();
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    LockFile? mLockFile;
}
