using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace DiffSinger;

/// <summary>
/// DiffSinger G2P dictionary data model (YAML-based).
/// Supports both legacy format (dictionary + symbols with vowel bool)
/// and the current DiffSinger format (entries + symbols with type string).
/// </summary>
public class DiffSingerG2pDictionaryData
{
    public struct Replacement
    {
        public string from;
        public string to;
    }

    // Current DiffSinger format
    public List<EntryV2>? entries;
    public List<SymbolV2>? symbols;

    // Legacy format
    public List<SymbolEntry>? legacy_symbols;
    public List<DictionaryEntry>? dictionary;
    public Replacement[]? replacements;

    public Dictionary<string, string> replacementsDict()
    {
        var dict = new Dictionary<string, string>();
        if (replacements != null)
        {
            foreach (var r in replacements)
            {
                dict[r.from] = r.to;
            }
        }
        return dict;
    }
}

/// <summary>Symbol entry in current DiffSinger format (type as string).</summary>
public class SymbolV2
{
    [YamlMember(Alias = "symbol")]
    public string Symbol { get; set; } = string.Empty;

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = string.Empty;

    public bool IsVowel => Type == "vowel";
}

/// <summary>Dictionary entry in current DiffSinger format (entries array).</summary>
public class EntryV2
{
    [YamlMember(Alias = "grapheme")]
    public string Grapheme { get; set; } = string.Empty;

    [YamlMember(Alias = "phonemes")]
    public List<string>? Phonemes { get; set; }
}

/// <summary>Legacy symbol entry (vowel as bool).</summary>
public class SymbolEntry
{
    public string symbol = string.Empty;
    public bool? vowel;
}

/// <summary>Legacy dictionary entry (phonemes as space-separated string).</summary>
public class DictionaryEntry
{
    public string grapheme = string.Empty;
    public string phonemes = string.Empty;
}

/// <summary>
/// Simple G2P (grapheme-to-phoneme) engine that queries a dictionary.
/// </summary>
public class G2pEngine
{
    private readonly Dictionary<string, string[]> _dict = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _symbols = new();
    private readonly HashSet<string> _vowels = new();
    private readonly Dictionary<string, string> _replacements = new();

    /// <summary>All known phoneme symbols.</summary>
    public IReadOnlySet<string> Symbols => _symbols;

    /// <summary>Vowel symbols.</summary>
    public IReadOnlySet<string> Vowels => _vowels;

    public bool IsValidSymbol(string symbol) => _symbols.Contains(symbol);
    public bool IsVowel(string symbol) => _vowels.Contains(symbol);

    /// <summary>
    /// Load from a YAML dictionary file.
    /// Supports both legacy format and current DiffSinger format.
    /// </summary>
    public static G2pEngine Load(string yamlPath)
    {
        var engine = new G2pEngine();

        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();
        var data = deserializer.Deserialize<DiffSingerG2pDictionaryData>(yaml);

        // Load symbols (current DiffSinger format: type: vowel/consonant)
        if (data.symbols != null)
        {
            foreach (var entry in data.symbols)
            {
                var sym = entry.Symbol?.Trim();
                if (!string.IsNullOrEmpty(sym))
                {
                    engine._symbols.Add(sym);
                    if (entry.IsVowel)
                        engine._vowels.Add(sym);
                }
            }
        }

        // Load symbols (legacy format: vowel: true/false)
        if (data.legacy_symbols != null)
        {
            foreach (var entry in data.legacy_symbols)
            {
                var sym = entry.symbol.Trim();
                if (!string.IsNullOrEmpty(sym))
                {
                    engine._symbols.Add(sym);
                    if (entry.vowel == true)
                        engine._vowels.Add(sym);
                }
            }
        }

        // SP and AP are always vowels
        engine._symbols.Add("SP");
        engine._symbols.Add("AP");
        engine._vowels.Add("SP");
        engine._vowels.Add("AP");

        // Load replacements
        if (data.replacements != null)
        {
            foreach (var r in data.replacements)
            {
                engine._replacements[r.from] = r.to;
            }
        }

        // Load dictionary entries (current format: entries array with phonemes list)
        if (data.entries != null)
        {
            foreach (var entry in data.entries)
            {
                var grapheme = entry.Grapheme?.Trim();
                if (string.IsNullOrEmpty(grapheme) || entry.Phonemes == null || entry.Phonemes.Count == 0)
                    continue;

                var phonemes = entry.Phonemes
                    .Select(p => p?.Trim() ?? string.Empty)
                    .Where(p => p.Length > 0)
                    .ToArray();

                if (phonemes.Length > 0)
                {
                    engine._dict[grapheme] = phonemes;
                }
            }
        }

        // Load dictionary entries (legacy format: dictionary array with space-separated phonemes)
        if (data.dictionary != null)
        {
            foreach (var entry in data.dictionary)
            {
                var grapheme = entry.grapheme.Trim();
                var phonemeStr = entry.phonemes.Trim();
                var phonemes = Regex.Split(phonemeStr, @"\s+")
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToArray();

                if (!string.IsNullOrEmpty(grapheme) && phonemes.Length > 0)
                {
                    engine._dict[grapheme] = phonemes;
                }
            }
        }

        return engine;
    }

    /// <summary>
    /// Query G2P for a lyric. Returns the phoneme sequence or null if not found.
    /// If not found in exact match, finds the closest entry by edit distance.
    /// </summary>
    public string[]? Query(string lyric)
    {
        if (string.IsNullOrEmpty(lyric))
            return null;

        // Exact match
        if (_dict.TryGetValue(lyric, out var result))
            return result;

        // Lowercase match
        if (_dict.TryGetValue(lyric.ToLowerInvariant(), out result))
            return result;

        // No match found - find closest by edit distance
        return FindClosest(lyric);
    }

    private string[]? FindClosest(string input)
    {
        if (_dict.Count == 0) return null;

        var lower = input.ToLowerInvariant();
        int bestDist = int.MaxValue;
        string? bestKey = null;

        foreach (var key in _dict.Keys)
        {
            int dist = LevenshteinDistance(lower, key.ToLowerInvariant());
            if (dist < bestDist)
            {
                bestDist = dist;
                bestKey = key;
            }
        }

        // If the best distance is reasonable (within half the input length)
        if (bestKey != null && bestDist <= Math.Max(1, lower.Length / 2))
        {
            return _dict[bestKey];
        }

        return null;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        int m = a.Length, n = b.Length;
        var dp = new int[m + 1, n + 1];

        for (int i = 0; i <= m; i++) dp[i, 0] = i;
        for (int j = 0; j <= n; j++) dp[0, j] = j;

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[m, n];
    }
}