using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Utils;

internal static class Log
{
    public static void Debug(object? value)
    {
        Write("Debug", value);
    }

    public static void Info(object? value)
    {
        Write("Info ", value);
    }

    public static void Warning(object? value)
    {
        Write("Warn ", value);
    }

    public static void Error(object? value)
    {
        Write("Error ", value);
    }

    static void Write(string type, object? value)
    {
        System.Diagnostics.Debug.WriteLine(string.Format("[{0}][{1}]{2}", type, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), value));
    }
}
