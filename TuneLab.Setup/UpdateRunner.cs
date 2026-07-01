using System;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.Setup.Core;

namespace TuneLab.Setup;

// -update 模式的驱动：用共享的 ProgressWindow 展示"更新→重启"，覆盖完成后等新版窗口出现再关。
internal static class UpdateRunner
{
    public static ProgressWindow CreateWindow(string installDir)
    {
        var window = new ProgressWindow();
        window.SetTitle("Updating TuneLab…".Tr(SetupI18N.Ctx));
        window.SetStatus("Waiting for TuneLab to close…".Tr(SetupI18N.Ctx));
        window.Opened += (_, _) => _ = RunAsync(window, installDir);
        return window;
    }

    static async Task RunAsync(ProgressWindow window, string installDir)
    {
        var options = new InstallOptions { InstallDir = installDir, IsUpdate = true, LaunchAfterInstall = false };
        var installer = new Installer(options);
        // 仅进度值驱动进度条；状态用本地化阶段词，不透传 Installer 内部英文原始消息。
        var progress = new Progress<InstallStatus>(s =>
        {
            window.SetProgress(s.Fraction);
            if (s.Fraction < 1) window.SetStatus("Updating…".Tr(SetupI18N.Ctx));
        });

        try
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Update runs on Windows only.");

            await installer.InstallAsync(progress, CancellationToken.None);

            // 覆盖很快、新版启动要数秒：保持窗口到其主窗口出现，文案切"正在重启"。
            window.SetIndeterminate();
            window.SetTitle("Restarting TuneLab…".Tr(SetupI18N.Ctx));
            window.SetStatus(string.Empty);
            installer.Launch();
            await WaitForAppWindowAsync(TimeSpan.FromSeconds(60));
        }
        catch (Exception ex)
        {
            window.SetStatus("Update failed: ".Tr(SetupI18N.Ctx) + ex.Message);
            await Task.Delay(3000);
        }

        window.Close();
    }

    // 轮询新版 TuneLab 进程，直到其主窗口句柄出现（即窗口已显示）或超时。
    static async Task WaitForAppWindowAsync(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(
                         System.IO.Path.GetFileNameWithoutExtension(ProductInfo.ExecutableName)))
            {
                try { p.Refresh(); if (p.MainWindowHandle != IntPtr.Zero) { await Task.Delay(300); return; } }
                catch { /* 进程可能刚退出 */ }
                finally { p.Dispose(); }
            }
            await Task.Delay(150);
        }
    }
}
