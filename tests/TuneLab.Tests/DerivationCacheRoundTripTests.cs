using System;
using System.Collections.Generic;
using TuneLab.Extensions.Derivers;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// deriver 缓存的手写 JSON 序列化（AudioDerivationCacheManager 的 DerivedResult ↔ JSON）往返语义。
// 只测受影响范围：Derived* 产物族忠实回环——含 null 槽（「不产」）保持 null、Point (X,Y)、part 多态(midi/audio)、
// part 裁剪 ±∞ 省键回环、phoneme 与 DataInfo 同构随产物往返、tempo/timesig 时间线产物。缓存可弃、读坏当未命中；本测保「命中即忠实」。
public class DerivationCacheRoundTripTests
{
    static DerivedResult RoundTrip(DerivedResult r)
        => AudioDerivationCacheManager.ReadResult(AudioDerivationCacheManager.WriteResult(r));

    [Fact]
    public void MidiPart_Notes_Pitch_Phonemes_Roundtrip()
    {
        var part = new DerivedMidiPart
        {
            StartTime = 1.5,
            Notes = new[]
            {
                new DerivedNote { StartTime = 1.5, EndTime = 2.0, Pitch = 60, Lyric = "la",
                    BodyOffset = -0.05,
                    LeadingPhonemes = new[] { new DerivedPhoneme { Symbol = "l", Duration = 0.05, StretchWeight = 0 } },
                    BodyPhonemes = new[] { new DerivedPhoneme { Symbol = "a", Duration = 0.4, StretchWeight = 1 } } },
                new DerivedNote { StartTime = 2.0, EndTime = 2.4, Pitch = 62 },
            },
            Pitch = new DerivedPitch { Segments = new IReadOnlyList<Point>[] { new[] { new Point(1.5, 60.1), new Point(1.75, 60.3) } } },
        };
        var restored = RoundTrip(new DerivedResult { Tracks = new[] { new DerivedTrack { Name = "T", Parts = new DerivedPart[] { part } } } });

        Assert.NotNull(restored.Tracks);
        var midi = Assert.IsType<DerivedMidiPart>(restored.Tracks![0].Parts[0]);
        Assert.Equal(1.5, midi.StartTime);
        // 未写 EndTime → +∞ 默认回环（终点开放）。
        Assert.True(double.IsPositiveInfinity(midi.EndTime));
        Assert.Equal(2, midi.Notes!.Count);
        Assert.Equal("la", midi.Notes[0].Lyric);
        Assert.Equal("", midi.Notes[1].Lyric);
        Assert.Equal(-0.05, midi.Notes[0].BodyOffset, 6);
        Assert.Single(midi.Notes[0].LeadingPhonemes);
        Assert.Equal("l", midi.Notes[0].LeadingPhonemes[0].Symbol);
        Assert.Equal("a", midi.Notes[0].BodyPhonemes[0].Symbol);
        Assert.Empty(midi.Notes[1].LeadingPhonemes);
        Assert.Equal(60.3, midi.Pitch.Segments[0][1].Y, 6);
    }

    [Fact]
    public void AudioPart_Crop_And_Timeline_Roundtrip()
    {
        var restored = RoundTrip(new DerivedResult
        {
            Tracks = new[] { new DerivedTrack { Parts = new DerivedPart[] { new DerivedAudioPart { StartTime = 3.5, EndTime = 5.5 } } } },
            Tempos = new[] { new DerivedTempo { Time = 0, Bpm = 120 } },
            TimeSignatures = new[] { new DerivedTimeSignature { Time = 0, Numerator = 3, Denominator = 4 } },
        });

        var audio = Assert.IsType<DerivedAudioPart>(restored.Tracks![0].Parts[0]);
        Assert.Equal(3.5, audio.StartTime, 6);   // 显式 [StartTime, EndTime] 回环
        Assert.Equal(5.5, audio.EndTime, 6);
        Assert.Equal("", restored.Tracks[0].Name);
        Assert.Equal(120, restored.Tempos![0].Bpm);
        Assert.Equal(3, restored.TimeSignatures![0].Numerator);
    }

    [Fact]
    public void NullSlots_StayNull()
    {
        var restored = RoundTrip(new DerivedResult());
        Assert.Empty(restored.Tracks);
        Assert.Empty(restored.Tempos);
        Assert.Empty(restored.TimeSignatures);
    }
}
