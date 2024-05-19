using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.International.Converters.PinYinConverter;
using TuneLab.Base.Structures;

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
        List<string> lyrics = [];
        if (string.IsNullOrEmpty(lyric))
            return lyrics;

        int flag = 0;
        bool lastIsLetter = char.IsAsciiLetter(lyric[0]);
        for (int i = 1; i < lyric.Length; i++)
        {
            bool isLetter = char.IsAsciiLetter(lyric[i]);
            if (isLetter && lastIsLetter)
                continue;

            lyrics.Add(lyric.Substring(flag, i - flag));
            flag = i;
        }
        lyrics.Add(lyric.Substring(flag, lyric.Length - flag));

        return lyrics;
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
            return pinyin.Substring(0, pinyin.Length - 1).ToLower();

        return pinyin.ToLower();
    }
}
