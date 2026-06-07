using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Utils;

namespace TuneLab.Utils;

internal static class ProcessHelper
{
    public static Process CreateProcess(string applicationPath, IEnumerable<string> args)
    {
        var commandLine = string.Empty;
        foreach (var arg in args)
        {
            commandLine += "\"" + arg + "\" ";
        }

        Process process = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                UseShellExecute = true,
                FileName = applicationPath,
                Arguments = commandLine,
            }
        };
        process.Start();

        return process;
    }

    // OpenUrl
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            // 否则失败会被静默吞掉（如该扩展名无默认关联程序）。
            Log.Error($"Failed to open url: {url}\n{ex}");
        }
    }

    // 打开文件：优先用系统文件关联（用户配过的程序最贴合预期，如 .log→VS Code）；该扩展名无关联导致失败时，
    // 回退到默认文本编辑器（Windows 记事本 / mac open -t / Linux xdg-open），避免像旧实现那样静默失败。
    public static void OpenFile(string path)
    {
        Log.Info($"OpenFile: {path}");
        // 委托系统外壳「打开」（等价于在文件管理器里双击），用该类型的默认程序（如 .log→VS Code）。
        // 不用 Process.Start(UseShellExecute=true, FileName=<文件>)：实测它对文件关联会静默失效（不抛异常也不打开），
        // 而 explorer / open / xdg-open（即双击背后的外壳路径）可靠。无关联时回退到默认文本编辑器。
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = true });
            return;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open file via shell, fallback to text editor: {path}\n{ex}");
        }

        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open", $"-t \"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open file in text editor: {path}\n{ex}");
        }
    }
}
