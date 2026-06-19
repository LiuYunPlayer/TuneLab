using TuneLab.Foundation;
using TuneLab.SDK;

namespace DiffSinger;

/// <summary>
/// Internal data produced by one Piece (segment) of synthesis.
/// </summary>
internal class SynthesizedParams
{
    public float[]? PitchCurve { get; init; }
    public double[] FrameTimes { get; init; } = Array.Empty<double>();
    public List<SynthesizedPhoneme> Phonemes { get; init; } = new();
    public Dictionary<string, List<Point>> ParameterCurves { get; init; } = new();
    public Dictionary<string, List<Point>> MappedParameterCurves { get; init; } = new();
}

/// <summary>
/// A segment (piece) of the synthesis timeline that can be independently synthesized.
/// </summary>
internal class Piece
{
    public List<ILiveNote> Notes { get; set; } = new();
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public bool Dirty { get; set; } = true;
    public bool Synthesizing { get; set; }
    public bool Failed { get; set; }
    public string? Error { get; set; }
    public SynthesizedParams? SynthesizedParams { get; set; }
}

/// <summary>
/// DiffSinger voice engine session.
///
/// Features:
/// 1. Loads dsdur, dspitch, dsvariance model directories
/// 2. G2P conversion of lyrics via DiffSinger dictionary (language-specific)
/// 3. Runs linguistic + duration model for phoneme timing
/// 4. Runs pitch model for F0 curve
/// 5. Runs variance model for energy/breathiness/voicing/tension curves
/// 6. Optional acoustic model + vocoder for full audio synthesis
/// 7. User-configurable variance parameter → target engine parameter mapping
/// 8. Expressiveness, blend, steps, and device controls
/// 9. Phoneme mismatch handling (discard or error)
/// 10. Smart phoneme alignment: merges multiple DS vowels/consonants when target has only 1
/// 11. Auto-scan voicebanks from configured singers directories
/// 12. Model path persistence to user config
/// </summary>
public sealed class DiffSingerSession : ISynthesisSession
{
    public string DefaultLyric => "la";

    // Model session
    private readonly DsModelSession _dsSession = new();
    private readonly PluginConfig _config = new();

    // State
    private readonly List<Piece> _pieces = new();
    private bool _needResegment = true;

    // Context & subscriptions
    private readonly ISynthesisContext _context;
    private readonly IDisposable _notesSubscription;
    private readonly Dictionary<ILiveNote, Action> _noteHandlers = new();

    // Config persistence
    private static readonly string ConfigFilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TuneLab", "DiffSinger", "config.json");

    // Track all available context automations for target parameter selection
    private IReadOnlyList<string> _targetAutomationKeys = Array.Empty<string>();

    public DiffSingerSession(ISynthesisContext context)
    {
        _context = context;

        LoadConfig();

        _notesSubscription = TuneLab.Foundation.NotifiableExtensions.WhenAny(
            context.Notes, SubscribeNote, UnsubscribeNote);
        context.Notes.ItemAdded += OnNotesStructureChanged;
        context.Notes.ItemRemoved += OnNotesStructureChanged;
        context.PartProperties.Modified += MarkAllDirty;
        context.Committed += OnCommitted;
        context.Pitch.RangeModified += OnRangeModified;
        context.PitchDeviation.RangeModified += OnRangeModified;

        _needResegment = true;
    }

    /// <summary>
    /// Auto-configure model paths from a detected singer.
    /// Called by the engine when user selects a scanned voicebank.
    /// </summary>
    public void AutoConfigureFromSinger(SingerDetectionInfo singer)
    {
        _config.SingerRootPath = singer.RootPath;
        _config.DurModelPath = singer.DurPath ?? string.Empty;
        _config.PitchModelPath = singer.PitchPath ?? string.Empty;
        _config.VarianceModelPath = singer.VariancePath ?? string.Empty;
        if (singer.Languages.Count > 0 && _config.Language == "default")
            _config.Language = singer.Languages[0];
        SaveConfig();
    }

    // ================================================================
    //  Config Persistence
    // ================================================================

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var loaded = System.Text.Json.JsonSerializer.Deserialize<PluginConfig>(json);
                if (loaded != null)
                {
                    _config.DurModelPath = loaded.DurModelPath;
                    _config.PitchModelPath = loaded.PitchModelPath;
                    _config.VarianceModelPath = loaded.VarianceModelPath;
                    _config.SingerRootPath = loaded.SingerRootPath;
                    _config.Language = loaded.Language;
                    _config.Device = loaded.Device;
                    _config.GpuIndex = loaded.GpuIndex;
                    _config.DurSteps = loaded.DurSteps;
                    _config.AcousticSteps = loaded.AcousticSteps;
                    _config.PitchSteps = loaded.PitchSteps;
                    _config.VarianceSteps = loaded.VarianceSteps;
                    _config.DiffusionDepth = loaded.DiffusionDepth;
                    _config.Expressiveness = loaded.Expressiveness;
                    _config.BlendPercent = loaded.BlendPercent;
                    _config.TensorCache = loaded.TensorCache;
                    _config.VarianceLocalPitchPatch = loaded.VarianceLocalPitchPatch;
                    _config.UnvoicedF0Interpolate = loaded.UnvoicedF0Interpolate;
                    _config.DiscardOnMismatch = loaded.DiscardOnMismatch;
                    _config.Mode = loaded.Mode;
                    _config.VarianceMappings = loaded.VarianceMappings ?? new();
                }
            }
        }
        catch { /* use defaults */ }
    }

    private void SaveConfig()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = System.Text.Json.JsonSerializer.Serialize(_config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFilePath, json);
        }
        catch { /* silent fail */ }
    }

    // ================================================================
    //  Part / Note Property Configs (UI)
    // ================================================================

    public ObjectConfig GetPartPropertyConfig(IPartPropertyContext context)
    {
        var properties = new OrderedMap<string, IControllerConfig>();

        // ================================================================
        //  Section: Singers Directory (global scan path)
        // ================================================================
        properties.Add("__singers_header", new TextBoxConfig
        {
            DefaultValue = "── Singer Discovery ──",
            DisplayText = "Singer Discovery"
        });

        string currentSingersDir = DiffSingerEngine.LoadSingersDirectory();
        properties.Add("singers_directory", new TextBoxConfig
        {
            DefaultValue = currentSingersDir,
            DisplayText = "Singers Directory (restart to scan)"
        });

        // ================================================================
        //  Section: Model Paths (persisted between sessions)
        // ================================================================
        properties.Add("__model_paths_header", new TextBoxConfig
        {
            DefaultValue = "── Model Paths ──",
            DisplayText = "DiffSinger Model Directories"
        });

        properties.Add("dur_model_path", new TextBoxConfig
        {
            DefaultValue = _config.DurModelPath,
            DisplayText = "dsdur Model Directory"
        });
        properties.Add("pitch_model_path", new TextBoxConfig
        {
            DefaultValue = _config.PitchModelPath,
            DisplayText = "dspitch Model Directory"
        });
        properties.Add("variance_model_path", new TextBoxConfig
        {
            DefaultValue = _config.VarianceModelPath,
            DisplayText = "dsvariance Model Directory"
        });
        properties.Add("singer_root_path", new TextBoxConfig
        {
            DefaultValue = _config.SingerRootPath,
            DisplayText = "Singer Root (acoustic+vocoder)"
        });

        // ================================================================
        //  Section: Language Selection
        // ================================================================
        var languages = _dsSession.IsDurLoaded ? _dsSession.GetAvailableLanguages() : Array.Empty<string>();
        if (languages.Length == 0) languages = new[] { "default" };
        properties.Add("language", new ComboBoxConfig
        {
            DefaultOption = new ComboBoxOption(_config.Language),
            Options = languages.Select(l => new ComboBoxOption(l)).ToList(),
            DisplayText = "Language (for G2P)"
        });

        // ================================================================
        //  Section: Render Settings
        // ================================================================
        properties.Add("__render_header", new TextBoxConfig
        {
            DefaultValue = "── Render Settings ──",
            DisplayText = "Render Settings"
        });

        // --- Device Selection ---
        properties.Add("exec_device", new ComboBoxConfig
        {
            DefaultOption = new ComboBoxOption(_config.Device == ExecutionDevice.DirectML ? "DirectML" : "CPU"),
            Options = new List<ComboBoxOption>
            {
                new("CPU"),
                new("DirectML")
            },
            DisplayText = "Execution Device"
        });

        // --- GPU Index (for DirectML multi-GPU) ---
        properties.Add("gpu_index", new SliderConfig
        {
            DefaultValue = _config.GpuIndex,
            MinValue = 0,
            MaxValue = 8,
            DisplayText = "GPU Device Index"
        });

        // ================================================================
        //  Section: Sampling Steps (mirrors OpenUtau DiffSingerSteps*)
        // ================================================================
        properties.Add("__steps_header", new TextBoxConfig
        {
            DefaultValue = "── Sampling Steps ──",
            DisplayText = "Sampling Steps"
        });

        // --- Dur Steps ---
        properties.Add("dur_steps", new SliderConfig
        {
            DefaultValue = _config.DurSteps,
            MinValue = 1,
            MaxValue = 100,
            DisplayText = "Dur Steps"
        });

        // --- Acoustic Steps (DiffSingerSteps) ---
        properties.Add("acoustic_steps", new SliderConfig
        {
            DefaultValue = _config.AcousticSteps,
            MinValue = 1,
            MaxValue = 100,
            DisplayText = "Acoustic Steps"
        });

        // --- Pitch Steps (DiffSingerStepsPitch) ---
        properties.Add("pitch_steps", new SliderConfig
        {
            DefaultValue = _config.PitchSteps,
            MinValue = 1,
            MaxValue = 100,
            DisplayText = "Pitch Steps"
        });

        // --- Variance Steps (DiffSingerStepsVariance) ---
        properties.Add("variance_steps", new SliderConfig
        {
            DefaultValue = _config.VarianceSteps,
            MinValue = 1,
            MaxValue = 100,
            DisplayText = "Variance Steps"
        });

        // --- Diffusion Depth (DiffSingerDepth) ---
        properties.Add("diffusion_depth", new SliderConfig
        {
            DefaultValue = _config.DiffusionDepth,
            MinValue = 0.01,
            MaxValue = 1.0,
            DisplayText = "Diffusion Depth"
        });

        // ================================================================
        //  Section: Performance Sliders
        // ================================================================
        properties.Add("__perf_header", new TextBoxConfig
        {
            DefaultValue = "── Performance ──",
            DisplayText = "Performance"
        });

        // --- Expressiveness ---
        properties.Add("expressiveness", new SliderConfig
        {
            DefaultValue = _config.Expressiveness,
            MinValue = 0,
            MaxValue = 200,
            DisplayText = "Expressiveness (%)"
        });

        // --- Blend ---
        properties.Add("blend", new SliderConfig
        {
            DefaultValue = _config.BlendPercent,
            MinValue = 0,
            MaxValue = 100,
            DisplayText = "Blend (%)"
        });

        // ================================================================
        //  Section: Advanced (mirrors OpenUtau DiffSinger flags)
        // ================================================================
        properties.Add("__advanced_header", new TextBoxConfig
        {
            DefaultValue = "── Advanced ──",
            DisplayText = "Advanced"
        });

        // --- Tensor Cache ---
        properties.Add("tensor_cache", new CheckBoxConfig
        {
            DefaultValue = _config.TensorCache,
            DisplayText = "Tensor Cache"
        });

        // --- Variance Local Pitch Patch ---
        properties.Add("variance_patch", new CheckBoxConfig
        {
            DefaultValue = _config.VarianceLocalPitchPatch,
            DisplayText = "Variance Local Pitch Patch"
        });

        // --- Unvoiced F0 Interpolate ---
        properties.Add("unvoiced_f0", new CheckBoxConfig
        {
            DefaultValue = _config.UnvoicedF0Interpolate,
            DisplayText = "Unvoiced F0 Interpolate"
        });

        // --- Phoneme Mismatch ---
        properties.Add("discard_mismatch", new CheckBoxConfig
        {
            DefaultValue = _config.DiscardOnMismatch,
            DisplayText = "Discard on phoneme mismatch"
        });

        // --- Apply Mode ---
        properties.Add("apply_mode", new ComboBoxConfig
        {
            DefaultOption = new ComboBoxOption(_config.Mode == ApplyMode.SelectedNotes ? "Selected Notes" : "Whole Track"),
            Options = new[] { new ComboBoxOption("Selected Notes"), new ComboBoxOption("Whole Track") },
            DisplayText = "Apply Mode"
        });

        // ================================================================
        //  Section: Variance Parameter Mappings
        // ================================================================
        var availableVarianceParams = _dsSession.IsVarianceLoaded
            ? _dsSession.AvailableVarianceParams.ToList()
            : VarianceParamTypes.All.ToList();

        RefreshTargetAutomationKeys();

        properties.Add("__variance_header", new TextBoxConfig
        {
            DefaultValue = "── Variance Mappings ──",
            DisplayText = "Variance Parameter Mappings"
        });

        for (int i = 0; i < 6; i++)
        {
            string idx = i.ToString();
            var existingMapping = i < _config.VarianceMappings.Count ? _config.VarianceMappings[i] : null;

            string sourceDefault = existingMapping?.SourceParam ?? "";
            string targetDefault = existingMapping?.TargetParam ?? "";

            var sourceOptions = new List<ComboBoxOption> { new("") };
            sourceOptions.AddRange(availableVarianceParams.Select(p => new ComboBoxOption(p)));

            var targetOptions = new List<ComboBoxOption> { new("") };
            targetOptions.AddRange(_targetAutomationKeys.Select(k => new ComboBoxOption(k)));

            properties.Add($"variance_map_{idx}_source", new ComboBoxConfig
            {
                DefaultOption = new ComboBoxOption(sourceDefault),
                Options = sourceOptions,
                DisplayText = $"Map {i + 1}: DS Param (source)"
            });

            properties.Add($"variance_map_{idx}_target", new ComboBoxConfig
            {
                DefaultOption = new ComboBoxOption(targetDefault),
                Options = targetOptions,
                DisplayText = $"Map {i + 1}: → Target Param"
            });
        }

        return new ObjectConfig { Properties = properties };
    }

    public ObjectConfig GetNotePropertyConfig(INotePropertyContext context)
    {
        return new ObjectConfig { Properties = new OrderedMap<string, IControllerConfig>() };
    }

    // ================================================================
    //  Automation Configs
    // ================================================================

    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IPartPropertyContext context)
    {
        return new OrderedMap<string, AutomationConfig>();
    }

    public IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IPartPropertyContext context)
    {
        var configs = new OrderedMap<string, AutomationConfig>();

        // Always declare the DS variance parameters (pre-declared, visible before rendering)
        var availableParams = _dsSession.IsVarianceLoaded
            ? _dsSession.AvailableVarianceParams
            : VarianceParamTypes.All.ToArray();

        foreach (var param in availableParams)
        {
            if (VarianceDisplayConfigs.TryGetValue(param, out var cfg))
                configs.Add($"ds_{param}", cfg);
            else
                configs.Add($"ds_{param}", new AutomationConfig
                {
                    DisplayText = $"DS {param}",
                    DefaultValue = double.NaN,
                    MinValue = -96,
                    MaxValue = 0,
                    Color = "#888888"
                });
        }

        // Always declare mapped parameter slots (even before models are loaded)
        foreach (var mapping in _config.VarianceMappings)
        {
            if (!string.IsNullOrEmpty(mapping.TargetParam) &&
                !configs.ContainsKey($"mapped_{mapping.TargetParam}"))
            {
                configs.Add($"mapped_{mapping.TargetParam}", new AutomationConfig
                {
                    DisplayText = $"[DS→{mapping.TargetParam}]",
                    DefaultValue = double.NaN,
                    MinValue = -96,
                    MaxValue = 0,
                    Color = "#FFAA44"
                });
            }
        }

        return configs;
    }

    private static readonly Dictionary<string, AutomationConfig> VarianceDisplayConfigs = new()
    {
        ["energy"] = new AutomationConfig
        {
            DisplayText = "DS Energy",
            DefaultValue = double.NaN,
            MinValue = -96, MaxValue = 0,
            Color = "#E5A573"
        },
        ["breathiness"] = new AutomationConfig
        {
            DisplayText = "DS Breathiness",
            DefaultValue = double.NaN,
            MinValue = -96, MaxValue = 0,
            Color = "#73C2E5"
        },
        ["voicing"] = new AutomationConfig
        {
            DisplayText = "DS Voicing",
            DefaultValue = double.NaN,
            MinValue = -96, MaxValue = 0,
            Color = "#E573B0"
        },
        ["tension"] = new AutomationConfig
        {
            DisplayText = "DS Tension",
            DefaultValue = double.NaN,
            MinValue = -10, MaxValue = 10,
            Color = "#73E5A5"
        },
    };

    // ================================================================
    //  Synthesis Pipeline
    // ================================================================

    public SynthesisSegment? GetNextSegment(double startTime, double endTime)
    {
        if (_needResegment) Resegment();

        foreach (var piece in _pieces)
        {
            if (!piece.Dirty || piece.Synthesizing)
                continue;
            if (piece.EndTime < startTime || piece.StartTime > endTime)
                continue;
            return new SynthesisSegment(piece.StartTime, piece.EndTime);
        }
        return null;
    }

    public async Task SynthesizeNext(SynthesisSegment segment, CancellationToken cancellation = default)
    {
        var piece = _pieces.FirstOrDefault(p =>
            p.StartTime == segment.StartTime && p.EndTime == segment.EndTime);
        if (piece == null || !piece.Dirty)
            return;

        UpdateConfigFromContext();

        var snapshot = _context.GetSnapshot(
            piece.Notes,
            piece.Notes[0].StartTime.Value,
            piece.Notes[^1].EndTime.Value);

        piece.Dirty = false;
        piece.Synthesizing = true;
        StatusChanged?.Invoke();

        try
        {
            SynthesizedParams? result = null;
            float[]? audioSamples = null;
            int audioSampleRate = 44100;

            await Task.Run(() =>
            {
                result = ExtractParameters(snapshot, piece.Notes, _config, cancellation);

                // If acoustic model + vocoder are loaded, synthesize audio
                if (result != null && _dsSession.IsAcousticLoaded && _dsSession.IsVocoderLoaded)
                {
                    audioSamples = RenderAudio(result, _config, cancellation);
                    var vc = _dsSession.VocoderConfig;
                    if (vc != null) audioSampleRate = vc.sample_rate;
                }
            }, CancellationToken.None);

            if (result != null)
            {
                piece.SynthesizedParams = result;
                if (audioSamples != null && audioSamples.Length > 0)
                {
                    long sampleOffset = (long)(segment.StartTime * audioSampleRate);
                    var seg = _context.CreateAudioSegment(sampleOffset, audioSamples.Length, audioSampleRate);
                    seg.Write(0, audioSamples);
                    seg.Commit();
                }
            }
        }
        catch (Exception ex)
        {
            piece.Failed = true;
            piece.Error = ex.Message;
        }
        finally
        {
            piece.Synthesizing = false;
            StatusChanged?.Invoke();
        }
    }

    // ================================================================
    //  Core Parameter Extraction
    // ================================================================

    /// <summary>
    /// Render audio from extracted DS parameters using acoustic model + vocoder.
    /// </summary>
    private float[]? RenderAudio(SynthesizedParams dsParams, PluginConfig config, CancellationToken cancellation)
    {
        if (dsParams.Phonemes.Count == 0 || dsParams.PitchCurve == null) return null;

        var frameMs = _dsSession.GetFrameMs();
        var durationsFrames = new List<int>();
        foreach (var ph in dsParams.Phonemes)
        {
            int frames = Math.Max(1, (int)((ph.EndTime - ph.StartTime) * 1000.0 / frameMs));
            durationsFrames.Add(frames);
        }
        int totalFrames = durationsFrames.Sum();
        if (totalFrames == 0) return null;

        var phonemeSymbols = dsParams.Phonemes.Select(p => p.Symbol).ToArray();
        double startTime = dsParams.Phonemes[0].StartTime;

        // F0 in Hz
        var f0Hz = new float[totalFrames];
        for (int f = 0; f < Math.Min(totalFrames, dsParams.PitchCurve.Length); f++)
            f0Hz[f] = (float)DsModelSession.ToneToFreq(dsParams.PitchCurve[f] * 0.01 + 69);

        // Variance params
        Dictionary<string, float[]>? varianceParams = null;
        if (dsParams.ParameterCurves.Count > 0)
        {
            varianceParams = new Dictionary<string, float[]>();
            foreach (var (name, points) in dsParams.ParameterCurves)
            {
                if (points.Count == 0) continue;
                var curve = new float[totalFrames];
                for (int f = 0; f < totalFrames; f++)
                {
                    double t = startTime + f * frameMs / 1000.0;
                    curve[f] = (float)points.OrderBy(p => Math.Abs(p.X - t)).First().Y;
                }
                varianceParams[name] = curve;
            }
        }

        float depth = Math.Clamp((float)config.DiffusionDepth, 0.01f, 1f);
        var mel = _dsSession.PredictAcoustic(
            phonemeSymbols, durationsFrames.ToArray(), f0Hz,
            totalFrames, config.AcousticSteps, depth, varianceParams);

        if (cancellation.IsCancellationRequested) return null;

        return _dsSession.PredictVocoder(mel, f0Hz);
    }

    private SynthesizedParams? ExtractParameters(
        SynthesisSnapshot snapshot,
        IReadOnlyList<ILiveNote> origins,
        PluginConfig config,
        CancellationToken cancellation)
    {
        if (snapshot.Notes.Count == 0) return null;

        // ---- Step 1: Determine which notes to process ----
        IReadOnlyList<SynthesisNoteSnapshot> notes;
        if (config.Mode == ApplyMode.SelectedNotes)
        {
            var noteList = new List<SynthesisNoteSnapshot>(origins.Count);
            foreach (var o in origins)
            {
                var phonemeList = o.Phonemes.Value;
                noteList.Add(new SynthesisNoteSnapshot
                {
                    StartTime = o.StartTime.Value,
                    EndTime = o.EndTime.Value,
                    Pitch = o.Pitch.Value,
                    Lyric = o.Lyric.Value,
                Phonemes = phonemeList.Select(p => new PinnedPhoneme { Symbol = p.Symbol, StartTime = p.StartTime, EndTime = p.EndTime }).ToArray(),
                    Properties = PropertyObject.Empty
                });
            }
            notes = noteList;
        }
        else
        {
            notes = snapshot.Notes.ToArray();
        }

        if (notes.Count == 0) return null;

        // ---- Step 2: Ensure models are loaded ----
        EnsureModelsLoaded(config);

        // ---- Step 3: G2P conversion ----
        var lyricToPhonemes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var allPhonemes = new List<string>();
        var midiTones = new List<int>();
        var noteEndTimesMs = new List<double>();
        var phonemeToNoteMap = new List<int>();

        double startTime = notes[0].StartTime;
        double endTime = notes.Max(n => n.EndTime);

        foreach (var note in notes)
        {
            if (cancellation.IsCancellationRequested) return null;

            string lyric = string.IsNullOrEmpty(note.Lyric) ? DefaultLyric : note.Lyric;

            if (lyric == "R" || lyric == "r")
            {
                lyricToPhonemes[lyric] = new[] { "SP" };
                allPhonemes.Add("SP");
                midiTones.Add(notes[^1].Pitch);
                noteEndTimesMs.Add(notes[^1].EndTime * 1000.0);
                phonemeToNoteMap.Add(phonemeToNoteMap.Count);
                continue;
            }

            string[]? phonemes;
            if (!lyricToPhonemes.TryGetValue(lyric, out phonemes))
            {
                if (_dsSession.G2p != null)
                {
                    phonemes = _dsSession.G2p.Query(lyric);
                }
                else
                {
                    phonemes = null;
                }

                if (phonemes is null or { Length: 0 })
                {
                    phonemes = new[] { lyric };
                }
                lyricToPhonemes[lyric] = phonemes;
            }

            allPhonemes.AddRange(phonemes);
            int nPitch = note.Pitch;
            double nEndMs = note.EndTime * 1000.0;
            foreach (var _ in phonemes)
            {
                midiTones.Add(nPitch);
                noteEndTimesMs.Add(nEndMs);
                phonemeToNoteMap.Add(phonemeToNoteMap.Count);
            }
        }

        if (allPhonemes.Count == 0) return null;

        // ---- Step 4: Run dur model ----
        double frameMs = _dsSession.GetFrameMs();
        var durResult = _dsSession.PredictDuration(
            allPhonemes.ToArray(),
            midiTones.ToArray(),
            noteEndTimesMs.ToArray(),
            startTime * 1000.0,
            frameMs,
            steps: config.DurSteps);

        // ---- Step 5: Smart phoneme alignment (per-note vowel/consonant alignment) ----
        // Collect per-note G2P results for alignment
        var noteG2pResults = new List<(string lyric, string[] dsPhonemes)>();
        foreach (var note in notes)
        {
            string lyric = string.IsNullOrEmpty(note.Lyric) ? DefaultLyric : note.Lyric;
            var phonemes = lyricToPhonemes.GetValueOrDefault(lyric, new[] { lyric });
            noteG2pResults.Add((lyric, phonemes));
        }

        var alignmentResult = SmartAlignPhonemeDurations(
            durResult,
            noteG2pResults,
            notes,
            config.DiscardOnMismatch,
            frameMs);

        durResult = alignmentResult.AlignedDurResult;
        var alignedMidiTones = alignmentResult.AlignedMidiTones;
        var alignedPhonemeToNoteMap = alignmentResult.AlignedPhonemeToNoteMap;
        allPhonemes = alignmentResult.AlignedPhonemes;

        // ---- Step 6: Build phoneme timings ----
        int nPhonemes = durResult.PhonemeSymbols.Length;
        var phonemeStartTimes = new List<double>();
        double currentTime = startTime * 1000.0;
        foreach (var d in durResult.DurationsMs)
        {
            phonemeStartTimes.Add(currentTime / 1000.0);
            currentTime += d;
        }

        var durationsFrames = durResult.DurationsFrames;
        int totalFrames = durResult.TotalFrames;

        // ---- Step 7: Read existing automation curves for blend ----
        var existingCurves = new Dictionary<string, float[]>();
        foreach (var mapping in config.VarianceMappings)
        {
            if (string.IsNullOrEmpty(mapping.TargetParam)) continue;
            var curve = SampleExistingCurve(mapping.TargetParam, startTime, endTime, totalFrames, frameMs);
            if (curve != null)
                existingCurves[mapping.TargetParam] = curve;
        }

        // ---- Step 8: Run pitch model ----
        float[]? pitchCurve = null;
        if (_dsSession.IsPitchLoaded)
        {
            pitchCurve = _dsSession.PredictPitch(
                durResult.PhonemeSymbols,
                alignedMidiTones.ToArray(),
                durationsFrames,
                totalFrames,
                frameMs,
                config.Expressiveness,
                startTime * 1000.0,
                steps: config.PitchSteps);
        }

        // ---- Step 9: Run variance model ----
        Dictionary<string, float[]>? varianceCurves = null;
        if (_dsSession.IsVarianceLoaded)
        {
            varianceCurves = _dsSession.PredictVariance(
                durResult.PhonemeSymbols,
                alignedMidiTones.ToArray(),
                durationsFrames,
                totalFrames,
                frameMs,
                config.Expressiveness,
                pitchCurve,
                steps: config.VarianceSteps);
        }

        // ---- Step 10: Build frame time array ----
        var frameTimes = new double[totalFrames];
        for (int f = 0; f < totalFrames; f++)
        {
            frameTimes[f] = startTime + f * frameMs / 1000.0;
        }

        // ---- Step 11: Build synthesized parameter curves ----
        var result = new SynthesizedParams
        {
            PitchCurve = pitchCurve,
            FrameTimes = frameTimes,
        };

        if (varianceCurves != null)
        {
            float bf = config.BlendFactor;

            foreach (var (paramName, curveData) in varianceCurves)
            {
                if (!_dsSession.AvailableVarianceParams.Contains(paramName))
                    continue;

                var points = new List<Point>();
                for (int f = 0; f < Math.Min(curveData.Length, totalFrames); f++)
                {
                    float value = curveData[f];
                    float existingValue = 0f;
                    var existingParam = config.VarianceMappings
                        .FirstOrDefault(m => m.SourceParam == paramName);
                    if (existingParam != null && existingCurves.TryGetValue(existingParam.TargetParam, out var existingArr))
                    {
                        if (f < existingArr.Length)
                            existingValue = existingArr[f];
                    }

                    float finalValue = value * bf + existingValue * (1f - bf);
                    double displayValue = NormalizeVarianceParam(paramName, finalValue);
                    points.Add(new Point(frameTimes[f], displayValue));
                }
                result.ParameterCurves[paramName] = points;
            }

            foreach (var mapping in config.VarianceMappings)
            {
                if (string.IsNullOrEmpty(mapping.SourceParam) || string.IsNullOrEmpty(mapping.TargetParam))
                    continue;

                if (!varianceCurves.TryGetValue(mapping.SourceParam, out var sourceData))
                    continue;

                existingCurves.TryGetValue(mapping.TargetParam, out var existingTargetCurve);

                float bf2 = config.BlendFactor;
                var mappedPoints = new List<Point>();
                for (int f = 0; f < Math.Min(sourceData.Length, totalFrames); f++)
                {
                    float dsValue = sourceData[f];
                    float existingVal = (existingTargetCurve != null && f < existingTargetCurve.Length)
                        ? existingTargetCurve[f] : 0f;

                    float blended = dsValue * bf2 + existingVal * (1f - bf2);
                    mappedPoints.Add(new Point(frameTimes[f], blended));
                }
                result.MappedParameterCurves[mapping.TargetParam] = mappedPoints;
            }
        }

        // ---- Step 12: Build phoneme output ----
        int phIdx = 0;
        for (int ni = 0; ni < notes.Count && phIdx < nPhonemes; ni++)
        {
            var note = notes[ni];
            string lyric = string.IsNullOrEmpty(note.Lyric) ? DefaultLyric : note.Lyric;

            if (!lyricToPhonemes.TryGetValue(lyric, out var phonemes))
                continue;

            if (phonemes == null) continue;

            for (int p = 0; p < phonemes.Length && phIdx < nPhonemes; p++)
            {
                double phStart = phonemeStartTimes[phIdx];
                double phEnd = phStart + durResult.DurationsMs[phIdx] / 1000.0;

                result.Phonemes.Add(new SynthesizedPhoneme
                {
                    Symbol = durResult.PhonemeSymbols[phIdx],
                    StartTime = phStart,
                    EndTime = phEnd,
                    Note = ni < origins.Count ? origins[ni] : null,
                    StretchWeight = phEnd - phStart,
                });
                phIdx++;
            }
        }

        return result;
    }

    // ================================================================
    //  Smart Phoneme Alignment
    // ================================================================

    /// <summary>
    /// Result of the smart phoneme alignment process.
    /// </summary>
    private class PhonemeAlignmentResult
    {
        public DurInferenceResult AlignedDurResult { get; init; } = null!;
        public List<int> AlignedMidiTones { get; init; } = new();
        public List<int> AlignedPhonemeToNoteMap { get; init; } = new();
        public List<string> AlignedPhonemes { get; init; } = new();
    }

    /// <summary>
    /// Align DS phoneme durations to target engine phonemes using vowel/consonant
    /// per-note matching. If target has 1 vowel but DS has N, merge those N DS 
    /// vowel durations into one. Same for consonants.
    /// </summary>
    private static PhonemeAlignmentResult SmartAlignPhonemeDurations(
        DurInferenceResult durResult,
        IReadOnlyList<(string lyric, string[] dsPhonemes)> noteG2pResults,
        IReadOnlyList<SynthesisNoteSnapshot> targetNotes,
        bool discardOnMismatch,
        double frameMs)
    {
        var alignedSymbols = new List<string>();
        var alignedDurationsMs = new List<double>();
        var alignedDurationsFrames = new List<int>();
        var alignedMidiTones = new List<int>();
        var alignedPhonemeToNoteMap = new List<int>();
        var alignedPhonemes = new List<string>();

        int dsGlobalIdx = 0; // Index into durResult.PhonemeSymbols

        for (int noteIdx = 0; noteIdx < noteG2pResults.Count; noteIdx++)
        {
            var (lyric, dsPhonemes) = noteG2pResults[noteIdx];
            var targetNote = targetNotes[noteIdx];
            int midiPitch = targetNote.Pitch;

            // Skip rest notes
            if (lyric == "R" || lyric == "r")
            {
                if (dsGlobalIdx < durResult.PhonemeSymbols.Length)
                {
                    alignedSymbols.Add(durResult.PhonemeSymbols[dsGlobalIdx]);
                    alignedDurationsMs.Add(durResult.DurationsMs[dsGlobalIdx]);
                    alignedDurationsFrames.Add(durResult.DurationsFrames[dsGlobalIdx]);
                    alignedMidiTones.Add(midiPitch);
                    alignedPhonemeToNoteMap.Add(noteIdx);
                    alignedPhonemes.Add("SP");
                    dsGlobalIdx++;
                }
                continue;
            }

            // Get the DS phoneme slice for this note
            int dsNotePhonemeCount = dsPhonemes.Length;
            if (dsGlobalIdx + dsNotePhonemeCount > durResult.PhonemeSymbols.Length)
            {
                // Not enough DS phonemes - trim
                dsNotePhonemeCount = Math.Max(0, durResult.PhonemeSymbols.Length - dsGlobalIdx);
            }

            if (dsNotePhonemeCount <= 0)
                continue;

            // Collect DS phoneme symbols, durations, and classifications
            var dsSliceSymbols = new List<string>();
            var dsSliceDurationsMs = new List<double>();
            var dsSliceDurationsFrames = new List<int>();
            for (int i = 0; i < dsNotePhonemeCount; i++)
            {
                int idx = dsGlobalIdx + i;
                dsSliceSymbols.Add(durResult.PhonemeSymbols[idx]);
                dsSliceDurationsMs.Add(durResult.DurationsMs[idx]);
                dsSliceDurationsFrames.Add(durResult.DurationsFrames[idx]);
            }

            // Classify DS phonemes: true = vowel-like, false = consonant-like
            // For phoneme symbols like "zh/a", extract the part after "/"
            var dsIsVowel = dsSliceSymbols.Select(s => IsLikelyVowel(s)).ToList();

            // Get target phoneme count for this note (from the target engine)
            var targetPhonemes = targetNote.Phonemes?.Select(p => p.Symbol).ToArray() ?? Array.Empty<string>();
            var targetIsVowel = targetPhonemes.Select(s => IsLikelyVowel(s)).ToList();

            if (targetPhonemes.Length == 0)
            {
                // No target phonemes - just pass through DS phonemes
                for (int i = 0; i < dsNotePhonemeCount; i++)
                {
                    int idx = dsGlobalIdx + i;
                    alignedSymbols.Add(durResult.PhonemeSymbols[idx]);
                    alignedDurationsMs.Add(durResult.DurationsMs[idx]);
                    alignedDurationsFrames.Add(durResult.DurationsFrames[idx]);
                    alignedMidiTones.Add(midiPitch);
                    alignedPhonemeToNoteMap.Add(noteIdx);
                    alignedPhonemes.Add(dsPhonemes[i]);
                }
                dsGlobalIdx += dsNotePhonemeCount;
                continue;
            }

            // Align DS phonemes to target phonemes by vowel/consonant categories
            try
            {
                var mergedSymbols = new List<string>();
                var mergedDurationsMs = new List<double>();
                var mergedDurationsFrames = new List<int>();
                var mergedPhonemes = new List<string>();

                int dsIdx = 0;
                foreach (var targetType in targetIsVowel)
                {
                    if (dsIdx >= dsSliceSymbols.Count)
                        break;

                    // Collect all DS phonemes of the same type (or until type changes)
                    double accMs = 0;
                    int accFrames = 0;
                    var accSymbols = new List<string>();
                    var accPhonemes = new List<string>();

                    // If target has 1 of this type, merge all consecutive DS of same type
                    // If target has N of this type where N == DS count, do 1:1
                    while (dsIdx < dsSliceSymbols.Count && dsIsVowel[dsIdx] == targetType)
                    {
                        accMs += dsSliceDurationsMs[dsIdx];
                        accFrames += dsSliceDurationsFrames[dsIdx];
                        accSymbols.Add(dsSliceSymbols[dsIdx]);
                        accPhonemes.Add(dsIdx < dsPhonemes.Length ? dsPhonemes[dsIdx] : dsSliceSymbols[dsIdx]);
                        dsIdx++;
                    }

                    if (accSymbols.Count > 0)
                    {
                        // Merge: take the last symbol (or combined) for the merged result
                        mergedSymbols.Add(accSymbols[^1]); // Use last symbol
                        mergedDurationsMs.Add(accMs);
                        mergedDurationsFrames.Add(accFrames);
                        mergedPhonemes.Add(string.Join(" ", accPhonemes));
                    }
                }

                // Handle any remaining DS phonemes (extra consonants/vowels after target runs out)
                while (dsIdx < dsSliceSymbols.Count)
                {
                    // If we have remaining DS phonemes and target ran out, merge them into last segment
                    if (mergedSymbols.Count > 0)
                    {
                        mergedDurationsMs[^1] += dsSliceDurationsMs[dsIdx];
                        mergedDurationsFrames[^1] += dsSliceDurationsFrames[dsIdx];
                        mergedPhonemes[^1] += " " + dsSliceSymbols[dsIdx];
                    }
                    dsIdx++;
                }

                // Now add the aligned results
                for (int i = 0; i < mergedSymbols.Count; i++)
                {
                    alignedSymbols.Add(mergedSymbols[i]);
                    alignedDurationsMs.Add(i < mergedDurationsMs.Count ? mergedDurationsMs[i] : 0);
                    alignedDurationsFrames.Add(i < mergedDurationsFrames.Count ? mergedDurationsFrames[i] : 1);
                    alignedMidiTones.Add(midiPitch);
                    alignedPhonemeToNoteMap.Add(noteIdx);
                    alignedPhonemes.Add(i < mergedPhonemes.Count ? mergedPhonemes[i] : mergedSymbols[i]);
                }
            }
            catch
            {
                // Fallback: pass through DS phonemes as-is
                for (int i = 0; i < dsNotePhonemeCount; i++)
                {
                    int idx = dsGlobalIdx + i;
                    alignedSymbols.Add(durResult.PhonemeSymbols[idx]);
                    alignedDurationsMs.Add(durResult.DurationsMs[idx]);
                    alignedDurationsFrames.Add(durResult.DurationsFrames[idx]);
                    alignedMidiTones.Add(midiPitch);
                    alignedPhonemeToNoteMap.Add(noteIdx);
                    alignedPhonemes.Add(i < dsPhonemes.Length ? dsPhonemes[i] : durResult.PhonemeSymbols[idx]);
                }
            }

            dsGlobalIdx += dsNotePhonemeCount;
        }

        // Handle remaining DS phonemes (discard if discardOnMismatch, otherwise include)
        if (!discardOnMismatch)
        {
            while (dsGlobalIdx < durResult.PhonemeSymbols.Length)
            {
                int lastPitch = alignedMidiTones.Count > 0 ? alignedMidiTones[^1] : 60;
                int lastNoteIdx = alignedPhonemeToNoteMap.Count > 0 ? alignedPhonemeToNoteMap[^1] : 0;
                alignedSymbols.Add(durResult.PhonemeSymbols[dsGlobalIdx]);
                alignedDurationsMs.Add(durResult.DurationsMs[dsGlobalIdx]);
                alignedDurationsFrames.Add(durResult.DurationsFrames[dsGlobalIdx]);
                alignedMidiTones.Add(lastPitch);
                alignedPhonemeToNoteMap.Add(lastNoteIdx);
                alignedPhonemes.Add(durResult.PhonemeSymbols[dsGlobalIdx]);
                dsGlobalIdx++;
            }
        }

        // Build aligned DurInferenceResult
        int totalFrames = alignedDurationsFrames.Sum();
        var alignedDurResult = new DurInferenceResult
        {
            PhonemeSymbols = alignedSymbols.ToArray(),
            DurationsMs = alignedDurationsMs.ToArray(),
            DurationsFrames = alignedDurationsFrames.ToArray(),
            TotalFrames = totalFrames,
            FrameMs = frameMs,
        };

        return new PhonemeAlignmentResult
        {
            AlignedDurResult = alignedDurResult,
            AlignedMidiTones = alignedMidiTones,
            AlignedPhonemeToNoteMap = alignedPhonemeToNoteMap,
            AlignedPhonemes = alignedPhonemes,
        };
    }

    /// <summary>
    /// Determine if a phoneme symbol is likely a vowel.
    /// Checks the phoneme part after "/" if present, or the full symbol.
    /// </summary>
    private static bool IsLikelyVowel(string symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return false;

        // Get the part after "/" (e.g., "zh/a" -> "a")
        int slashIdx = symbol.IndexOf('/');
        string core = slashIdx >= 0 ? symbol[(slashIdx + 1)..] : symbol;

        if (string.IsNullOrEmpty(core))
            return false;

        // Special phonemes
        if (core == "SP" || core == "AP" || core == "ExAP")
            return true;

        // Check first character
        char first = core[0];
        return first == 'a' || first == 'e' || first == 'i' || first == 'o' || first == 'u' ||
               first == 'A' || first == 'E' || first == 'I' || first == 'O' || first == 'U';
    }

    private float[]? SampleExistingCurve(string paramName, double startTime, double endTime,
        int totalFrames, double frameMs)
    {
        if (!_context.TryGetAutomation(paramName, out var automation))
            return null;

        var result = new float[totalFrames];
        var times = new double[totalFrames];
        for (int f = 0; f < totalFrames; f++)
        {
            times[f] = startTime + f * frameMs / 1000.0;
        }
        try
        {
            var values = automation.Evaluate(times);
            for (int f = 0; f < totalFrames; f++)
            {
                result[f] = (float)values[f];
            }
        }
        catch
        {
            Array.Fill(result, 0f);
        }
        return result;
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static float NormalizeVarianceParam(string paramName, float value)
    {
        return paramName.ToLowerInvariant() switch
        {
            "tension" => Math.Clamp((value + 10f) / 20f, 0f, 1f),
            "energy" or "breathiness" or "voicing" => Math.Clamp((value + 96f) / 96f, 0f, 1f),
            _ => Math.Clamp(value, 0f, 1f)
        };
    }

    private void EnsureModelsLoaded(PluginConfig config)
    {
        // Set device first (will trigger reload if changed)
        _dsSession.SetDevice(config.Device);

        bool pathsChanged = false;

        // Check dsdur model
        if (!string.IsNullOrEmpty(config.DurModelPath) &&
            (_durModelDirCached != config.DurModelPath || !_dsSession.IsDurLoaded))
        {
            _dsSession.LoadDurModel(config.DurModelPath);
            _dsSession.LoadG2p(config.Language);
            _durModelDirCached = config.DurModelPath;
            pathsChanged = true;
        }

        // Check dspitch model
        if (!string.IsNullOrEmpty(config.PitchModelPath) &&
            (_pitchModelDirCached != config.PitchModelPath || !_dsSession.IsPitchLoaded))
        {
            _dsSession.LoadPitchModel(config.PitchModelPath);
            _pitchModelDirCached = config.PitchModelPath;
            pathsChanged = true;
        }

        // Check dsvariance model
        if (!string.IsNullOrEmpty(config.VarianceModelPath) &&
            (_varianceModelDirCached != config.VarianceModelPath || !_dsSession.IsVarianceLoaded))
        {
            _dsSession.LoadVarianceModel(config.VarianceModelPath);
            _varianceModelDirCached = config.VarianceModelPath;
            pathsChanged = true;
        }

        // Check acoustic model + vocoder (singer root)
        if (!string.IsNullOrEmpty(config.SingerRootPath) &&
            (_singerRootDirCached != config.SingerRootPath || !_dsSession.IsAcousticLoaded))
        {
            _dsSession.LoadAcousticModel(config.SingerRootPath);
            _singerRootDirCached = config.SingerRootPath;
            pathsChanged = true;
        }

        // Load vocoder after acoustic model is loaded
        if (!string.IsNullOrEmpty(config.SingerRootPath) &&
            _dsSession.IsAcousticLoaded &&
            !_dsSession.IsVocoderLoaded)
        {
            _dsSession.LoadVocoder(config.SingerRootPath);
            pathsChanged = true;
        }

        if (pathsChanged)
            SaveConfig();
    }

    private void RefreshTargetAutomationKeys()
    {
        var keys = new List<string>
        {
            "pitch",
            "pitch_deviation",
            "TENC",
            "BRE",
            "VOIC",
            "ENER",
            "CLR",
            "VEL",
        };
        _targetAutomationKeys = keys;
    }

    private void UpdateConfigFromContext()
    {
        var props = _context.PartProperties;

        // Model paths
        _config.DurModelPath = ReadPropertyString(props, "dur_model_path", _config.DurModelPath);
        _config.PitchModelPath = ReadPropertyString(props, "pitch_model_path", _config.PitchModelPath);
        _config.VarianceModelPath = ReadPropertyString(props, "variance_model_path", _config.VarianceModelPath);
        _config.SingerRootPath = ReadPropertyString(props, "singer_root_path", _config.SingerRootPath);

        // Singers directory (save globally when changed)
        string newSingersDir = ReadPropertyString(props, "singers_directory", _config.SingerRootPath);
        string currentSingersDir = DiffSingerEngine.LoadSingersDirectory();
        if (!string.IsNullOrEmpty(newSingersDir) && newSingersDir != currentSingersDir)
        {
            DiffSingerEngine.SaveSingersDirectory(newSingersDir);
        }

        // Language
        _config.Language = ReadPropertyString(props, "language", _config.Language);

        // Device
        var deviceStr = ReadPropertyString(props, "exec_device", "CPU");
        _config.Device = deviceStr == "DirectML" ? ExecutionDevice.DirectML : ExecutionDevice.CPU;
        _config.GpuIndex = (int)ReadPropertyFloat(props, "gpu_index", (float)_config.GpuIndex);

        // Sampling Steps
        _config.DurSteps = (int)ReadPropertyFloat(props, "dur_steps", (float)_config.DurSteps);
        _config.AcousticSteps = (int)ReadPropertyFloat(props, "acoustic_steps", (float)_config.AcousticSteps);
        _config.PitchSteps = (int)ReadPropertyFloat(props, "pitch_steps", (float)_config.PitchSteps);
        _config.VarianceSteps = (int)ReadPropertyFloat(props, "variance_steps", (float)_config.VarianceSteps);
        _config.DiffusionDepth = ReadPropertyFloat(props, "diffusion_depth", (float)_config.DiffusionDepth);

        // Performance Sliders
        _config.Expressiveness = ReadPropertyFloat(props, "expressiveness", _config.Expressiveness);
        _config.BlendPercent = ReadPropertyFloat(props, "blend", _config.BlendPercent);

        // Advanced toggles
        _config.TensorCache = ReadPropertyBool(props, "tensor_cache", _config.TensorCache);
        _config.VarianceLocalPitchPatch = ReadPropertyBool(props, "variance_patch", _config.VarianceLocalPitchPatch);
        _config.UnvoicedF0Interpolate = ReadPropertyBool(props, "unvoiced_f0", _config.UnvoicedF0Interpolate);
        _config.DiscardOnMismatch = ReadPropertyBool(props, "discard_mismatch", _config.DiscardOnMismatch);

        // Apply mode
        var modeStr = ReadPropertyString(props, "apply_mode", "Selected Notes");
        _config.Mode = modeStr == "Whole Track" ? ApplyMode.WholeTrack : ApplyMode.SelectedNotes;

        // Variance mappings (up to 6)
        _config.VarianceMappings.Clear();
        for (int i = 0; i < 6; i++)
        {
            var idx = i.ToString();
            var source = ReadPropertyString(props, $"variance_map_{idx}_source", "");
            var target = ReadPropertyString(props, $"variance_map_{idx}_target", "");

            if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(target))
            {
                _config.VarianceMappings.Add(new VarianceMapping
                {
                    SourceParam = source,
                    TargetParam = target
                });
            }
        }

        // Sync device to model session
        if (_dsSession.GetDevice() != _config.Device)
        {
            _dsSession.SetDevice(_config.Device);
            // Force model reload on next extraction
            _durModelDirCached = null;
            _pitchModelDirCached = null;
            _varianceModelDirCached = null;
        }
    }

    // Track cached model dirs to detect changes
    private string? _durModelDirCached;
    private string? _pitchModelDirCached;
    private string? _varianceModelDirCached;
    private string? _singerRootDirCached;

    // Helper to read from IReadOnlyNotifiablePropertyObject
    private static string ReadPropertyString(IReadOnlyNotifiablePropertyObject props, string key, string defaultValue)
    {
        var prop = props.StringField(key, defaultValue);
        return prop.Value;
    }

    private static float ReadPropertyFloat(IReadOnlyNotifiablePropertyObject props, string key, float defaultValue)
    {
        var prop = props.NumberField(key, defaultValue);
        return (float)prop.Value;
    }

    private static bool ReadPropertyBool(IReadOnlyNotifiablePropertyObject props, string key, bool defaultValue)
    {
        var prop = props.BoolField(key, defaultValue);
        return prop.Value;
    }

    // ================================================================
    //  Output Properties
    // ================================================================

    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch
    {
        get
        {
            var segments = new List<IReadOnlyList<Point>>();
            foreach (var piece in _pieces)
            {
                if (piece.SynthesizedParams?.PitchCurve == null) continue;

                var pitchData = piece.SynthesizedParams.PitchCurve;
                var frameTimes = piece.SynthesizedParams.FrameTimes;

                if (pitchData.Length == 0 || frameTimes.Length == 0) continue;

                var points = new List<Point>();
                for (int f = 0; f < Math.Min(pitchData.Length, frameTimes.Length); f++)
                {
                    double semitones = pitchData[f] * 0.01;
                    points.Add(new Point(frameTimes[f], semitones));
                }
                if (points.Count > 0)
                    segments.Add(points);
            }
            return segments;
        }
    }

    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters
    {
        get
        {
            var map = new Map<string, SynthesizedParameter>();

            var allParamNames = new HashSet<string>();
            var allMappedNames = new HashSet<string>();
            foreach (var piece in _pieces)
            {
                if (piece.SynthesizedParams == null) continue;
                foreach (var key in piece.SynthesizedParams.ParameterCurves.Keys)
                    allParamNames.Add(key);
                foreach (var key in piece.SynthesizedParams.MappedParameterCurves.Keys)
                    allMappedNames.Add(key);
            }

            // DS variance parameters: prefix with "ds_"
            foreach (var paramName in allParamNames)
            {
                var segments = new List<IReadOnlyList<Point>>();
                foreach (var piece in _pieces)
                {
                    if (piece.SynthesizedParams == null) continue;
                    if (piece.SynthesizedParams.ParameterCurves.TryGetValue(paramName, out var points) && points.Count > 0)
                        segments.Add(points);
                }
                if (segments.Count > 0)
                    map[$"ds_{paramName}"] = new SynthesizedParameter { Segments = segments };
            }

            // Mapped parameters: prefix with "mapped_"
            foreach (var mappedName in allMappedNames)
            {
                var segments = new List<IReadOnlyList<Point>>();
                foreach (var piece in _pieces)
                {
                    if (piece.SynthesizedParams == null) continue;
                    if (piece.SynthesizedParams.MappedParameterCurves.TryGetValue(mappedName, out var points) && points.Count > 0)
                        segments.Add(points);
                }
                if (segments.Count > 0)
                    map[$"mapped_{mappedName}"] = new SynthesizedParameter { Segments = segments };
            }

            return map;
        }
    }

    public IReadOnlyList<SynthesizedPhoneme> Phonemes
    {
        get
        {
            var result = new List<SynthesizedPhoneme>();
            foreach (var piece in _pieces)
            {
                if (piece.SynthesizedParams != null)
                    result.AddRange(piece.SynthesizedParams.Phonemes);
            }
            return result;
        }
    }

    public IReadOnlyList<SynthesisStatusSegment> GetStatus()
    {
        var result = new List<SynthesisStatusSegment>(_pieces.Count);
        foreach (var piece in _pieces)
        {
            var status = piece.Failed ? SynthesisSegmentStatus.Failed
                : piece.Synthesizing ? SynthesisSegmentStatus.Synthesizing
                : piece.Dirty ? SynthesisSegmentStatus.Pending
                : SynthesisSegmentStatus.Synthesized;

            string? message = piece.Failed
                ? $"Error: {piece.Error}"
                : piece.Synthesizing ? "Extracting DS parameters..." : null;

            result.Add(new SynthesisStatusSegment
            {
                StartTime = piece.StartTime,
                EndTime = piece.EndTime,
                Status = status,
                Message = message,
                Progress = piece.Synthesizing ? 0 : 1,
            });
        }
        return result;
    }

    public event Action? StatusChanged;

    public void Dispose()
    {
        _notesSubscription.Dispose();
        _context.Notes.ItemAdded -= OnNotesStructureChanged;
        _context.Notes.ItemRemoved -= OnNotesStructureChanged;
        _context.PartProperties.Modified -= MarkAllDirty;
        _context.Committed -= OnCommitted;
        _context.Pitch.RangeModified -= OnRangeModified;
        _context.PitchDeviation.RangeModified -= OnRangeModified;
        _dsSession.Dispose();
    }

    // ================================================================
    //  Piece Management
    // ================================================================

    private void Resegment()
    {
        _needResegment = false;

        var groups = new List<List<ILiveNote>>();
        List<ILiveNote>? current = null;
        double groupMaxEnd = 0;

        foreach (var note in _context.Notes)
        {
            if (current == null || note.StartTime.Value > groupMaxEnd)
            {
                current = new List<ILiveNote>();
                groups.Add(current);
                groupMaxEnd = note.EndTime.Value;
            }
            else
            {
                groupMaxEnd = Math.Max(groupMaxEnd, note.EndTime.Value);
            }
            current.Add(note);
        }

        var newPieces = new List<Piece>(groups.Count);
        foreach (var notes in groups)
        {
            double pieceEnd = notes.Max(n => n.EndTime.Value);
            var existing = _pieces.FirstOrDefault(p => p.Notes.SequenceEqual(notes));
            if (existing != null)
            {
                _pieces.Remove(existing);
                existing.StartTime = notes[0].StartTime.Value;
                existing.EndTime = pieceEnd;
                newPieces.Add(existing);
            }
            else
            {
                newPieces.Add(new Piece
                {
                    Notes = notes,
                    StartTime = notes[0].StartTime.Value,
                    EndTime = pieceEnd,
                    Dirty = true,
                });
            }
        }
        _pieces.Clear();
        _pieces.AddRange(newPieces);
    }

    // Event handlers
    private void OnNotesStructureChanged(ILiveNote note)
    {
        _needResegment = true;
        MarkAllDirty();
    }

    private void OnCommitted()
    {
        if (_needResegment) Resegment();
    }

    private void OnRangeModified(double startTime, double endTime)
    {
        MarkAllDirty();
    }

    private void MarkAllDirty()
    {
        _needResegment = true;
        foreach (var piece in _pieces)
            piece.Dirty = true;
        StatusChanged?.Invoke();
    }

    private void MarkDirtyOnNoteChange()
    {
        foreach (var piece in _pieces)
            piece.Dirty = true;
        StatusChanged?.Invoke();
    }

    private void SubscribeNote(ILiveNote note)
    {
        var handler = new Action(MarkDirtyOnNoteChange);
        note.StartTime.Modified += handler;
        note.EndTime.Modified += handler;
        note.Pitch.Modified += handler;
        note.Lyric.Modified += handler;
        _noteHandlers[note] = handler;
        MarkAllDirty();
    }

    private void UnsubscribeNote(ILiveNote note)
    {
        if (_noteHandlers.TryGetValue(note, out var handler))
        {
            note.StartTime.Modified -= handler;
            note.EndTime.Modified -= handler;
            note.Pitch.Modified -= handler;
            note.Lyric.Modified -= handler;
            _noteHandlers.Remove(note);
        }
    }

}
