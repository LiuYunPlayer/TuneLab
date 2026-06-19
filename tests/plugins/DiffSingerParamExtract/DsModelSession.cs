using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text;

namespace DiffSinger;

/// <summary>
/// Manages DiffSinger ONNX model sessions for linguistic, duration, pitch, and variance models.
/// Each model (dur/pitch/variance) has its own linguistic encoder per DiffSinger architecture.
/// </summary>
public partial class DsModelSession : IDisposable
{
    // ---- Dur model resources ----
    private InferenceSession? _durLinguisticSession;
    private InferenceSession? _durationSession;
    private DsConfig? _durConfig;
    private string _durDir = string.Empty;

    // ---- Pitch model resources ----
    private InferenceSession? _pitchLinguisticSession;
    private InferenceSession? _pitchSession;
    private DsConfig? _pitchConfig;
    private string _pitchDir = string.Empty;

    // ---- Variance model resources ----
    private InferenceSession? _varianceLinguisticSession;
    private InferenceSession? _varianceSession;
    private DsConfig? _varianceConfig;
    private string _varianceDir = string.Empty;

    // ---- Shared resources ----
    private Dictionary<string, int>? _durPhonemeTokens;
    private Dictionary<string, int>? _pitchPhonemeTokens;
    private Dictionary<string, int>? _variancePhonemeTokens;
    private G2pEngine? _g2p;

    public G2pEngine? G2p => _g2p;
    public DsConfig? DurConfig => _durConfig;
    public DsConfig? PitchConfig => _pitchConfig;
    public DsConfig? VarianceConfig => _varianceConfig;
    public string DurDir => _durDir;
    public string PitchDir => _pitchDir;
    public string VarianceDir => _varianceDir;

    public bool IsDurLoaded => _durationSession != null;
    public bool IsPitchLoaded => _pitchSession != null;
    public bool IsVarianceLoaded => _varianceSession != null;

    /// <summary>Available variance parameter names predicted by the variance model.</summary>
    public string[] AvailableVarianceParams { get; private set; } = Array.Empty<string>();

    private bool _disposed;
    private ExecutionDevice _device = ExecutionDevice.CPU;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _durLinguisticSession?.Dispose();
        _durationSession?.Dispose();
        _pitchLinguisticSession?.Dispose();
        _pitchSession?.Dispose();
        _varianceLinguisticSession?.Dispose();
        _varianceSession?.Dispose();
        _acousticSession?.Dispose();
        _vocoderSession?.Dispose();
    }

    // ================================================================
    //  Session Options
    // ================================================================

    private SessionOptions CreateSessionOptions()
    {
        var options = new SessionOptions();
        if (_device == ExecutionDevice.DirectML)
        {
            try
            {
                // DirectML execution provider (DML)
                options.AppendExecutionProvider_DML(deviceId: 0);
            }
            catch
            {
                // Fall back to CPU if DirectML unavailable
            }
        }
        return options;
    }

    private byte[] ReadModelBytes(string dirPath, string modelRelativePath)
    {
        var path = Path.Combine(dirPath, modelRelativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Model not found: {path}");
        return File.ReadAllBytes(path);
    }

    private InferenceSession CreateSession(byte[] modelBytes)
    {
        var opts = CreateSessionOptions();
        return new InferenceSession(modelBytes, opts);
    }

    // ================================================================
    //  Model Loading
    // ================================================================

    /// <summary>Set the execution device for subsequent model loads.</summary>
    public void SetDevice(ExecutionDevice device)
    {
        if (_device != device)
        {
            _device = device;
            // Force reload of all models with new device
            UnloadAll();
        }
    }

    public ExecutionDevice GetDevice() => _device;

    private void UnloadAll()
    {
        _durLinguisticSession?.Dispose(); _durLinguisticSession = null;
        _durationSession?.Dispose(); _durationSession = null;
        _pitchLinguisticSession?.Dispose(); _pitchLinguisticSession = null;
        _pitchSession?.Dispose(); _pitchSession = null;
        _varianceLinguisticSession?.Dispose(); _varianceLinguisticSession = null;
        _varianceSession?.Dispose(); _varianceSession = null;
        _acousticSession?.Dispose(); _acousticSession = null;
        _vocoderSession?.Dispose(); _vocoderSession = null;
        _durConfig = null; _pitchConfig = null; _varianceConfig = null;
        _durPhonemeTokens = null; _pitchPhonemeTokens = null; _variancePhonemeTokens = null;
        _acousticPhonemeTokens = null;
        _durDir = _pitchDir = _varianceDir = _singerRootDir = string.Empty;
        _speakerEmbed = null;
    }

    /// <summary>Load the dsdur model (linguistic + duration ONNX).</summary>
    public void LoadDurModel(string dirPath)
    {
        if (!Directory.Exists(dirPath))
            throw new DirectoryNotFoundException($"dsdur directory not found: {dirPath}");

        _durDir = dirPath;
        var config = LoadConfig(dirPath);
        _durConfig = config;

        // Phoneme list
        var phonemesPath = Path.Combine(dirPath, config.phonemes);
        if (!File.Exists(phonemesPath))
            throw new FileNotFoundException($"Phoneme list not found: {phonemesPath}");
        _durPhonemeTokens = DiffSingerUtils.LoadPhonemes(phonemesPath);

        // Dur linguistic model
        if (string.IsNullOrEmpty(config.linguistic))
            throw new InvalidDataException("dsconfig.yaml missing 'linguistic' for dur model");
        _durLinguisticSession?.Dispose();
        _durLinguisticSession = CreateSession(ReadModelBytes(dirPath, config.linguistic));

        // Duration model
        if (string.IsNullOrEmpty(config.dur))
            throw new InvalidDataException("dsconfig.yaml missing 'dur' field for dur model");
        _durationSession?.Dispose();
        _durationSession = CreateSession(ReadModelBytes(dirPath, config.dur));
    }

    /// <summary>Load the dspitch model (linguistic + pitch ONNX).</summary>
    public void LoadPitchModel(string dirPath)
    {
        if (!Directory.Exists(dirPath))
            throw new DirectoryNotFoundException($"dspitch directory not found: {dirPath}");

        _pitchDir = dirPath;
        var config = LoadConfig(dirPath);
        _pitchConfig = config;

        // Phoneme list — pitch model may have its own
        var phonemesPath = Path.Combine(dirPath, config.phonemes);
        if (File.Exists(phonemesPath))
            _pitchPhonemeTokens = DiffSingerUtils.LoadPhonemes(phonemesPath);

        // Pitch linguistic model
        if (string.IsNullOrEmpty(config.linguistic))
            throw new InvalidDataException("dsconfig.yaml missing 'linguistic' for pitch model");
        _pitchLinguisticSession?.Dispose();
        _pitchLinguisticSession = CreateSession(ReadModelBytes(dirPath, config.linguistic));

        // Pitch model
        if (string.IsNullOrEmpty(config.pitch))
            throw new InvalidDataException("dsconfig.yaml missing 'pitch' field");
        _pitchSession?.Dispose();
        _pitchSession = CreateSession(ReadModelBytes(dirPath, config.pitch));
    }

    /// <summary>Load the dsvariance model (linguistic + variance ONNX).</summary>
    public void LoadVarianceModel(string dirPath)
    {
        if (!Directory.Exists(dirPath))
            throw new DirectoryNotFoundException($"dsvariance directory not found: {dirPath}");

        _varianceDir = dirPath;
        var config = LoadConfig(dirPath);
        _varianceConfig = config;

        // Phoneme list — variance model may have its own
        var phonemesPath = Path.Combine(dirPath, config.phonemes);
        if (File.Exists(phonemesPath))
            _variancePhonemeTokens = DiffSingerUtils.LoadPhonemes(phonemesPath);

        // Variance linguistic model
        if (string.IsNullOrEmpty(config.linguistic))
            throw new InvalidDataException("dsconfig.yaml missing 'linguistic' for variance model");
        _varianceLinguisticSession?.Dispose();
        _varianceLinguisticSession = CreateSession(ReadModelBytes(dirPath, config.linguistic));

        // Variance model
        if (string.IsNullOrEmpty(config.variance))
            throw new InvalidDataException("dsconfig.yaml missing 'variance' field");
        _varianceSession?.Dispose();
        _varianceSession = new InferenceSession(File.ReadAllBytes(Path.Combine(dirPath, config.variance)));

        // Determine predicted variance params
        AvailableVarianceParams = VarianceParamTypes.GetPredictedParams(config);
    }

    /// <summary>
    /// Load G2P dictionary for a specific language from the dsdur directory.
    /// Looks for dsdict-{langCode}.yaml, falls back to dsdict.yaml.
    /// If fallback fails, returns null (caller can decide what to do).
    /// </summary>
    public bool LoadG2p(string langCode)
    {
        if (_durDir == string.Empty)
            throw new InvalidOperationException("Load dsdur model first before loading G2P.");

        var langDictPath = Path.Combine(_durDir, $"dsdict-{langCode}.yaml");
        if (File.Exists(langDictPath))
        {
            _g2p = G2pEngine.Load(langDictPath);
            return true;
        }

        var defaultDictPath = Path.Combine(_durDir, "dsdict.yaml");
        if (File.Exists(defaultDictPath))
        {
            _g2p = G2pEngine.Load(defaultDictPath);
            return true;
        }

        _g2p = null;
        return false;
    }

    /// <summary>Get available language codes from dsdict-*.yaml files.</summary>
    public string[] GetAvailableLanguages()
    {
        if (_durDir == string.Empty) return Array.Empty<string>();

        var languages = new List<string>();
        foreach (var file in Directory.GetFiles(_durDir, "dsdict-*.yaml"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var langCode = name.Substring("dsdict-".Length);
            if (!string.IsNullOrEmpty(langCode))
                languages.Add(langCode);
        }

        if (File.Exists(Path.Combine(_durDir, "dsdict.yaml")))
            languages.Insert(0, "default");

        return languages.ToArray();
    }

    // ================================================================
    //  Frame rate helpers
    // ================================================================

    public double GetFrameMs()
    {
        return _durConfig?.frameMs() ?? _pitchConfig?.frameMs() ?? _varianceConfig?.frameMs() ?? 10.0;
    }

    // ================================================================
    //  Inference: DURATION
    // ================================================================

    /// <summary>
    /// Run linguistic encoder + duration predictor.
    /// </summary>
    /// <param name="phonemeSequence">DS phoneme symbols (after G2P).</param>
    /// <param name="midiTones">One MIDI tone per phoneme.</param>
    /// <param name="noteEndTimesMs">End time in ms for each phoneme's parent note.</param>
    /// <param name="noteStartTimeMs">Start time in ms of the first note.</param>
    /// <param name="frameMs">Frame period from config.</param>
    public DurInferenceResult PredictDuration(
        string[] phonemeSequence,
        int[] midiTones,
        double[] noteEndTimesMs,
        double noteStartTimeMs,
        double frameMs,
        int steps = 20)
    {
        if (_durLinguisticSession == null || _durationSession == null || _durPhonemeTokens == null)
            throw new InvalidOperationException("dsdur model not loaded.");

        // 1. Tokenize phonemes (with SP padding)
        var paddedPhonemes = phonemeSequence.Prepend("SP").Append("SP").ToArray();
        var tokens = paddedPhonemes.Select(p =>
        {
            if (!_durPhonemeTokens.TryGetValue(p, out int token))
                throw new InvalidDataException($"Phoneme '{p}' not found in dur model phoneme list.");
            return (long)token;
        }).ToArray();

        int nPhonemes = phonemeSequence.Length;
        int nPadded = nPhonemes + 2;

        // 2. Build note duration frames for word encoding
        // Each phoneme belongs to a note; word boundaries are at note boundaries
        var wordDiv = new List<long>();
        var wordDur = new List<long>();
        int phIdx = 0;
        while (phIdx < nPhonemes)
        {
            int wordLen = 0;
            double noteEnd = noteEndTimesMs[phIdx];
            double wordEndMs = noteEnd;
            // Count phonemes belonging to the same note (same end time)
            while (phIdx < nPhonemes && Math.Abs(noteEndTimesMs[phIdx] - noteEnd) < 0.001)
            {
                wordLen++;
                phIdx++;
            }
            wordDiv.Add(wordLen + 1); // +1 for leading SP of this word
            // word duration in frames
            double wordStartMs = phIdx > 0 ? noteEndTimesMs[phIdx - wordLen - 1] : noteStartTimeMs;
            double wordDurMs = wordEndMs - wordStartMs;
            wordDur.Add(Math.Max(1, (long)Math.Round(wordDurMs / frameMs)));
        }

        // Add head SP as separate word
        wordDiv.Insert(0, 1);
        wordDur.Insert(0, Math.Max(1, (long)Math.Round(8 * frameMs / frameMs))); // head frames

        // Add tail SP as separate word
        wordDiv.Add(1);
        wordDur.Add(Math.Max(1, (long)Math.Round(8 * frameMs / frameMs))); // tail frames

        var wordDivArr = wordDiv.ToArray();
        var wordDurArr = wordDur.ToArray();

        // 3. Build linguistic encoder inputs
        var lingInputs = new List<NamedOnnxValue>();

        lingInputs.Add(NamedOnnxValue.CreateFromTensor("tokens",
            new DenseTensor<long>(tokens, new int[] { 1, nPadded })));

        if (_durConfig!.predict_dur)
        {
            // Word encoding mode
            lingInputs.Add(NamedOnnxValue.CreateFromTensor("word_div",
                new DenseTensor<long>(wordDivArr, new int[] { 1, wordDivArr.Length })));
            lingInputs.Add(NamedOnnxValue.CreateFromTensor("word_dur",
                new DenseTensor<long>(wordDurArr, new int[] { 1, wordDurArr.Length })));
        }
        else
        {
            // Phoneme encoding mode: ph_dur from note durations
            double prevMs = noteStartTimeMs;
            var phDurMs = new List<long>();
            for (int i = 0; i < nPhonemes; i++)
            {
                double durMs = noteEndTimesMs[i] - prevMs;
                phDurMs.Add(Math.Max(1, (long)Math.Round(durMs / frameMs)));
                prevMs = noteEndTimesMs[i];
            }
            // Add head/tail paddings
            phDurMs.Insert(0, 8); // head frames
            phDurMs.Add(8);       // tail frames
            var phDurArr = phDurMs.ToArray();

            lingInputs.Add(NamedOnnxValue.CreateFromTensor("ph_dur",
                new DenseTensor<long>(phDurArr, new int[] { 1, phDurArr.Length })));
        }

        // Language IDs
        if (_durConfig.use_lang_id && _durConfig.languages != null)
        {
            var langPath = Path.Combine(_durDir, _durConfig.languages);
            if (File.Exists(langPath))
            {
                var langIds = DiffSingerUtils.LoadLanguageIds(langPath);
                var langIdByPhone = paddedPhonemes
                    .Select(p => (long)langIds.GetValueOrDefault(DiffSingerUtils.PhonemeLanguage(p), 0))
                    .ToArray();
                lingInputs.Add(NamedOnnxValue.CreateFromTensor("languages",
                    new DenseTensor<long>(langIdByPhone, new int[] { 1, langIdByPhone.Length })));
            }
        }

        // 4. Run linguistic encoder
        var lingOutputs = _durLinguisticSession.Run(lingInputs).ToList();
        var encoderOut = lingOutputs.First(o => o.Name == "encoder_out").AsTensor<float>();
        var xMasks = lingOutputs.First(o => o.Name == "x_masks").AsTensor<bool>();

        // 5. Run duration predictor
        var durInputs = new List<NamedOnnxValue>();
        durInputs.Add(NamedOnnxValue.CreateFromTensor("encoder_out", encoderOut));
        durInputs.Add(NamedOnnxValue.CreateFromTensor("x_masks", xMasks));
        durInputs.Add(NamedOnnxValue.CreateFromTensor("ph_midi",
            new DenseTensor<long>(midiTones.Select(t => (long)t).ToArray(), new int[] { 1, nPhonemes })));

        var durOutputs = _durationSession.Run(durInputs).ToList();
        var durPred = durOutputs.First().AsTensor<float>();

        // 6. Parse output: dur_pred should have shape [1, nPhonemes, 1] or [1, nPhonemes]
        var durFrames = new float[nPhonemes];
        for (int i = 0; i < nPhonemes; i++)
        {
            int flatIdx = i; // Assume [1, nPhonemes]
            if (durPred.Length == nPhonemes * 1)
                flatIdx = i;
            else if (durPred.Length == nPhonemes)
                flatIdx = i;
            else
                flatIdx = i; // Try best effort
            durFrames[i] = durPred.ToArray()[flatIdx];
        }

        // Clamp minimum to 1 frame
        for (int i = 0; i < durFrames.Length; i++)
            durFrames[i] = Math.Max(1, durFrames[i]);

        var durationsMs = durFrames.Select(f => f * frameMs).ToArray();

        return new DurInferenceResult
        {
            PhonemeSymbols = phonemeSequence,
            DurationsMs = durationsMs,
            DurationsFrames = durFrames.Select(f => (int)Math.Round(f)).ToArray(),
            TotalFrames = (int)Math.Round(durFrames.Sum()),
            FrameMs = frameMs,
        };
    }

    // ================================================================
    //  Inference: PITCH
    // ================================================================

    /// <summary>
    /// Run linguistic encoder + pitch predictor.
    /// Returns per-frame pitch values (normalized cents: value * 0.01 gives semitones).
    /// </summary>
    public float[] PredictPitch(
        string[] phonemeSequence,
        int[] midiTones,
        int[] durationsFrames,
        int totalFrames,
        double frameMs,
        float expressiveness,
        double startTimeMs,
        int steps = 10)
    {
        if (_pitchLinguisticSession == null || _pitchSession == null)
            throw new InvalidOperationException("dspitch model not loaded.");

        int nPhonemes = phonemeSequence.Length;

        // Tokenize
        var tokens = phonemeSequence
            .Prepend("SP").Append("SP")
            .Select(p =>
            {
                if (_pitchPhonemeTokens != null && _pitchPhonemeTokens.TryGetValue(p, out int token))
                    return (long)token;
                if (_durPhonemeTokens != null && _durPhonemeTokens.TryGetValue(p, out int token2))
                    return (long)token2;
                return (long)0;
            }).ToArray();

        int nPadded = nPhonemes + 2;

        // Build ph_dur with head/tail padding
        var phDur = durationsFrames
            .Select(f => (long)f)
            .Prepend(8)  // head frames
            .Append(8)   // tail frames
            .ToArray();

        // Build linguistic inputs
        var lingInputs = new List<NamedOnnxValue>();
        lingInputs.Add(NamedOnnxValue.CreateFromTensor("tokens",
            new DenseTensor<long>(tokens, new int[] { 1, tokens.Length })));

        if (_pitchConfig!.predict_dur)
        {
            // Simplified word encoding — use note boundaries
            var wordDiv = new long[] { (long)(nPadded) };
            var wordDur = new long[] { phDur.Sum() };
            lingInputs.Add(NamedOnnxValue.CreateFromTensor("word_div",
                new DenseTensor<long>(wordDiv, new int[] { 1, wordDiv.Length })));
            lingInputs.Add(NamedOnnxValue.CreateFromTensor("word_dur",
                new DenseTensor<long>(wordDur, new int[] { 1, wordDur.Length })));
        }
        else
        {
            lingInputs.Add(NamedOnnxValue.CreateFromTensor("ph_dur",
                new DenseTensor<long>(phDur, new int[] { 1, phDur.Length })));
        }

        // Language IDs
        if (_pitchConfig.use_lang_id && _pitchConfig.languages != null)
        {
            var langPath = Path.Combine(_pitchDir, _pitchConfig.languages);
            if (File.Exists(langPath))
            {
                var langIds = DiffSingerUtils.LoadLanguageIds(langPath);
                var langIdByPhone = phonemeSequence
                    .Prepend("").Append("")
                    .Select(p => (long)langIds.GetValueOrDefault(DiffSingerUtils.PhonemeLanguage(p), 0))
                    .ToArray();
                lingInputs.Add(NamedOnnxValue.CreateFromTensor("languages",
                    new DenseTensor<long>(langIdByPhone, new int[] { 1, langIdByPhone.Length })));
            }
        }

        // Run linguistic
        var lingOutputs = _pitchLinguisticSession.Run(lingInputs).ToList();
        var encoderOut = lingOutputs.First(o => o.Name == "encoder_out").AsTensor<float>();

        // Build note-level info (simplified: one note per phoneme group)
        int nNotes = phonemeSequence.Length; // Simplified: 1 phoneme = 1 "note segment"
        var noteMidi = midiTones.Select(t => (float)t).ToArray();
        var noteDur = durationsFrames.Select(f => (long)f).ToArray();
        var noteRest = new bool[nNotes];

        // Build initial pitch (all zeros / target MIDI)
        var initPitch = new float[totalFrames];
        int cumulativeFrames = 0;
        for (int i = 0; i < nNotes && cumulativeFrames < totalFrames; i++)
        {
            int noteFrames = Math.Min(durationsFrames[i], totalFrames - cumulativeFrames);
            float midiFloat = midiTones[i] * 0.01f; // Normalize to match DS convention
            for (int f = 0; f < noteFrames; f++)
                initPitch[cumulativeFrames + f] = midiFloat;
            cumulativeFrames += noteFrames;
        }

        var retake = Enumerable.Repeat(true, totalFrames).ToArray();

        // Build pitch model inputs
        var pitchInputs = new List<NamedOnnxValue>();
        pitchInputs.Add(NamedOnnxValue.CreateFromTensor("encoder_out", encoderOut));
        pitchInputs.Add(NamedOnnxValue.CreateFromTensor("note_midi",
            new DenseTensor<float>(noteMidi, new int[] { 1, nNotes })));
        pitchInputs.Add(NamedOnnxValue.CreateFromTensor("note_dur",
            new DenseTensor<long>(noteDur, new int[] { 1, nNotes })));
        pitchInputs.Add(NamedOnnxValue.CreateFromTensor("ph_dur",
            new DenseTensor<long>(phDur, new int[] { 1, phDur.Length })));
        pitchInputs.Add(NamedOnnxValue.CreateFromTensor("pitch",
            new DenseTensor<float>(initPitch, new int[] { 1, totalFrames })));
        pitchInputs.Add(NamedOnnxValue.CreateFromTensor("retake",
            new DenseTensor<bool>(retake, new int[] { 1, totalFrames })));

        // Sampling steps (use the dedicated steps parameter, not expressiveness)
        if (_pitchConfig.useContinuousAcceleration)
        {
            pitchInputs.Add(NamedOnnxValue.CreateFromTensor("steps",
                new DenseTensor<long>(new long[] { Math.Max(1, steps) }, new int[] { 1 })));
        }
        else
        {
            int speedup = Math.Max(1, 1000 / Math.Max(1, steps));
            pitchInputs.Add(NamedOnnxValue.CreateFromTensor("speedup",
                new DenseTensor<long>(new long[] { speedup }, new int[] { 1 })));
        }

        // Expressiveness curve (expr)
        if (_pitchConfig.use_expr)
        {
            var exprCurve = Enumerable.Repeat(Math.Clamp(expressiveness / 100f, 0f, 1f), totalFrames).ToArray();
            pitchInputs.Add(NamedOnnxValue.CreateFromTensor("expr",
                new DenseTensor<float>(exprCurve, new int[] { 1, totalFrames })));
        }

        // Note rest
        if (_pitchConfig.use_note_rest)
        {
            pitchInputs.Add(NamedOnnxValue.CreateFromTensor("note_rest",
                new DenseTensor<bool>(noteRest, new int[] { 1, nNotes })));
        }

        // Run pitch model
        var pitchOutputs = _pitchSession.Run(pitchInputs).ToList();
        var pitchOut = pitchOutputs.First().AsTensor<float>().ToArray();

        return pitchOut;
    }

    // ================================================================
    //  Inference: VARIANCE
    // ================================================================

    /// <summary>
    /// Run linguistic encoder + variance predictor.
    /// Returns a dictionary of paramName → per-frame float values.
    /// </summary>
    public Dictionary<string, float[]> PredictVariance(
        string[] phonemeSequence,
        int[] midiTones,
        int[] durationsFrames,
        int totalFrames,
        double frameMs,
        float expressiveness,
        float[]? existingPitch = null,
        int steps = 20)
    {
        if (_varianceLinguisticSession == null || _varianceSession == null)
            throw new InvalidOperationException("dsvariance model not loaded.");

        int nPhonemes = phonemeSequence.Length;

        // Tokenize
        var tokens = phonemeSequence
            .Prepend("SP").Append("SP")
            .Select(p =>
            {
                if (_variancePhonemeTokens != null && _variancePhonemeTokens.TryGetValue(p, out int token))
                    return (long)token;
                if (_durPhonemeTokens != null && _durPhonemeTokens.TryGetValue(p, out int token2))
                    return (long)token2;
                return (long)0;
            }).ToArray();

        // Build ph_dur with padding
        var phDur = durationsFrames
            .Select(f => (long)f)
            .Prepend(8)
            .Append(8)
            .ToArray();

        // Linguistic encoder
        var lingInputs = new List<NamedOnnxValue>();
        lingInputs.Add(NamedOnnxValue.CreateFromTensor("tokens",
            new DenseTensor<long>(tokens, new int[] { 1, tokens.Length })));

        if (_varianceConfig!.predict_dur)
        {
            var wordDiv = new long[] { (long)(nPhonemes + 2) };
            var wordDur = new long[] { phDur.Sum() };
            lingInputs.Add(NamedOnnxValue.CreateFromTensor("word_div",
                new DenseTensor<long>(wordDiv, new int[] { 1, wordDiv.Length })));
            lingInputs.Add(NamedOnnxValue.CreateFromTensor("word_dur",
                new DenseTensor<long>(wordDur, new int[] { 1, wordDur.Length })));
        }
        else
        {
            lingInputs.Add(NamedOnnxValue.CreateFromTensor("ph_dur",
                new DenseTensor<long>(phDur, new int[] { 1, phDur.Length })));
        }

        if (_varianceConfig.use_lang_id && _varianceConfig.languages != null)
        {
            var langPath = Path.Combine(_varianceDir, _varianceConfig.languages);
            if (File.Exists(langPath))
            {
                var langIds = DiffSingerUtils.LoadLanguageIds(langPath);
                var langIdByPhone = phonemeSequence
                    .Prepend("").Append("")
                    .Select(p => (long)langIds.GetValueOrDefault(DiffSingerUtils.PhonemeLanguage(p), 0))
                    .ToArray();
                lingInputs.Add(NamedOnnxValue.CreateFromTensor("languages",
                    new DenseTensor<long>(langIdByPhone, new int[] { 1, langIdByPhone.Length })));
            }
        }

        var lingOutputs = _varianceLinguisticSession.Run(lingInputs).ToList();
        var encoderOut = lingOutputs.First(o => o.Name == "encoder_out").AsTensor<float>();

        // Build pitch input for variance model
        var pitch = existingPitch ?? new float[totalFrames];
        if (existingPitch == null)
        {
            int cumulativeFrames = 0;
            for (int i = 0; i < nPhonemes && cumulativeFrames < totalFrames; i++)
            {
                int noteFrames = Math.Min(durationsFrames[i], totalFrames - cumulativeFrames);
                float midiCents = midiTones[i] * 0.01f;
                for (int f = 0; f < noteFrames; f++)
                    pitch[cumulativeFrames + f] = midiCents;
                cumulativeFrames += noteFrames;
            }
        }

        // Build variance model inputs
        var varianceInputs = new List<NamedOnnxValue>();
        varianceInputs.Add(NamedOnnxValue.CreateFromTensor("encoder_out", encoderOut));
        varianceInputs.Add(NamedOnnxValue.CreateFromTensor("ph_dur",
            new DenseTensor<long>(phDur, new int[] { 1, phDur.Length })));
        varianceInputs.Add(NamedOnnxValue.CreateFromTensor("pitch",
            new DenseTensor<float>(pitch, new int[] { 1, totalFrames })));

        // Initialize variance inputs with zeros
        int numVariances = 0;
        if (_varianceConfig.predict_energy)
        {
            varianceInputs.Add(NamedOnnxValue.CreateFromTensor("energy",
                new DenseTensor<float>(new float[totalFrames], new int[] { 1, totalFrames })));
            numVariances++;
        }
        if (_varianceConfig.predict_breathiness)
        {
            varianceInputs.Add(NamedOnnxValue.CreateFromTensor("breathiness",
                new DenseTensor<float>(new float[totalFrames], new int[] { 1, totalFrames })));
            numVariances++;
        }
        if (_varianceConfig.predict_voicing)
        {
            varianceInputs.Add(NamedOnnxValue.CreateFromTensor("voicing",
                new DenseTensor<float>(new float[totalFrames], new int[] { 1, totalFrames })));
            numVariances++;
        }
        if (_varianceConfig.predict_tension)
        {
            varianceInputs.Add(NamedOnnxValue.CreateFromTensor("tension",
                new DenseTensor<float>(new float[totalFrames], new int[] { 1, totalFrames })));
            numVariances++;
        }

        // Retake mask
        var retake = Enumerable.Repeat(true, totalFrames * Math.Max(1, numVariances)).ToArray();
        varianceInputs.Add(NamedOnnxValue.CreateFromTensor("retake",
            new DenseTensor<bool>(retake, new int[] { 1, totalFrames, Math.Max(1, numVariances) })));

        // Steps / speedup (use the dedicated steps parameter, not expressiveness)
        if (_varianceConfig.useContinuousAcceleration)
        {
            varianceInputs.Add(NamedOnnxValue.CreateFromTensor("steps",
                new DenseTensor<long>(new long[] { Math.Max(1, steps) }, new int[] { 1 })));
        }
        else
        {
            int speedup = Math.Max(1, 1000 / Math.Max(1, steps));
            varianceInputs.Add(NamedOnnxValue.CreateFromTensor("speedup",
                new DenseTensor<long>(new long[] { speedup }, new int[] { 1 })));
        }

        // Run variance model
        var varianceOutputs = _varianceSession.Run(varianceInputs).ToList();

        var result = new Dictionary<string, float[]>();
        foreach (var output in varianceOutputs)
        {
            var name = output.Name;
            var tensor = output.AsTensor<float>();
            result[name] = tensor.ToArray();
        }

        return result;
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static DsConfig LoadConfig(string dirPath)
    {
        var configPath = Path.Combine(dirPath, "dsconfig.yaml");
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"dsconfig.yaml not found in {dirPath}");
        var yaml = File.ReadAllText(configPath);
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<DsConfig>(yaml);
    }
}

/// <summary>Result of duration inference.</summary>
public class DurInferenceResult
{
    public required string[] PhonemeSymbols { get; init; }
    public required double[] DurationsMs { get; init; }
    public required int[] DurationsFrames { get; init; }
    public required int TotalFrames { get; init; }
    public required double FrameMs { get; init; }
}

// ================================================================
//  Vocoder Config (loaded from vocoder.yaml)
// ================================================================

[Serializable]
public class DsVocoderConfig
{
    public string name = "vocoder";
    public string model = "model.onnx";
    public int sample_rate = 44100;
    public int hop_size = 512;
    public int win_size = 2048;
    public int fft_size = 2048;
    public int num_mel_bins = 128;
    public double mel_fmin = 40;
    public double mel_fmax = 16000;
    public string mel_base = "10";
    public string mel_scale = "slaney";
    public bool pitch_controllable = false;

    public float FrameMs() => 1000f * hop_size / sample_rate;
}

// ================================================================
//  Acoustic Model & Vocoder — extensions to DsModelSession
//  These methods are called from the session when SingerRootPath is configured.
// ================================================================

partial class DsModelSession
{
    // ---- Acoustic/Vocoder model resources ----
    private InferenceSession? _acousticSession;
    private InferenceSession? _vocoderSession;
    private DsVocoderConfig? _vocoderConfig;
    private Dictionary<string, int>? _acousticPhonemeTokens;
    private string _singerRootDir = string.Empty;
    private float[]? _speakerEmbed;
    private int _hiddenSize = 256;

    public bool IsAcousticLoaded => _acousticSession != null;
    public bool IsVocoderLoaded => _vocoderSession != null;
    public DsVocoderConfig? VocoderConfig => _vocoderConfig;

    public void LoadAcousticModel(string rootDir)
    {
        if (!Directory.Exists(rootDir))
            throw new DirectoryNotFoundException($"Singer root directory not found: {rootDir}");

        _singerRootDir = rootDir;
        var config = LoadConfig(rootDir);
        _hiddenSize = config.hiddenSize;

        if (string.IsNullOrEmpty(config.phonemes))
            throw new InvalidDataException("dsconfig.yaml missing 'phonemes' for acoustic model");
        var phonemesPath = Path.Combine(rootDir, config.phonemes);
        if (!File.Exists(phonemesPath))
            throw new FileNotFoundException($"Phoneme list not found: {phonemesPath}");
        _acousticPhonemeTokens = DiffSingerUtils.LoadPhonemes(phonemesPath);

        if (string.IsNullOrEmpty(config.acoustic))
            throw new InvalidDataException("dsconfig.yaml missing 'acoustic' field");
        _acousticSession?.Dispose();
        _acousticSession = CreateSession(ReadModelBytes(rootDir, config.acoustic));

        // Load speaker embedding (first speaker)
        if (config.speakers != null && config.speakers.Count > 0)
        {
            var embPath = Path.Combine(rootDir, config.speakers[0] + ".emb");
            if (File.Exists(embPath))
            {
                using var reader = new BinaryReader(File.OpenRead(embPath));
                var embed = new float[_hiddenSize];
                for (int i = 0; i < _hiddenSize && reader.BaseStream.Position < reader.BaseStream.Length; i++)
                    embed[i] = reader.ReadSingle();
                _speakerEmbed = embed;
            }
        }
    }

    public void LoadVocoder(string rootDir)
    {
        var config = LoadConfig(rootDir);
        if (string.IsNullOrEmpty(config.vocoder))
            throw new InvalidDataException("dsconfig.yaml missing 'vocoder' field");

        var vocoderDir = Path.Combine(rootDir, config.vocoder);
        if (!Directory.Exists(vocoderDir))
            throw new DirectoryNotFoundException($"Vocoder directory not found: {vocoderDir}");

        var vocoderConfigPath = Path.Combine(vocoderDir, "vocoder.yaml");
        if (!File.Exists(vocoderConfigPath))
            throw new FileNotFoundException($"vocoder.yaml not found: {vocoderConfigPath}");

        var yaml = File.ReadAllText(vocoderConfigPath);
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();
        _vocoderConfig = deserializer.Deserialize<DsVocoderConfig>(yaml);

        var modelPath = Path.Combine(vocoderDir, _vocoderConfig.model);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Vocoder model not found: {modelPath}");

        _vocoderSession?.Dispose();
        _vocoderSession = CreateSession(File.ReadAllBytes(modelPath));
    }

    public int AcousticPhonemeTokenize(string phoneme)
    {
        if (_acousticPhonemeTokens == null)
            throw new InvalidOperationException("Acoustic model not loaded.");
        return _acousticPhonemeTokens.TryGetValue(phoneme, out int token) ? token : 0;
    }

    public Tensor<float> PredictAcoustic(
        string[] phonemeSequence, int[] durationsFrames, float[] f0Hz,
        int totalFrames, int steps, float depth,
        Dictionary<string, float[]>? varianceParams = null)
    {
        if (_acousticSession == null || _acousticPhonemeTokens == null)
            throw new InvalidOperationException("Acoustic model not loaded.");

        var config = LoadConfig(_singerRootDir);
        int headFrames = 8, tailFrames = 8;

        var tokens = phonemeSequence
            .Prepend("SP").Append("SP")
            .Select(p => (long)AcousticPhonemeTokenize(p))
            .ToArray();

        var durations = durationsFrames
            .Select(f => (long)f)
            .Prepend(headFrames).Append(tailFrames)
            .ToArray();

        var inputs = new List<NamedOnnxValue>();
        inputs.Add(NamedOnnxValue.CreateFromTensor("tokens", new DenseTensor<long>(tokens, new[] { 1, tokens.Length })));
        inputs.Add(NamedOnnxValue.CreateFromTensor("durations", new DenseTensor<long>(durations, new[] { 1, durations.Length })));
        inputs.Add(NamedOnnxValue.CreateFromTensor("f0", new DenseTensor<float>(f0Hz, new[] { 1, totalFrames })));

        if (config.useContinuousAcceleration)
        {
            if (config.useVariableDepth)
                inputs.Add(NamedOnnxValue.CreateFromTensor("depth", new DenseTensor<float>(new[] { depth }, new[] { 1 })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("steps", new DenseTensor<long>(new[] { (long)Math.Max(1, steps) }, new[] { 1 })));
        }
        else
        {
            int speedup = Math.Max(1, 1000 / Math.Max(1, steps));
            inputs.Add(NamedOnnxValue.CreateFromTensor("speedup", new DenseTensor<long>(new[] { (long)speedup }, new[] { 1 })));
        }

        if (config.use_lang_id && !string.IsNullOrEmpty(config.languages))
        {
            var langIds = phonemeSequence.Select(p => (long)0).Prepend(0).Append(0).ToArray();
            inputs.Add(NamedOnnxValue.CreateFromTensor("languages", new DenseTensor<long>(langIds, new[] { 1, langIds.Length })));
        }

        if (config.speakers != null && config.speakers.Count > 0 && _speakerEmbed != null)
        {
            var spk = new float[totalFrames * _hiddenSize];
            for (int i = 0; i < totalFrames; i++)
                Array.Copy(_speakerEmbed, 0, spk, i * _hiddenSize, _hiddenSize);
            inputs.Add(NamedOnnxValue.CreateFromTensor("spk_embed", new DenseTensor<float>(spk, new[] { 1, totalFrames, _hiddenSize })));
        }

        if (varianceParams != null)
        {
            foreach (var (name, curve) in varianceParams)
            {
                if (curve != null && curve.Length > 0)
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<float>(curve.Take(totalFrames).ToArray(), new[] { 1, totalFrames })));
            }
        }

        var outputs = _acousticSession.Run(inputs).ToList();
        return outputs.First().AsTensor<float>();
    }

    public float[] PredictVocoder(Tensor<float> mel, float[] f0Hz)
    {
        if (_vocoderSession == null || _vocoderConfig == null)
            throw new InvalidOperationException("Vocoder not loaded.");

        var inputs = new List<NamedOnnxValue>();
        inputs.Add(NamedOnnxValue.CreateFromTensor("mel", mel));
        inputs.Add(NamedOnnxValue.CreateFromTensor("f0", new DenseTensor<float>(f0Hz, new[] { 1, f0Hz.Length })));

        var outputs = _vocoderSession.Run(inputs).ToList();
        var tensor = outputs.First().AsTensor<float>();
        var samples = new float[tensor.Length];
        for (int i = 0; i < tensor.Length; i++) samples[i] = tensor.ToArray()[i];
        return samples;
    }

    public static double ToneToFreq(double tone) => 440.0 * Math.Pow(2.0, (tone - 69.0) / 12.0);
}