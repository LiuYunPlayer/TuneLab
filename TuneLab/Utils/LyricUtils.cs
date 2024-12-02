using Pinyin;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
        var lyricList = SplitToWords(lyrics);
        // 设置Error.Default、转换失败时，发音元素会返回原文本，error为true；Size与输入列表相同
        var pinyinResults = Pinyin.Pinyin.Instance.HanziToPinyin(lyricList, ManTone.Style.NORMAL, Pinyin.Error.Default, true, false);
        var kanaResults = Kana.Kana.KanaToRomaji(lyricList);

        var results = new List<LyricResult>();

        for (int i = 0; i < lyricList.Count; i++)
        {
            if (pinyinResults[i].error == false)
                results.Add(new LyricResult() { Lyric = pinyinResults[i].hanzi, Pronunciation = pinyinResults[i].pinyin/*, Candidates = pinyinRes.candidates*/ });
            else if (kanaResults[i].Error == false)
                results.Add(new LyricResult() { Lyric = kanaResults[i].Kana, Pronunciation = kanaResults[i].Romaji });
            else
                results.Add(new LyricResult() { Lyric = lyricList[i], Pronunciation = string.Empty });

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
        string pattern = "(?![ー\u309c])([a-zA-Z]+|[+-]|[0-9]|[\\u4e00-\\u9fa5]|[\\u3040-\\u309F\\u30A0-\\u30FF][ャュョゃゅょァィゥェォぁぃぅぇぉ]?)";
        return (from Match m in Regex.Matches(lyric, pattern)
                select m.Value).ToList();
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
        // 校验字符后可直接返回发音
        if (Pinyin.Pinyin.Instance.IsHanzi(lyric))
            return Pinyin.Pinyin.Instance.GetDefaultPinyin(lyric, ManTone.Style.NORMAL, false, false)[0];
        else if (Kana.Kana.IsKana(lyric))
            return Kana.Kana.KanaToRomaji(lyric)[0].Romaji;

        return string.Empty;
    }

    public static IReadOnlyCollection<string> GetPronunciations(string lyric)
    {
        // 校验字符后可直接返回发音
        if (Pinyin.Pinyin.Instance.IsHanzi(lyric))
            return Pinyin.Pinyin.Instance.GetDefaultPinyin(lyric, ManTone.Style.NORMAL, false, false);
        else if (Kana.Kana.IsKana(lyric))
            return Kana.Kana.KanaToRomaji(lyric).ToStrList();

        return [];
    }
}
