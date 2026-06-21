namespace DiffSinger;

/// <summary>
/// Defines a mapping from a DS variance parameter to a target engine parameter.
/// </summary>
public class VarianceMapping
{
    /// <summary>DS variance parameter name (e.g., "tension", "breathiness").</summary>
    public string SourceParam { get; set; } = string.Empty;

    /// <summary>Target engine parameter abbreviation (e.g., "TENC", "BRE").</summary>
    public string TargetParam { get; set; } = string.Empty;

    /// <summary>Display name for the mapping.</summary>
    public string DisplayName => $"{SourceParam} → {TargetParam}";
}

/// <summary>
/// Apply mode for the plugin.
/// </summary>
public enum ApplyMode
{
    SelectedNotes,
    WholeTrack
}

/// <summary>
/// Execution provider for ONNX inference.
/// </summary>
public enum ExecutionDevice
{
    CPU,
    DirectML
}

/// <summary>
/// Stores the configuration state for the DiffSinger for TuneLab plugin.
/// Mirrors all key settings from OpenUtau's DiffSinger preferences.
/// </summary>
public class PluginConfig
{
    // ================================================================
    //  Model Paths
    // ================================================================
    public string DurModelPath { get; set; } = string.Empty;       // dsdur/ subdirectory
    public string PitchModelPath { get; set; } = string.Empty;     // dspitch/ subdirectory
    public string VarianceModelPath { get; set; } = string.Empty;  // dsvariance/ subdirectory
    public string SingerRootPath { get; set; } = string.Empty;     // Singer root (acoustic model + vocoder)

    // ================================================================
    //  Language
    // ================================================================
    public string Language { get; set; } = "default";

    // ================================================================
    //  Execution Device (mirrors OpenUtau OnnxRunner)
    // ================================================================
    public ExecutionDevice Device { get; set; } = ExecutionDevice.CPU;
    public int GpuIndex { get; set; } = 0;  // DirectML GPU device index

    // ================================================================
    //  Rendering Steps (mirrors OpenUtau DiffSingerSteps*)
    // ================================================================
    public int DurSteps { get; set; } = 20;         // Duration model steps
    public int AcousticSteps { get; set; } = 20;    // Acoustic model steps (DiffSingerSteps)
    public int PitchSteps { get; set; } = 10;       // Pitch model steps (DiffSingerStepsPitch)
    public int VarianceSteps { get; set; } = 20;    // Variance model steps (DiffSingerStepsVariance)

    // ================================================================
    //  Diffusion Depth (mirrors OpenUtau DiffSingerDepth)
    // ================================================================
    public double DiffusionDepth { get; set; } = 1.0;

    // ================================================================
    //  Performance Sliders
    // ================================================================
    public float Expressiveness { get; set; } = 100f;
    public float BlendPercent { get; set; } = 100f;

    // ================================================================
    //  Advanced Toggles (mirrors OpenUtau DiffSinger* flags)
    // ================================================================
    public bool TensorCache { get; set; } = true;                            // DiffSingerTensorCache
    public bool VarianceLocalPitchPatch { get; set; } = true;               // DiffSingerVarianceLocalPitchPatch
    public bool UnvoicedF0Interpolate { get; set; } = true;                 // DiffSingerUnvoicedConsonantAcousticF0Interpolate
    public bool DiscardOnMismatch { get; set; } = true;

    // ================================================================
    //  Apply Mode
    // ================================================================
    public ApplyMode Mode { get; set; } = ApplyMode.SelectedNotes;

    // ================================================================
    //  Variance Mappings (DS param → target engine param)
    // ================================================================
    public List<VarianceMapping> VarianceMappings { get; set; } = new();

    /// <summary>Get the blend factor (0..1).</summary>
    public float BlendFactor => BlendPercent / 100f;
}