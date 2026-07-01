using System.Collections.Generic;

namespace TuneLab.Setup.Core;

/// <summary>解压过程中回报的单条进度。</summary>
internal readonly record struct ExtractProgress(long BytesDone, long BytesTotal, string CurrentEntry);

/// <summary>
/// 载荷来源抽象：把"要装的一整套 app 文件"从某个容器铺到目标目录。
/// 实现分两类——开发期从磁盘目录/zip 读，发布期从附加在安装器自身尾部的 SFX 档读。
/// </summary>
internal interface IPayloadProvider
{
    /// <summary>载荷解压后总字节数（用于进度条；无法预知时返回 -1）。</summary>
    long UncompressedSize { get; }

    /// <summary>载荷内的相对条目路径枚举（诊断/预览用）。</summary>
    IEnumerable<string> EnumerateEntries();

    /// <summary>把全部条目铺到 <paramref name="targetDir"/>，逐条回报进度。</summary>
    void ExtractTo(string targetDir, System.IProgress<ExtractProgress>? progress, System.Threading.CancellationToken ct);
}
