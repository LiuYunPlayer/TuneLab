using System;
using System.IO;
using System.Text;

namespace TuneLab.Foundation;

// 原子写文件：仿 Qt 的 QSaveFile。先写到目标同目录下的临时文件，Commit() 时刷盘并原子 rename 覆盖目标——
// 要么旧内容完整保留、要么新内容完整替换，绝不会留下写到一半的半截文件（崩溃/断电/并发读时安全）。
// 未 Commit 即 Dispose（异常或主动放弃）则丢弃临时文件、目标不受影响。
//
// 用法（流式）：using var f = new SaveFile(path); /* 写 f.Stream */ f.Commit();
// 便捷封装见末尾静态方法 WriteAllText / WriteAllBytes / Write，可直接替换 File.WriteAllText 等调用点。
public sealed class SaveFile : IDisposable
{
    readonly string mTargetPath;
    readonly string mTempPath;
    FileStream? mStream;
    bool mCommitted;

    public SaveFile(string targetPath)
    {
        mTargetPath = Path.GetFullPath(targetPath);
        var dir = Path.GetDirectoryName(mTargetPath) ?? throw new ArgumentException("Invalid target path: " + targetPath);
        Directory.CreateDirectory(dir);
        // 临时文件必须与目标同目录（同卷），rename 才能原子；用隐藏前缀 + GUID 避免与并发写者撞名。
        mTempPath = Path.Combine(dir, "." + Path.GetFileName(mTargetPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        mStream = new FileStream(mTempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    // 写入用的临时流。Commit 前内容都只落在临时文件上。
    public Stream Stream => mStream ?? throw new ObjectDisposedException(nameof(SaveFile));

    // 提交：刷盘后原子 rename 覆盖目标。成功返回 true。失败抛异常（临时文件由 Dispose 清理）。
    public bool Commit()
    {
        if (mStream == null)
            return false;
        mStream.Flush(true);   // true=连同 OS 缓冲一起刷到磁盘，确保 rename 时内容已落盘
        mStream.Dispose();
        mStream = null;
        File.Move(mTempPath, mTargetPath, overwrite: true);  // 同卷原子替换（Win:MoveFileEx / Unix:rename）
        mCommitted = true;
        return true;
    }

    public void Dispose()
    {
        if (mStream != null)
        {
            mStream.Dispose();
            mStream = null;
        }
        if (!mCommitted)
        {
            try
            {
                if (File.Exists(mTempPath))
                    File.Delete(mTempPath);
            }
            catch (Exception ex)
            {
                Log.Warning("SaveFile failed to clean up temp file " + mTempPath + ": " + ex.Message);
            }
        }
    }

    // ── 便捷封装：一行替换原 File.WriteAllText / WriteAllBytes，自动走原子写 ──

    // 文本默认 UTF-8 无 BOM（与 File.WriteAllText 默认一致）。
    public static void WriteAllText(string path, string contents)
        => WriteAllBytes(path, new UTF8Encoding(false).GetBytes(contents ?? string.Empty));

    public static void WriteAllText(string path, string contents, Encoding encoding)
        => WriteAllBytes(path, encoding.GetBytes(contents ?? string.Empty));

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        using var f = new SaveFile(path);
        f.Stream.Write(bytes, 0, bytes.Length);
        f.Commit();
    }

    // 流式写入点（如 stream.CopyTo / 序列化器写入）用这个：在回调里往 Stream 写，回调正常返回即提交。
    public static void Write(string path, Action<Stream> write)
    {
        using var f = new SaveFile(path);
        write(f.Stream);
        f.Commit();
    }
}
