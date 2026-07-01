using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace TuneLab.Setup.Core;

/// <summary>
/// 从磁盘目录读载荷。开发期最方便：把 `dotnet publish` 出的自包含目录放到安装器 exe 旁的 payload\ 即可跑通全流程，
/// 无需先做 SFX 打包。
/// </summary>
internal sealed class DirectoryPayloadProvider : IPayloadProvider
{
    readonly string mSourceDir;
    readonly string[] mFiles;

    public DirectoryPayloadProvider(string sourceDir)
    {
        mSourceDir = Path.GetFullPath(sourceDir);
        mFiles = Directory.GetFiles(mSourceDir, "*", SearchOption.AllDirectories);
    }

    public long UncompressedSize => mFiles.Sum(f => new FileInfo(f).Length);

    public IEnumerable<string> EnumerateEntries()
        => mFiles.Select(f => Path.GetRelativePath(mSourceDir, f));

    public void ExtractTo(string targetDir, IProgress<ExtractProgress>? progress, CancellationToken ct)
    {
        long total = UncompressedSize;
        long done = 0;
        Directory.CreateDirectory(targetDir);

        foreach (var src in mFiles)
        {
            ct.ThrowIfCancellationRequested();

            string rel = Path.GetRelativePath(mSourceDir, src);
            string dest = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            CopyWithRetry(src, dest, ct);

            done += new FileInfo(src).Length;
            progress?.Report(new ExtractProgress(done, total, rel));
        }
    }

    // 更新场景下目标文件可能刚被退出的 app 短暂占用（OS 尚未完全释放句柄），重试等其释放。
    static void CopyWithRetry(string src, string dest, CancellationToken ct)
    {
        const int maxAttempts = 20;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(src, dest, overwrite: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < maxAttempts)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(300);
            }
        }
    }
}
