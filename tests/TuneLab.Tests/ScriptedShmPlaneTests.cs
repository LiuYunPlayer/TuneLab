using System;
using System.IO;
using System.Linq;
using TuneLab.Extensions.Scripted;
using Xunit;

namespace TuneLab.Tests;

// M4b 共享内存区域单元：头布局（§7 钉死的字节表）、body 读写、扩容量保内容、boot 清扫。
public class ScriptedShmPlaneTests
{
    [Fact]
    public void RegionHeaderMatchesWireSpecLayout()
    {
        var path = Path.Combine(Path.GetTempPath(), "tlx-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var region = ShmRegion.Open(path, 88200, 44100, 44100);
            try
            {
                // 共享模式：宿主句柄是 RW，任何旁观读必须带 FileShare.ReadWrite（同真实 sidecar 纪律）
                byte[] bytes;
                using (var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bytes = new byte[probe.Length];
                    probe.ReadExactly(bytes);
                }
                Assert.Equal(24 + 44100 * 4, bytes.Length);
                Assert.Equal(new byte[] { 0x54, 0x4C, 0x58, 0x41 }, bytes[0..4]);   // 'TLXA' on disk
                Assert.Equal(new byte[] { 1, 0 }, bytes[4..6]);                     // version
                Assert.Equal(new byte[] { 0, 0 }, bytes[6..8]);                     // flags
                Assert.Equal(BitConverter.GetBytes(88200L), bytes[8..16]);
                Assert.Equal(BitConverter.GetBytes(44100), bytes[16..20]);
                Assert.Equal(BitConverter.GetBytes(44100), bytes[20..24]);
            }
            finally { region.Dispose(); }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void RegionBodySharedAcrossIndependentHandles()
    {
        var path = Path.Combine(Path.GetTempPath(), "tlx-test-" + Guid.NewGuid().ToString("N"));
        // 真实形态：宿主区域句柄全程存活，sidecar 另开句柄读写同一文件后备区。
            var host = ShmRegion.Open(path, 0, 8, 44100);
            try
            {
                using var sidecarFile = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                using (var sidecar = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(
                           sidecarFile.SafeFileHandle, null, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite,
                           System.IO.HandleInheritability.None, leaveOpen: true))
                using (var acc = sidecar.CreateViewAccessor(ShmRegion.HeaderBytes + 2 * 4, 3 * 4, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite))
                {
                    var raw = new byte[12];
                    for (int i = 0; i < 3; i++) BitConverter.GetBytes(new[] { 0.25f, -1.5f, 3.5f }[i]).CopyTo(raw, i * 4);
                    acc.WriteArray(0, raw, 0, raw.Length);
                    acc.Flush();
                }
                var buf = new float[3];
                host.ReadBody(2, buf);
                Assert.Equal(new float[] { 0.25f, -1.5f, 3.5f }, buf);
            }
            finally { host.Dispose(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void RegionGrowKeepsBodyContent()
    {
        var path = Path.Combine(Path.GetTempPath(), "tlx-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var region = ShmRegion.Open(path, 0, 4, 44100);
            region.WriteBody(0, new float[] { 1f, 2f, 3f, 4f });
            region.SetGeometry(0, 8, 44100);   // 扩容量（resize 重铺前的形态：同文件扩）
            var buf = new float[8];
            region.ReadBody(0, buf);
            Assert.Equal(new float[] { 1, 2, 3, 4, 0, 0, 0, 0 }, buf);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SweepDeletesForeignBootKeepsCurrent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tlx-sweep-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var foreign = Path.Combine(dir, "tlx-0123456789ab-pkg-1");
            var mine = Path.Combine(dir, $"tlx-{ShmPlane.Nonce}-pkg-1");
            File.WriteAllText(foreign, "stale");
            File.WriteAllText(mine, "current");
            ShmPlane.SweepExcept(ShmPlane.Nonce, dir);
            Assert.False(File.Exists(foreign));
            Assert.True(File.Exists(mine));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SlugIsPathSafeAndCapped()
    {
        Assert.Equal("comtestx", ShmPlane.Slug("com.test/../x"));
        Assert.Equal(24, ShmPlane.Slug(new string('a', 100)).Length);
    }
}
