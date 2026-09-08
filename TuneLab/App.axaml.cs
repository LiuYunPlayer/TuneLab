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
using TuneLab.Bridge;
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
                // 失败只报不停；哪些引擎要急切 Init、以及为何必须在挂工程之前，
                // 那条判据收在 SoundSourceEngines.InitAll（见那里），这里只管告诉眼前的用户。
                foreach (var failure in SoundSourceEngines.InitAll())
                {
                    var dialog = new Dialog();
                    dialog.SetTitle("Error");
                    dialog.SetMessage(failure);
                    dialog.AddButton("OK", Dialog.ButtonType.Primary);
                    dialog.Show();
                }

                mMainWindow = new MainWindow();
                desktop.MainWindow = mMainWindow;

                // 命令桥（默认关，用户在设置里开）：本机的外部进程经命名管道驱动这个实例，跑的是与
                // AI Agent 面板同一份命令。放在主窗口之后——编辑器在构造时才把宿主执行环境装上。
                CommandBridge.Init();

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
