using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace TuneLab.Extensions.Scripted;

// 共享内存音频面（设计 §7，M4b）。区域 = 文件后备的 MemoryMappedFile（Windows 下 Global\ 命名映射
// 需提权，POSIX 下 shm_open 是优化非必须 ⇒ 一律文件后备，随启动清扫/Dispose 删除）。
// 头部 24 字节：magic u32 | version u16 | flags u16 | sample_offset i64 | sample_count i32 | sample_rate i32，
// 其后 body = 交织 f32 little-endian（与 inline 面 base64 体同形）。
// 同步语义：同一区域页跨进程天然一致（page cache），commit 是唯一屏障——façade 写完发 commit，
// 宿主拷贝 body 进段缓冲；无锁、无原子序要求（对齐 in-proc "Write 后 Commit 才进 effect" 闸门）。
internal sealed class ShmRegion : IDisposable
{
    public const int HeaderBytes = 24;
    public const uint Magic = 0x41584C54;   // 写 LE 后盘上字节 = 'T','L','X','A'
    public const ushort LayoutVersion = 1;

    readonly FileStream m_file;
    MemoryMappedFile m_mapped;

    public readonly string FilePath;
    public long SampleOffset { get; private set; }
    public int SampleCount { get; private set; }
    public int SampleRate { get; private set; }
    long m_capacity;

    ShmRegion(FileStream file, MemoryMappedFile mapped, string path)
    {
        m_file = file;
        m_mapped = mapped;
        FilePath = path;
    }

    /// <summary>创建/重开区域并把容量设为 header + 4*sampleCount（不足则扩文件，body 交集内容保留）。</summary>
    public static ShmRegion Open(string path, long sampleOffset, int sampleCount, int sampleRate)
    {
        var capacity = (long)HeaderBytes + 4L * sampleCount;
        var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        if (file.Length < capacity)
            file.SetLength(capacity);
        var mapped = MemoryMappedFile.CreateFromFile(file, null, capacity, MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None, leaveOpen: true);
        var region = new ShmRegion(file, mapped, path) { SampleOffset = sampleOffset, SampleCount = sampleCount, SampleRate = sampleRate, m_capacity = capacity };
        region.WriteHeader(sampleOffset, sampleCount, sampleRate);
        return region;
    }

    /// <summary>改几何：先冲 header 进旧映射，再扩文件重开映射（body 内容留在文件里，映射须重建）。</summary>
    public void SetGeometry(long sampleOffset, int sampleCount, int sampleRate)
    {
        var capacity = (long)HeaderBytes + 4L * sampleCount;
        if (capacity > m_capacity)
        {
            WriteHeader(sampleOffset, sampleCount, sampleRate);   // header 经旧映射落文件
            m_mapped.Dispose();
            if (m_file.Length < capacity)
                m_file.SetLength(capacity);
            m_mapped = MemoryMappedFile.CreateFromFile(m_file, null, capacity, MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None, leaveOpen: true);
            m_capacity = capacity;
        }
        else
        {
            WriteHeader(sampleOffset, sampleCount, sampleRate);
        }
        SampleOffset = sampleOffset;
        SampleCount = sampleCount;
        SampleRate = sampleRate;
    }

    void WriteHeader(long sampleOffset, int sampleCount, int sampleRate)
    {
        var header = new byte[HeaderBytes];
        var span = header.AsSpan();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[0..4], Magic);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(span[4..6], LayoutVersion);
        // span[6..8] flags = 0（读视图位留 §6.2 input）
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(span[8..16], sampleOffset);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[16..20], sampleCount);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[20..24], sampleRate);
        using var view = m_mapped.CreateViewAccessor(0, HeaderBytes, MemoryMappedFileAccess.ReadWrite);
        view.WriteArray(0, header, 0, HeaderBytes);
    }

    /// <summary>读 body 区 [sampleStart, +sampleCount) 进 span（commit 拷贝 / resize 快照）。</summary>
    public void ReadBody(int sampleStart, Span<float> destination)
    {
        var byteLen = destination.Length * 4;
        if (byteLen == 0) return;
        using var view = m_mapped.CreateViewAccessor(HeaderBytes + 4L * sampleStart, byteLen, MemoryMappedFileAccess.ReadWrite);
        var bytes = new byte[byteLen];
        view.ReadArray(0, bytes, 0, byteLen);
        for (int i = 0; i < destination.Length; i++)
            destination[i] = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4));
    }

    /// <summary>宿主代理写 body（inline segment_write 在 shm 段的透传路径）。</summary>
    public void WriteBody(int sampleStart, ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return;
        var bytes = new byte[samples.Length * 4];
        for (int i = 0; i < samples.Length; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), samples[i]);
        using var view = m_mapped.CreateViewAccessor(HeaderBytes + 4L * sampleStart, bytes.Length, MemoryMappedFileAccess.ReadWrite);
        view.WriteArray(0, bytes, 0, bytes.Length);
    }

    public void Dispose()
    {
        try { m_mapped.Dispose(); } catch { }
        try { m_file.Dispose(); } catch { }
        try { File.Delete(FilePath); } catch { }   // 删不掉（对端仍映射）没关系：启动清扫兜底
    }
}

/// <summary>宿主侧区域分配器（按 boot-nonce 成族；§7：上次崩溃的残留可识别、可清扫）。</summary>
internal static class ShmPlane
{
    static int sSwept;
    static int sSeq;

    public static string RegionsDir => Path.Combine(Path.GetTempPath(), "tunelab-regions");

    public static string Slug(string packageId)
    {
        var sb = new StringBuilder();
        foreach (var c in packageId)
            if (char.IsLetterOrDigit(c) && sb.Length < 24) sb.Append(char.ToLowerInvariant(c));
        return sb.Length == 0 ? "pkg" : sb.ToString();
    }

    /// <summary>首次使用时清扫非本 boot 的残留（尽力而为）。</summary>
    public static void EnsureSwept()
    {
        if (Interlocked.Exchange(ref sSwept, 1) != 0) return;
        SweepExcept(SidecarIo.Nonce, RegionsDir);
    }

    /// <summary>删除目录里非本 boot 的 tlx-* 残留（测试注入形态）。</summary>
    internal static void SweepExcept(string keepNonce, string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "tlx-*"))
            {
                if (f.Contains(keepNonce, StringComparison.Ordinal)) continue;
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    public static ShmRegion Allocate(string packageId, long sampleOffset, int sampleCount, int sampleRate)
    {
        EnsureSwept();
        Directory.CreateDirectory(RegionsDir);
        var name = $"tlx-{SidecarIo.Nonce}-{Slug(packageId)}-{Interlocked.Increment(ref sSeq)}";
        return ShmRegion.Open(Path.Combine(RegionsDir, name), sampleOffset, sampleCount, sampleRate);
    }

    public static string Nonce => SidecarIo.Nonce;
}
