using System;
using System.IO;
using TuneLab;
using TuneLab.Extensions.Derivers;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// AudioDerivationCacheManager.Contains（stage②：提交时的廉价缓存命中探测）与 Put/TryGet 的一致性：
//   · 未写入的键 => Contains=false（提交据此建任务跑模型）；
//   · Put 后 => Contains=true（提交据此跳过跑模型、记录即刻可应用），且 TryGet 忠实读回。
// 缓存是内容寻址的可弃磁盘缓存；本测用唯一 key 写一条并在 finally 清理，不干扰真实缓存内容。
public class DerivationCacheContainsTests
{
    [Fact]
    public void Contains_TracksPut_AndTryGetReadsBack()
    {
        var key = "test-contains-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.False(AudioDerivationCacheManager.Contains(key));   // 未写入 => 缺失
            Assert.False(AudioDerivationCacheManager.TryGet(key, out _));

            var result = new DerivedResult
            {
                Tracks = new[] { new DerivedTrack { Name = "T", Parts = new DerivedPart[]
                {
                    new DerivedMidiPart { Notes = new[] { new DerivedNote { StartTime = 0, EndTime = 1, Pitch = 60, Lyric = "la" } } },
                } } },
                Tempos = new[] { new DerivedTempo { Time = 0, Bpm = 120 } },
            };
            AudioDerivationCacheManager.Put(key, result);

            Assert.True(AudioDerivationCacheManager.Contains(key));     // Put 后 => 命中
            Assert.True(AudioDerivationCacheManager.TryGet(key, out var restored));
            var midi = Assert.IsType<DerivedMidiPart>(restored.Tracks[0].Parts[0]);
            Assert.Equal("la", midi.Notes[0].Lyric);
            Assert.Equal(120, restored.Tempos[0].Bpm);
        }
        finally
        {
            var path = Path.Combine(PathManager.DerivationCacheFolder, key + ".json");
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
