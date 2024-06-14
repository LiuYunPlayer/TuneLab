using IKg2p;
using System.Collections.Generic;
using System.Linq;

namespace TuneLab.Utils;

internal static class LyricUtils
{
    public static List<string> SplitLyrics(string lyrics)
    {
        List<string> result = new List<string>();
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

    public static IReadOnlyCollection<string> GetPronunciations(string lyric)
    {
        if (lyric.Length == 1)
        {
            var pinyin = ZhG2p.MandarinInstance.Convert(lyric, false, true)[0];
            if (!pinyin.error)
                return new List<string> { pinyin.syllable }.AsReadOnly();
        }

        return [];
    }

    static string ToPinyin(string pinyin)
    {
        if (string.IsNullOrEmpty(pinyin))
            return string.Empty;

        if (char.IsNumber(pinyin[^1]))
            return pinyin.Substring(0, pinyin.Length - 1).ToLower();

        return pinyin.ToLower();
    }
}
