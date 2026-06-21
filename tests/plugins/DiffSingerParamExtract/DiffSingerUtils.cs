using System.Text.RegularExpressions;

namespace DiffSinger;

/// <summary>
/// Utility functions ported from OpenUtau's DiffSinger implementation.
/// </summary>
public static class DiffSingerUtils
{
    /// <summary>Head padding frames (consonant anticipation).</summary>
    public const int HeadFrames = 16;
    /// <summary>Tail padding frames.</summary>
    public const int TailFrames = 16;

    /// <summary>
    /// Load phoneme-to-token mapping from a phoneme list file.
    /// </summary>
    public static Dictionary<string, int> LoadPhonemes(string phonemesPath)
    {
        var lines = File.ReadAllLines(phonemesPath);
        var dict = new Dictionary<string, int>();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!string.IsNullOrEmpty(line) && !line.StartsWith("#"))
            {
                dict[line] = i;
            }
        }
        return dict;
    }

    /// <summary>
    /// Load language IDs mapping from a YAML file.
    /// </summary>
    public static Dictionary<string, int> LoadLanguageIds(string langIdPath)
    {
        var lines = File.ReadAllLines(langIdPath);
        var dict = new Dictionary<string, int>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;
            var parts = trimmed.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int id))
            {
                dict[parts[0].Trim()] = id;
            }
        }
        return dict;
    }

    /// <summary>
    /// Extract language code from a phoneme symbol (e.g., "en/AA" -> "en").
    /// </summary>
    public static string PhonemeLanguage(string phoneme)
    {
        int slash = phoneme.IndexOf('/');
        return slash >= 0 ? phoneme[..slash] : string.Empty;
    }

    /// <summary>
    /// Sample a float curve at regular intervals and convert using a value function.
    /// </summary>
    public static double[] SampleCurve(
        IReadOnlyList<float> curve,
        double startValue,
        double frameMs,
        int totalFrames,
        int headFrames,
        int tailFrames,
        Func<double, double> valueFunc)
    {
        var result = new double[totalFrames];
        for (int i = 0; i < totalFrames; i++)
        {
            double t = (i - headFrames) * frameMs / 1000.0;
            int idx = (int)(t * 100); // Assuming 100Hz control rate
            idx = Math.Clamp(idx, 0, Math.Max(0, curve.Count - 1));
            result[i] = valueFunc(curve.Count > 0 ? curve[idx] : startValue);
        }
        return result;
    }

    /// <summary>
    /// Calculate padded phoneme durations in frames.
    /// </summary>
    public static int[] PaddedPhoneDurations(
        IReadOnlyList<double> phonemeEndsMs,
        IReadOnlyList<double> noteDurationsMs,
        double frameMs,
        int headFrames,
        int tailFrames)
    {
        if (phonemeEndsMs.Count == 0)
            return Array.Empty<int>();

        double firstPhonemeStart = 0;
        double lastPhonemeEnd = phonemeEndsMs[^1];

        var durations = new List<int>();
        // Head padding
        durations.Add(headFrames);

        // Phoneme durations in frames
        double prevEnd = firstPhonemeStart;
        for (int i = 0; i < phonemeEndsMs.Count; i++)
        {
            double durMs = phonemeEndsMs[i] - prevEnd;
            int frames = Math.Max(1, (int)Math.Round(durMs / frameMs));
            durations.Add(frames);
            prevEnd = phonemeEndsMs[i];
        }

        // Tail padding
        durations.Add(tailFrames);

        return durations.ToArray();
    }

    /// <summary>
    /// Convert tone (semitones relative to A4=69) to frequency in Hz.
    /// </summary>
    public static double ToneToFreq(double tone)
    {
        return 440.0 * Math.Pow(2.0, (tone - 69.0) / 12.0);
    }

    /// <summary>
    /// Validate tensor shape.
    /// </summary>
    public static bool ValidateShape(ReadOnlySpan<int> shape, ReadOnlySpan<int> expected)
    {
        if (shape.Length != expected.Length) return false;
        for (int i = 0; i < shape.Length; i++)
        {
            if (expected[i] >= 0 && shape[i] != expected[i]) return false;
        }
        return true;
    }

    public static string ShapeString(ReadOnlySpan<int> shape)
    {
        return "[" + string.Join(", ", shape.ToArray()) + "]";
    }

    /// <summary>
    /// Compute cumulative sum of a sequence.
    /// </summary>
    public static IEnumerable<double> CumulativeSum(IEnumerable<double> sequence, double start = 0)
    {
        double sum = start;
        foreach (var item in sequence)
        {
            sum += item;
            yield return sum;
        }
    }

    public static IEnumerable<int> CumulativeSum(IEnumerable<int> sequence, int start = 0)
    {
        int sum = start;
        foreach (var item in sequence)
        {
            sum += item;
            yield return sum;
        }
    }

    /// <summary>
    /// Stretch a duration sequence to fit within an end position.
    /// </summary>
    public static List<double> Stretch(IList<double> source, double ratio, double endPos)
    {
        double startPos = endPos - source.Sum() * ratio;
        var result = CumulativeSum(source.Select(x => x * ratio).Prepend(0), startPos).ToList();
        result.RemoveAt(result.Count - 1);
        return result;
    }

    /// <summary>
    /// Resample a padded curve from one frame rate/offset to another.
    /// </summary>
    public static float[] ResamplePaddedCurve(
        float[] sourceCurve,
        int targetFrames,
        int sourceHeadFrames,
        int sourceTailFrames,
        int targetHeadFrames,
        int targetTailFrames,
        double sourceFrameMs,
        double targetFrameMs)
    {
        if (sourceCurve.Length == 0) return Array.Empty<float>();

        int sourceContentFrames = sourceCurve.Length - sourceHeadFrames - sourceTailFrames;
        if (sourceContentFrames <= 0) return Enumerable.Repeat(0f, targetFrames).ToArray();

        var result = new float[targetFrames];
        double sourceContentDurationMs = sourceContentFrames * sourceFrameMs;

        for (int i = 0; i < targetFrames; i++)
        {
            double tMs = (i - targetHeadFrames) * targetFrameMs;
            double ratio = tMs / sourceContentDurationMs;
            double srcIdx = sourceHeadFrames + ratio * sourceContentFrames;
            int srcIdx0 = Math.Clamp((int)srcIdx, 0, sourceCurve.Length - 2);
            int srcIdx1 = srcIdx0 + 1;
            double frac = srcIdx - srcIdx0;
            result[i] = (float)(sourceCurve[srcIdx0] * (1 - frac) + sourceCurve[srcIdx1] * frac);
        }

        return result;
    }
}