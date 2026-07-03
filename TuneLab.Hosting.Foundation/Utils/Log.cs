using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

// 日志输出后端（sink）：Log 静态门面把格式化后的行写到这里。
// 与 TuneLab.SDK.ILogger（插件作用域日志器）区分：sink 是宿主内部的落盘抽象。
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

    // 带归因的错误日志：输出 "message: 异常(含堆栈)"，并解析 ex 的来源——堆栈落在插件 ALC 内时
    // 自动加 "[插件包名] " 前缀（与插件日志器的前缀同形，可按包名 grep 聚合）。
    // 记录异常一律用本方法原样传入 Exception，不要自己拼进 message（拼成字符串后无法归因）。
    public static void ErrorAttributed(object? value, Exception ex)
    {
        Write("Error ", AttributionPrefix(ex) + value + ": " + ex);
    }

    // 异常→归因前缀："[scope] " 或空串。绝不抛出——多在崩溃路径上调用，次生异常会吃掉日志本体。
    static string AttributionPrefix(Exception ex)
    {
        try
        {
            var scope = ResolveScope(ex);
            return string.IsNullOrEmpty(scope) ? "" : "[" + scope + "] ";
        }
        catch
        {
            return ""; // 归因失败退化为无前缀，不得影响日志本体
        }
    }

    // 沿异常堆栈帧找第一个属于非 Default、有名 ALC 的程序集，返回其 ALC 名——宿主的插件加载
    // 上下文以插件包目录名命名（插件改不了，归因可信），即归因到插件包；纯宿主/BCL 堆栈返回 null。
    // 深帧优先（最靠近抛出点的插件帧胜出），本层无果再下探内层异常（AggregateException 逐个内层试）。
    static string? ResolveScope(Exception? ex)
    {
        if (ex == null)
            return null;

        foreach (var frame in new StackTrace(ex).GetFrames())
        {
            var assembly = frame.GetMethod()?.Module.Assembly;
            if (assembly == null)
                continue;

            var alc = AssemblyLoadContext.GetLoadContext(assembly);
            if (alc != null && alc != AssemblyLoadContext.Default && !string.IsNullOrEmpty(alc.Name))
                return alc.Name;
        }

        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (ResolveScope(inner) is string scope)
                    return scope;
            }
            return null;
        }

        return ResolveScope(ex.InnerException);
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
