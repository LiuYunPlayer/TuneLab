using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

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
}
