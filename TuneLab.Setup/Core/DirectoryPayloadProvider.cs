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
            File.Copy(src, dest, overwrite: true);

            done += new FileInfo(src).Length;
            progress?.Report(new ExtractProgress(done, total, rel));
        }
    }
}
