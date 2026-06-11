using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using TuneLab.Foundation.Utils;

namespace TuneLab.Utils;

// 异步缓冲日志：调用线程仅把消息入队、绝不碰磁盘 / 控制台 I/O；单后台线程顺序写文件 + 控制台，
// 队列排空（空闲）或累积到阈值时才 Flush——突发日志合并为少量刷盘、空闲时即时落盘、长突发也有上界。
// 关停（Dispose）时排空队列 + 末次刷盘，避免丢日志；进程退出 / 崩溃 / 硬杀早退由 Program 调 Log.Shutdown() 兜底。
internal class FileLogger : ILogSink, IDisposable
{
    public FileLogger(string path)
    {
        PathManager.MakeSureExist(Path.GetDirectoryName(path)!);
        mStreamWriter = new StreamWriter(path) { AutoFlush = false };
        mWorker = new Thread(ProcessQueue) { IsBackground = true, Name = "FileLogger" };
        mWorker.Start();
    }

    public void WriteLine(string message)
    {
        // 仅入队，调用线程零 I/O。关停后（CompleteAdding）入队会抛，吞掉即丢弃尾部日志。
        try { mQueue.Add(message); }
        catch (InvalidOperationException) { }
    }

    void ProcessQueue()
    {
        long lastFlush = Environment.TickCount64;
        try
        {
            foreach (var message in mQueue.GetConsumingEnumerable())
            {
                System.Diagnostics.Debug.WriteLine(message);
                Console.WriteLine(message);
                mStreamWriter.WriteLine(message);
                // 队列排空即刷盘（稀疏日志逐行落盘）；持续突发时也至少每 FlushIntervalMs 刷一次——
                // 把「硬崩溃丢失未刷盘日志」的窗口限制在该间隔内，同时避免突发逐行刷盘拖垮后台。
                long now = Environment.TickCount64;
                if (mQueue.Count == 0 || now - lastFlush >= FlushIntervalMs)
                {
                    mStreamWriter.Flush();
                    lastFlush = now;
                }
            }
        }
        catch (ObjectDisposedException) { /* 关停竞态：写流已释放 */ }
        TryFlush();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref mShutdown, 1) != 0)
            return;

        mQueue.CompleteAdding();     // 令后台线程 foreach 排空剩余后自然结束
        if (!mWorker.Join(2000))     // 等它写完 + 末次 flush
            TryFlush();              // 卡住时兜底（仅崩溃 / 强退路径，容许竞态）
    }

    void TryFlush()
    {
        try { mStreamWriter.Flush(); }
        catch { /* 关停竞态 */ }
    }

    const int FlushIntervalMs = 50;
    int mShutdown;
    readonly StreamWriter mStreamWriter;
    readonly BlockingCollection<string> mQueue = new(new ConcurrentQueue<string>());
    readonly Thread mWorker;
}
