using YamlDotNet.Serialization;

namespace DiffSinger;

/// <summary>
/// dsconfig.yaml model for DiffSinger model configuration.
/// </summary>
[Serializable]
public class DsConfig
{
    public string phonemes = "phonemes.txt";
    public string? languages;
    public string? acoustic;
    public string? vocoder;
    public List<string>? speakers;
    public int hiddenSize = 256;
    public bool useKeyShiftEmbed;
    public bool useSpeedEmbed;
    public bool useEnergyEmbed;
    public bool useBreathinessEmbed;
    public bool useVoicingEmbed;
    public bool useTensionEmbed;
    public AugmentationArgs? augmentationArgs;
    public bool useContinuousAcceleration;
    public bool use_lang_id;
    [YamlMember(Alias = "use_shallow_diffusion")] public bool? _useShallowDiffusion;
    [YamlMember(Alias = "use_variable_depth")] public bool? _useVariableDepth;

    [YamlIgnore]
    public bool useVariableDepth
    {
        get
        {
            if (_useVariableDepth.HasValue) return _useVariableDepth.Value;
            if (_useShallowDiffusion.HasValue) return _useShallowDiffusion.Value;
            return false;
        }
    }

    [YamlMember(Alias = "max_depth")] public double _maxDepth;
    [YamlIgnore] public double maxDepth => useContinuousAcceleration ? _maxDepth : _maxDepth / 1000.0;

    public string? dur;
    public string? linguistic;
    public string? pitch;
    public string? variance;
    public bool predict_dur = true;
    public bool predict_energy = true;
    public bool predict_breathiness = true;
    public bool predict_voicing;
    public bool predict_tension;
    public bool use_expr;
    public bool use_note_rest;
    public int sample_rate = 44100;
    public int hop_size = 512;
    public int win_size = 2048;
    public int fft_size = 2048;
    public int num_mel_bins = 128;
    public double mel_fmin = 40;
    public double mel_fmax = 16000;
    public string mel_base = "10";
    public string mel_scale = "slaney";
    public string unvoiced_phonemes = "dsunvoiced.yaml";

    public float frameMs() => 1000f * hop_size / sample_rate;
}

[Serializable]
public class RandomPitchShifting
{
    public float[]? range;
}

[Serializable]
public class AugmentationArgs
{
    public RandomPitchShifting? randomPitchShifting;
}

/// <summary>
/// Known DS variance parameter types.
/// </summary>
public static class VarianceParamTypes
{
    public const string Energy = "energy";
    public const string Breathiness = "breathiness";
    public const string Voicing = "voicing";
    public const string Tension = "tension";

    public static readonly IReadOnlyList<string> All = new[] { Energy, Breathiness, Voicing, Tension };

    /// <summary>
    /// Returns which variance params are predicted by the model according to dsconfig.
    /// </summary>
    public static string[] GetPredictedParams(DsConfig config)
    {
        var result = new List<string>();
        if (config.predict_energy) result.Add(Energy);
        if (config.predict_breathiness) result.Add(Breathiness);
        if (config.predict_voicing) result.Add(Voicing);
        if (config.predict_tension) result.Add(Tension);
        return result.ToArray();
    }
}