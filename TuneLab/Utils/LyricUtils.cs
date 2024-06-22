using IKg2p;
using Microsoft.International.Converters.PinYinConverter;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Base.Structures;

namespace TuneLab.Utils;

internal static class LyricUtils
{
    public struct LyricResult
    {
        public string Lyric;
        public string Pronunciation;
        /*public IReadOnlyList<string> Candidates;*/
    }

    public static List<LyricResult> Split(string lyrics)
    {
        var g2pResults = ZhG2p.MandarinInstance.Convert(lyrics, false, true);
        var results = new List<LyricResult>();
        foreach (var g2pRes in g2pResults)
        {
            results.Add(new LyricResult() { Lyric = g2pRes.lyric, Pronunciation = g2pRes.syllable/*, Candidates = g2pRes.candidates*/ });
        }
        return results;
    }

    public static List<string> SplitLyrics(string lyrics)
    {
        var result = new List<string>();
        var splitedLyrics = SplitByInvailidChars(lyrics);
        foreach (var lyric in splitedLyrics)
        {
            result.AddRange(SplitToWords(lyric));
        }

        return result;
    }

    public static List<string> SplitToWords(string lyric)
    {
        return ZhG2p.SplitString(lyric);
    }

    public static IEnumerable<string> SplitByInvailidChars(string lyric)
    {
        return lyric.Split(['\n', ' ', '\t', '\r',
            '.', ',', '!', '?', ';', ':', '"', '(', ')', '[', ']', '{', '}', '/', '\'', '%', '$', '£', '€',
            '。', '，', '！', '？', '；', '：', '“', '”', '‘', '’', '（', '）', '【', '】', '『', '』', '—', '·'])
            .Where(s => !string.IsNullOrEmpty(s));
    }

    public static string GetPreferredPronunciation(string lyric)
    {
        var pinyin = ZhG2p.MandarinInstance.Convert(lyric, false, true)[0];
        if (!pinyin.error)
            return pinyin.syllable;

        return string.Empty;
    }

    public static IReadOnlyCollection<string> GetPronunciations(string lyric)
    {
        if (lyric.Length == 1)
        {
            var c = lyric[0];
            if (ChineseChar.IsValidChar(c))
            {
                var chineseChar = new ChineseChar(c);
                return chineseChar.Pinyins.Take(chineseChar.PinyinCount).Convert(ToPinyin).ToHashSet();
            }
        }

        return [];
    }

    static string ToPinyin(string pinyin)
    {
        if (string.IsNullOrEmpty(pinyin))
            return string.Empty;

        if (char.IsNumber(pinyin[^1]))
            return pinyin[..^1].ToLower();

        return pinyin.ToLower();
    }
}
