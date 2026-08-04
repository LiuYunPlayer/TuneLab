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
using TuneLab.Foundation;
using TuneLab.Extensions;
using TuneLab.SDK;
using TuneLab.GUI;
using TuneLab.Configs;
using TuneLab.UI;
using TuneLab.Utils;
using TuneLab.I18N;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Threading;
using System.IO.Pipes;

using TuneLab.Extensions.Instruments;
using TuneLab.Extensions.Voices;
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
                    ExtensionManager.LaunchPendingUninstalls();
                    ExtensionManager.Destroy();
                    AudioEngine.Destroy();
                };

                // init audio engine
                AudioUtils.Init(new NAudioCodec());
                AudioEngine.SampleRate.Value = Settings.SampleRate;
                AudioEngine.BufferSize.Value = Settings.BufferSize;
                if (!string.IsNullOrEmpty(Settings.AudioDriver)) AudioEngine.CurrentDriver.Value = Settings.AudioDriver;
                if (!string.IsNullOrEmpty(Settings.AudioDevice)) AudioEngine.CurrentDevice.Value = Settings.AudioDevice;
                AudioEngine.Init();
                AudioEngine.LoadKeySamples(Settings.PianoKeySamplesPath);
                AudioEngine.MasterGain = Settings.MasterGain;
                Settings.PianoKeySamplesPath.Modified.Subscribe(() => AudioEngine.LoadKeySamples(Settings.PianoKeySamplesPath));
                Settings.MasterGain.Modified.Subscribe(() => { AudioEngine.MasterGain = Settings.MasterGain; });
                Settings.BufferSize.Modified.Subscribe(() => { AudioEngine.BufferSize.Value = Settings.BufferSize; });
                Settings.SampleRate.Modified.Subscribe(() => { AudioEngine.SampleRate.Value = Settings.SampleRate; });
                Settings.AudioDriver.Modified.Subscribe(() => { AudioEngine.CurrentDriver.Value = Settings.AudioDriver; });
                Settings.AudioDevice.Modified.Subscribe(() => { AudioEngine.CurrentDevice.Value = Settings.AudioDevice; });

                LegacyCompatLoader.Wire();
                ExtensionManager.LoadExtensions();

                // 音源引擎（voice 与 instrument）须在 MainWindow 构建前初始化：MainWindow 构造时会新建默认
                // 工程，其 part 立即 Activate 并构建合成管线，此刻引擎若未 Init 则会回落到空会话且无回建
                // 路径（起动即无声）。（同时也让「设置音源」右键菜单更快弹出。）
                //
                // instrument 与 voice 在这件事上【同构】：两者都挂在 MidiPart 上作音源（XOR 二选一）、
                // 都要在菜单里列出自己的音源目录，所以两者都得急切 Init——只 Init voice 会让 instrument part
                // 起动无声、菜单也慢。effect 不在此列且不该在：它没有音源目录，按 part 用到才 Init 是对的。
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
                foreach (var engine in InstrumentsManager.GetAllInstrumentEngines())
                {
                    try
                    {
                        InstrumentsManager.InitEngine(engine);
                    }
                    catch (Exception ex)
                    {
                        var dialog = new Dialog();
                        dialog.SetTitle("Error");
                        dialog.SetMessage(string.Format("Instrument engine [{0}] failed to init:\n{1}", engine, ex.Message));
                        dialog.AddButton("OK", Dialog.ButtonType.Primary);
                        dialog.Show();
                    }
                }

                mMainWindow = new MainWindow();
                desktop.MainWindow = mMainWindow;

                // 检测启动参数
                var args = Environment.GetCommandLineArgs();
                Log.Info($"Command line args:");
                for (int i = 1; i < args.Length; i++)
                {
                    Log.Info(args[i]);
                    HandleArg(args[i]);
                }

                // 获取主线程SynchronizationContext
                var context = SynchronizationContext.Current ?? throw new InvalidOperationException("SynchronizationContext.Current is null");

                // 监听其他实例的启动参数
                Task.Run(() =>
                {
                    while (true)
                    {
                        var pipeServer = new NamedPipeServerStream("TuneLab", PipeDirection.In);
                        pipeServer.WaitForConnection();

                        using var reader = new StreamReader(pipeServer);
                        while (pipeServer.IsConnected)
                        {
                            var arg = reader.ReadLine();
                            if (arg == null)
                                continue;

                            Log.Info($"Received from another instance: {arg}");
                            context.Post(_ => HandleArg(arg), null);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                var dialog = new Dialog();
                dialog.SetTitle("Launch Failed");
                dialog.SetMessage(ex.ToString());
                dialog.AddButton("Quit", Dialog.ButtonType.Primary).Clicked += () => { Process.GetCurrentProcess().Kill(); };
                dialog.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void HandleArg(string arg)
    {
        if (mMainWindow == null)
            return;

        // 按扩展名分流：.tlx 是扩展包而不是工程，当工程打开只会报「打开失败」。
        // 这条路同时服务命令行、双击关联文件、以及第二个实例转发过来的参数。
        if (Path.GetExtension(arg).Equals(".tlx", StringComparison.OrdinalIgnoreCase))
            mMainWindow.Editor.InstallExtensions([arg]);
        else
            mMainWindow.Editor.OpenProjectByPath(arg);
    }

    MainWindow? mMainWindow = null;
}
