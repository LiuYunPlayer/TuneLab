using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.Extensions.Derivers;

// deriver 结果（贵、模型跑出来的 DerivedResult）的内容寻址缓存——宿主内部模块、非插件面、非工程、非 Settings
//（照 RecentSoundSourceManager 的宿主记忆范式，但为目录 + 内容寻址）。per-user，同音频跨工程命中。
//
// 键 = run-inputs 身份：hash(源音频 PCM)（FrozenAudioDerivationInput 顺带算）+ engineId + 插件 manifest version
// + 参数 hash。喂的是整段源音频（位置无关），故移动 / 裁剪 part 都不改键、必命中；裁剪是 apply-side、不进键。
// 模型版本位取 manifest version：发布即变，令旧模型结果不被误服用（代价 over-invalidate，但缓存可弃）。
//
// 形态：每个键一个 <key>.json（符号产物体积小）。淘汰：有界 + 按访问时间 LRU（缓存可弃，淘汰不损正确性）。
// 线程：查表 / 写表在数据线程；文件 IO 短暂、无并发写同键。
internal static class AudioDerivationCacheManager
{
    const int MaxEntries = 200;

    // 由 run-inputs 身份算缓存键。paramsJson 用与工程/扩展设置同一套 PropertyObject→JSON 转换，稳定可复现。
    public static string ComputeKey(string contentHash, string engineId, string manifestVersion, PropertyObject properties)
    {
        var paramsJson = PropertyJsonUtils.ToJson(properties).ToString(Formatting.None);
        var material = string.Join("|", contentHash, engineId, manifestVersion, paramsJson);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // 命中即返回反序列化结果（并 touch 访问时间供 LRU）；未命中 / 读坏返回 false（读坏当未命中、下次重算覆盖）。
    public static bool TryGet(string key, out DerivedResult result)
    {
        result = null!;
        try
        {
            var path = FilePath(key);
            if (!File.Exists(path))
                return false;
            result = ReadResult(JObject.Parse(File.ReadAllText(path)));
            try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch { /* touch 失败不影响命中 */ }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(string.Format("Derivation cache read failed for {0}: {1}", key, ex.Message));
            return false;
        }
    }

    // 写入结果（写一次不可变、同键同内容）。失败仅记日志（缓存可弃、不影响落地）。
    public static void Put(string key, DerivedResult result)
    {
        try
        {
            PathManager.MakeSureExist(PathManager.DerivationCacheFolder);
            File.WriteAllText(FilePath(key), WriteResult(result).ToString(Formatting.None));
            TrimToCapacity();
        }
        catch (Exception ex)
        {
            Log.Warning(string.Format("Derivation cache write failed for {0}: {1}", key, ex.Message));
        }
    }

    static string FilePath(string key) => Path.Combine(PathManager.DerivationCacheFolder, key + ".json");

    static void TrimToCapacity()
    {
        var dir = new DirectoryInfo(PathManager.DerivationCacheFolder);
        if (!dir.Exists)
            return;
        var files = dir.GetFiles("*.json");
        if (files.Length <= MaxEntries)
            return;
        foreach (var file in files.OrderBy(f => f.LastAccessTimeUtc).Take(files.Length - MaxEntries))
        {
            try { file.Delete(); } catch { /* 淘汰失败无害，下次再修剪 */ }
        }
    }

    // ── 序列化：DerivedResult ↔ JSON（手写 JToken，同 PropertyJsonUtils/DataInfoJsonUtils 风格）──
    // null 列表 = 「不产」→ 省键；读回缺键即 null，忠实往返。Point → [x, y]。part 多态用 "kind" 判别位。
    // part 裁剪 ±∞ 省键（JSON 无 Infinity；读回缺即 ±∞）。phoneme 与 DataInfo 同构、随产物往返。

    // 空集合 omit-if-empty（省键），读回默认空——JSON 紧凑 + API 非空两得。
    internal static JObject WriteResult(DerivedResult r)
    {
        var json = new JObject();
        if (r.Tracks.Count > 0)
            json["tracks"] = new JArray(r.Tracks.Select(WriteTrack));
        if (r.Tempos.Count > 0)
            json["tempos"] = new JArray(r.Tempos.Select(t => new JObject { ["time"] = t.Time, ["bpm"] = t.Bpm }));
        if (r.TimeSignatures.Count > 0)
            json["timeSignatures"] = new JArray(r.TimeSignatures.Select(t => new JObject { ["time"] = t.Time, ["numerator"] = t.Numerator, ["denominator"] = t.Denominator }));
        return json;
    }

    static JObject WriteTrack(DerivedTrack t)
    {
        var json = new JObject { ["parts"] = new JArray(t.Parts.Select(WritePart)) };
        if (t.Name.Length > 0)
            json["name"] = t.Name;
        return json;
    }

    static JObject WritePart(DerivedPart part)
    {
        var json = new JObject();
        if (part.StartTime != 0)
            json["start"] = part.StartTime;
        if (!double.IsPositiveInfinity(part.EndTime))
            json["end"] = part.EndTime;
        switch (part)
        {
            case DerivedMidiPart midi:
                json["kind"] = "midi";
                if (midi.Notes.Count > 0)
                    json["notes"] = new JArray(midi.Notes.Select(WriteNote));
                if (midi.Pitch.Segments.Count > 0)
                    json["pitch"] = WriteSegments(midi.Pitch.Segments);
                break;
            case DerivedAudioPart:
                json["kind"] = "audio";
                break;
        }
        return json;
    }

    static JObject WriteNote(DerivedNote n)
    {
        var json = new JObject { ["start"] = n.StartTime, ["end"] = n.EndTime, ["pitch"] = n.Pitch };
        if (n.Lyric.Length > 0)
            json["lyric"] = n.Lyric;
        if (n.BodyOffset != 0)
            json["bodyOffset"] = n.BodyOffset;
        if (n.LeadingPhonemes.Count > 0)
            json["leading"] = new JArray(n.LeadingPhonemes.Select(WritePhoneme));
        if (n.BodyPhonemes.Count > 0)
            json["body"] = new JArray(n.BodyPhonemes.Select(WritePhoneme));
        return json;
    }

    static JObject WritePhoneme(DerivedPhoneme p)
        => new() { ["symbol"] = p.Symbol, ["duration"] = p.Duration, ["stretch"] = p.StretchWeight };

    static JArray WriteSegments(IReadOnlyList<IReadOnlyList<Point>> segments) => new(segments.Select(WritePoints));
    static JArray WritePoints(IReadOnlyList<Point> points) => new(points.Select(pt => new JArray(pt.X, pt.Y)));

    internal static DerivedResult ReadResult(JObject json) => new()
    {
        Tracks = json["tracks"] is JArray tracks ? tracks.Select(t => ReadTrack((JObject)t)).ToArray() : [],
        Tempos = json["tempos"] is JArray tempos ? tempos.Select(t => new DerivedTempo { Time = (double)t["time"]!, Bpm = (double)t["bpm"]! }).ToArray() : [],
        TimeSignatures = json["timeSignatures"] is JArray ts ? ts.Select(t => new DerivedTimeSignature { Time = (double)t["time"]!, Numerator = (int)t["numerator"]!, Denominator = (int)t["denominator"]! }).ToArray() : [],
    };

    static DerivedTrack ReadTrack(JObject json) => new()
    {
        Name = (string?)json["name"] ?? string.Empty,
        Parts = json["parts"] is JArray parts ? parts.Select(p => ReadPart((JObject)p)).ToArray() : [],
    };

    static DerivedPart ReadPart(JObject json)
    {
        double start = json["start"] is { } s ? (double)s : 0;
        double end = json["end"] is { } e ? (double)e : double.PositiveInfinity;
        if ((string?)json["kind"] == "audio")
            return new DerivedAudioPart { StartTime = start, EndTime = end };

        return new DerivedMidiPart
        {
            StartTime = start,
            EndTime = end,
            Notes = json["notes"] is JArray notes ? notes.Select(n => ReadNote((JObject)n)).ToArray() : [],
            Pitch = new DerivedPitch { Segments = json["pitch"] is JArray pitch ? ReadSegments(pitch) : [] },
        };
    }

    static DerivedNote ReadNote(JObject json) => new()
    {
        StartTime = (double)json["start"]!,
        EndTime = (double)json["end"]!,
        Pitch = (int)json["pitch"]!,
        Lyric = (string?)json["lyric"] ?? string.Empty,
        BodyOffset = json["bodyOffset"] is { } b ? (double)b : 0,
        LeadingPhonemes = json["leading"] is JArray l ? l.Select(p => ReadPhoneme((JObject)p)).ToArray() : [],
        BodyPhonemes = json["body"] is JArray bd ? bd.Select(p => ReadPhoneme((JObject)p)).ToArray() : [],
    };

    static DerivedPhoneme ReadPhoneme(JObject json) => new()
    {
        Symbol = (string?)json["symbol"] ?? string.Empty,
        Duration = (double)json["duration"]!,
        StretchWeight = json["stretch"] is { } w ? (double)w : 0,
    };

    static IReadOnlyList<IReadOnlyList<Point>> ReadSegments(JArray json) => json.Select(seg => ReadPoints((JArray)seg)).ToArray();
    static IReadOnlyList<Point> ReadPoints(JArray json) => json.Select(pt => new Point((double)pt[0]!, (double)pt[1]!)).ToArray();
}
