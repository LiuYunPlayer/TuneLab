using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
using TuneLab.PluginHost;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Threading;
using System.IO.Pipes;

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
                    // Shutdown plugin host first (before audio engine)
                    try
                    {
                        PluginHostManager.Instance.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to shutdown PluginHost: {ex}");
                    }
                    
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

                // Init plugin host
                try
                {
                    PluginHostManager.Instance.Initialize();
                    Log.Info("PluginHost initialized successfully");
                    
                    // Add default VST scan paths
                    AddDefaultPluginScanPaths();
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to initialize PluginHost: {ex}");
                    var dialog = new Dialog();
                    dialog.SetTitle("Warning");
                    dialog.SetMessage($"Plugin host failed to initialize:\n{ex.Message}\nVST plugins will not be available.");
                    dialog.AddButton("OK", Dialog.ButtonType.Primary);
                    dialog.Show();
                }

                ExtensionManager.LoadExtensions();
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

    public void HandleArg(string arg)
    {
        mMainWindow?.Editor.OpenProjectByPath(arg);
    }

    private static void AddDefaultPluginScanPaths()
    {
        var manager = PluginHostManager.Instance;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Common VST3 paths on Windows
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var commonProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
            var commonProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86);
            
            // VST3 standard paths
            var vst3Paths = new[]
            {
                Path.Combine(commonProgramFiles, "VST3"),
                Path.Combine(commonProgramFilesX86, "VST3"),
                Path.Combine(programFiles, "Common Files", "VST3"),
                Path.Combine(programFilesX86, "Common Files", "VST3"),
            };
            
            foreach (var path in vst3Paths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        manager.AddScanPath(path);
                        Log.Info($"Added plugin scan path: {path}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Failed to add scan path {path}: {ex.Message}");
                    }
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Common VST3/AU paths on macOS
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var macPaths = new[]
            {
                "/Library/Audio/Plug-Ins/VST3",
                "/Library/Audio/Plug-Ins/Components",
                Path.Combine(home, "Library/Audio/Plug-Ins/VST3"),
                Path.Combine(home, "Library/Audio/Plug-Ins/Components"),
            };
            
            foreach (var path in macPaths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        manager.AddScanPath(path);
                        Log.Info($"Added plugin scan path: {path}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Failed to add scan path {path}: {ex.Message}");
                    }
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Common VST3 paths on Linux
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var linuxPaths = new[]
            {
                "/usr/lib/vst3",
                "/usr/local/lib/vst3",
                Path.Combine(home, ".vst3"),
            };
            
            foreach (var path in linuxPaths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        manager.AddScanPath(path);
                        Log.Info($"Added plugin scan path: {path}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Failed to add scan path {path}: {ex.Message}");
                    }
                }
            }
        }
    }

    MainWindow? mMainWindow = null;
}
