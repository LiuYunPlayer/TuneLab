using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TuneLab.Animation;
using TuneLab.Audio;
using TuneLab.Audio.NAudio;
using TuneLab.Audio.SDL2;
using TuneLab.Foundation;
using TuneLab.Extensions;
using TuneLab.Extensions.Formats;
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

                // 检测启动参数（args[0] 是可执行文件自身，不是参数）
                var args = Environment.GetCommandLineArgs();
                Log.Info("Command line args: " + string.Join(" ", args, 1, Math.Max(0, args.Length - 1)));
                HandleStartupArgs(args[1..]);

                // 获取主线程SynchronizationContext
                var context = SynchronizationContext.Current ?? throw new InvalidOperationException("SynchronizationContext.Current is null");

                // 监听其他实例的启动参数
                Task.Run(() =>
                {
                    while (true)
                    {
                        using var pipeServer = new NamedPipeServerStream("TuneLab", PipeDirection.In);
                        pipeServer.WaitForConnection();

                        using var reader = new StreamReader(pipeServer);
                        // 一次连接 = 另一个实例的一次启动：把它那一批参数**收齐**再统一分流。逐行处理会让
                        // "一批里只认第一个工程"退化成"每一行都算第一个"，四个参数就又变回四个弹窗。
                        var received = new List<string>();
                        while (true)
                        {
                            var arg = reader.ReadLine();
                            if (arg == null)
                                break;   // 流到头了。原先写的是 continue，对方断开后这里会空转烧 CPU。

                            Log.Info($"Received from another instance: {arg}");
                            received.Add(arg);
                        }
                        if (received.Count != 0)
                            context.Post(_ => HandleStartupArgs(received), null);
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

    // 一批启动参数（命令行 / 双击关联文件 / 第二个实例转发过来的那一批）。规则与理由见 StartupArgs。
    public void HandleStartupArgs(IReadOnlyList<string> args)
    {
        if (mMainWindow == null)
            return;

        var plan = StartupArgs.Split(args, LooksLikeProjectFile);
        foreach (var ignored in plan.Ignored)
            Log.Info("Ignored startup argument (not a file we can open): " + ignored);

        if (plan.ExtensionPackages.Count != 0)
            mMainWindow.Editor.InstallExtensions(plan.ExtensionPackages);

        if (plan.ProjectPath != null)
            mMainWindow.Editor.OpenProjectByPath(plan.ProjectPath);
    }

    // 值不值得送去"打开工程"：文件在那儿，或者后缀是已注册的导入格式（那种情况下打不开要如实提示，
    // 用户多半是双击了一个已被删除/移走的工程）。都不是就不该弹框——那不是他要打开的东西。
    static bool LooksLikeProjectFile(string path)
    {
        if (File.Exists(path))
            return true;

        var suffix = Path.GetExtension(path).TrimStart('.');
        if (suffix.Length == 0)
            return false;

        foreach (var format in FormatsManager.GetAllImportFormats())
        {
            if (string.Equals(format, suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    MainWindow? mMainWindow = null;
}
