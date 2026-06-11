using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Utils;

// 日志输出后端（sink）：Log 静态门面把格式化后的行写到这里。
// 与 SDK.Base.Environment.ILogger（插件作用域日志器）区分：sink 是宿主内部的落盘抽象。
public interface ILogSink
{
    void WriteLine(string message);
}

public static class Log
{
    public static void SetupLogger(ILogSink logger)
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

    static ILogSink? mLogger;
}
