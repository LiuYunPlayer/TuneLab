using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
}
