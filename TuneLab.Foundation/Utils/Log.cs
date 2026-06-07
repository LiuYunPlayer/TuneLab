using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Utils;

public interface ILogger
{
    void WriteLine(string message);
}

public static class Log
{
    public static void SetupLogger(ILogger logger)
    {
        mLogger = logger;
    }

    // 关停日志后端（刷盘 + 停后台线程）。异步缓冲后端需在进程退出 / 崩溃时调用，避免丢失未落盘日志。幂等。
    public static void Shutdown()
    {
        lock (mLock)
            (mLogger as IDisposable)?.Dispose();
    }

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
        lock (mLock)
        {
            mLogger?.WriteLine(string.Format("[{0}][{1}][TID:{2}] {3}", type, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), Environment.CurrentManagedThreadId, value));
        }
    }
    
    static readonly object mLock = new();

    static ILogger? mLogger;
}
