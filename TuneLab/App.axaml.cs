using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using TuneLab.Animation;
using TuneLab.Audio;
using TuneLab.Audio.NAudio;
using TuneLab.Audio.SDL2;
using TuneLab.Base.Utils;
using TuneLab.Extensions;
using TuneLab.Extensions.Voices;
using TuneLab.GUI;
using TuneLab.Configs;
using TuneLab.UI;
using TuneLab.Utils;
using TuneLab.I18N;
using System.Linq;

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
                desktop.Startup += (s, e) =>
                {
                    AnimationManager.SharedManager.Init();
                };
                desktop.Exit += (s, e) =>
                {
                    ExtensionManager.Destroy();
                    AudioEngine.Destroy();
                };

                // init translation
                TranslationManager.Init(PathManager.TranslationsFolder);
                TranslationManager.CurrentLanguage.Value = TranslationManager.Languages.Contains(Settings.Language.Value) ? Settings.Language : TranslationManager.GetCurrentOSLanguage();
                Settings.Language.Modified.Subscribe(() => TranslationManager.CurrentLanguage.Value = Settings.Language);

                // init audio engine
                AudioUtils.Init(new NAudioCodec());
                AudioEngine.Init(new SDLPlaybackHandler());
                AudioEngine.LoadKeySamples(Settings.KeySamplesPath);
                Settings.KeySamplesPath.Modified.Subscribe(() => AudioEngine.LoadKeySamples(Settings.KeySamplesPath));

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
}
