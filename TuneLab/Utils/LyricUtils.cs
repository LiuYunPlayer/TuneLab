using Pinyin;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Base.Utils;

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
        var pinyinResults = Pinyin.Pinyin.Instance.HanziToPinyin(lyrics, ManTone.Style.NORMAL, Error.Default, true, false);
        var results = new List<LyricResult>();
        foreach (var pinyinRes in pinyinResults)
        {
            results.Add(new LyricResult() { Lyric = pinyinRes.hanzi, Pronunciation = pinyinRes.pinyin/*, Candidates = pinyinRes.candidates*/ });
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
        return Pinyin.ChineseG2p.SplitString(lyric);
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
        var pinyins = Pinyin.Pinyin.Instance.HanziToPinyin(lyric, ManTone.Style.NORMAL, Error.Default, true, false);
        if (pinyins.IsEmpty())
            return string.Empty;

        var pinyin = pinyins[0];
        if (pinyin.error)
            return string.Empty;

        return pinyin.pinyin;
    }

    public static IReadOnlyCollection<string> GetPronunciations(string lyric)
    {
        if (lyric.Length == 1)
        {
            if (Pinyin.Pinyin.Instance.IsHanzi(lyric))
            {
                return Pinyin.Pinyin.Instance.GetDefaultPinyin(lyric, ManTone.Style.NORMAL, false, false);
            }
        }

        return [];
    }
}
