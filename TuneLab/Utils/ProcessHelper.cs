using System.Collections.Generic;
using System.Diagnostics;

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
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
