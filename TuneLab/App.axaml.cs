using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TuneLab.Audio;
using TuneLab.Animation;
using TuneLab.Extensions;
using TuneLab.Utils;
using System.Diagnostics;
using System;
using System.IO;
using TuneLab.GUI;
using TuneLab.Extensions.Voices;
using TuneLab.Audio.NAudio;
using TuneLab.Audio.SDL2;
using TuneLab.UI;
using TuneLab.Base.Utils;
using TuneLab.I18N;
using Avalonia.Threading;

namespace TuneLab;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
    private void UpdateDialog(UpdateInfo mUpdateCheck)
    {
        var dialog = new Dialog();
        dialog.SetTitle("Update Available".Tr(TC.Dialog));
        dialog.SetMessage("Version".Tr(TC.Dialog) + $": {mUpdateCheck.version}\n" +"Public Date".Tr(TC.Dialog) + $": {mUpdateCheck.publishedAt}\n\n{mUpdateCheck.description}");
        dialog.SetTextAlignment(Avalonia.Media.TextAlignment.Left);
        dialog.AddButton("Ignore".Tr(TC.Dialog), Dialog.ButtonType.Normal).Clicked += () => AppUpdateManager.SaveIgnoreVersion(mUpdateCheck.version);
        dialog.AddButton("Later".Tr(TC.Dialog), Dialog.ButtonType.Normal);
        dialog.AddButton("Download".Tr(TC.Dialog), Dialog.ButtonType.Primary).Clicked += () =>
        {
            ProcessHelper.OpenUrl(mUpdateCheck.url);
        };
        dialog.Show();
    }
    public async void CheckUpdate()
    {
        try
        {
            var mUpdateCheck = await AppUpdateManager.CheckForUpdate();
            if (mUpdateCheck != null)
            {
                Log.Info($"Update available: {mUpdateCheck.version}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateDialog(mUpdateCheck);
                });
            } else
            {
                Log.Info("No update available.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"CheckUpdate: {ex.Message}");
        }
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                Log.SetupLogger(new FileLogger(Path.Combine(PathManager.LogsFolder, "TuneLab_" + DateTime.Now.ToString("yyyy-MM-dd_hh-mm-ss") + ".log")));
                Log.Info("Version: " + AppInfo.Version);
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

                CheckUpdate();
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
