using TuneLab.Foundation;
using TuneLab.SDK;
using System.Text.Json;

namespace DiffSinger;

/// <summary>
/// Detected DiffSinger singer information.
/// </summary>
public record SingerDetectionInfo
{
    public required string Name { get; init; }
    public required string RootPath { get; init; }
    public string? DurPath { get; init; }
    public string? PitchPath { get; init; }
    public string? VariancePath { get; init; }
    public bool HasAcousticModel { get; init; }
    public List<string> Languages { get; init; } = new();
}

/// <summary>
/// DiffSinger voice engine for TuneLab.
/// Auto-scans configured singers directories for DiffSinger voicebanks during Init().
/// </summary>
[VoiceEngine("DiffSinger")]
public sealed class DiffSingerEngine : IVoiceEngine
{
    private readonly OrderedMap<string, VoiceSourceInfo> _sources = new();
    internal static readonly Dictionary<string, SingerDetectionInfo> DetectedSingers = new();

    // Plugin-level config (separate from session-level PluginConfig)
    internal static string PluginConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TuneLab", "DiffSinger", "plugin_config.json");

    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => _sources;

    public void Init()
    {
        // Write debug log to file
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuneLab", "DiffSinger", "init_debug.log");
        void Log(string msg) { try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); } catch { } }
        try { Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? ""); } catch { }

        Log($"=== DiffSinger Init() START ===");
        Log($"PluginConfigPath: {PluginConfigPath}");
        Log($"Assembly.Location: {System.Reflection.Assembly.GetExecutingAssembly().Location}");

        try
        {
            DetectedSingers.Clear();
            _sources.Clear();

            var scanDirs = new HashSet<string>();

            // 1. From saved config
            string savedDir = LoadSingersDirectory();
            Log($"LoadSingersDirectory() = '{savedDir}'");
            if (!string.IsNullOrEmpty(savedDir) && Directory.Exists(savedDir))
                scanDirs.Add(savedDir);

            // 2. Common directories
            string[] commonDirs = [
                @"D:\Program Files\OpenUTAU\Singers",
                @"D:\Program Files\OpenUtau\Singers",
                @"C:\Program Files\OpenUTAU\Singers",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuneLab", "Singers"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenUtau", "Singers"),
            ];
            foreach (var d in commonDirs)
            {
                if (Directory.Exists(d)) { scanDirs.Add(d); Log($"  Found: {d}"); }
                else Log($"  Not found: {d}");
            }

            // 3. Drive roots
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                var p = Path.Combine(drive.RootDirectory.FullName, "Program Files", "OpenUTAU", "Singers");
                if (Directory.Exists(p)) { scanDirs.Add(p); Log($"  Drive: {p}"); }
            }

            Log($"Total scan dirs: {scanDirs.Count}");
            bool foundAny = false;

            foreach (var scanDir in scanDirs)
            {
                Log($"Scanning: {scanDir}");
                if (!Directory.Exists(scanDir)) continue;
                foreach (var subDir in Directory.GetDirectories(scanDir))
                {
                    var name = Path.GetFileName(subDir);
                    Log($"  Subdir: {name}");
                    if (DetectedSingers.ContainsKey($"ds-{name}")) continue;
                    var info = DetectSinger(subDir);
                    if (info != null)
                    {
                        Log($"    DETECTED: {info.Name}");
                        var key = $"ds-{name}";
                        DetectedSingers[key] = info;
                        _sources.Add(key, new VoiceSourceInfo { Name = $"[DS] {info.Name}", Description = subDir });
                        foundAny = true;
                    }
                    else Log($"    Not a DS singer");
                }
            }

            Log($"FoundAny={foundAny}, Sources before manual={_sources.Count}");

            _sources.Add("ds-manual", new VoiceSourceInfo
            {
                Name = foundAny ? "Manual Config..." : "DiffSinger (Manual)",
                Description = "Manually configure model paths in the property panel"
            });

            if (!foundAny)
            {
                _sources.Insert(0, "ds-setup", new VoiceSourceInfo
                {
                    Name = "⚙ Set Singers Path",
                    Description = "Configure the DiffSinger voicebank directory in property panel"
                });
            }

            Log($"Final sources count={_sources.Count}");
            foreach (var s in _sources) Log($"  [{s.Key}] = {s.Value.Name}");
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            _sources.Clear();
            _sources.Add("ds-manual", new VoiceSourceInfo { Name = "DiffSinger (Manual)", Description = "Manual config" });
        }
        Log("=== DiffSinger Init() END ===");
    }

    public void Destroy() { }

    public ISynthesisSession CreateSession(string voiceId, ISynthesisContext context)
    {
        var session = new DiffSingerSession(context);

        // If a detected singer was selected, auto-configure paths
        if (DetectedSingers.TryGetValue(voiceId, out var info))
        {
            session.AutoConfigureFromSinger(info);
        }

        return session;
    }

    // ================================================================
    //  Singer Detection
    // ================================================================

    private static SingerDetectionInfo? DetectSinger(string dirPath)
    {
        var dirName = Path.GetFileName(dirPath);

        string? durPath = null;
        string? pitchPath = null;
        string? variancePath = null;
        bool hasAcoustic = false;
        var languages = new List<string>();

        var dsdurDir = Path.Combine(dirPath, "dsdur");
        var dspitchDir = Path.Combine(dirPath, "dspitch");
        var dsvarianceDir = Path.Combine(dirPath, "dsvariance");
        var rootConfig = Path.Combine(dirPath, "dsconfig.yaml");

        // Check dsdur
        if (Directory.Exists(dsdurDir) && File.Exists(Path.Combine(dsdurDir, "dsconfig.yaml")))
        {
            durPath = dsdurDir;
            // Detect languages from dsdict-*.yaml files
            foreach (var f in Directory.GetFiles(dsdurDir, "dsdict-*.yaml"))
            {
                var langCode = Path.GetFileNameWithoutExtension(f).Replace("dsdict-", "");
                if (!string.IsNullOrEmpty(langCode)) languages.Add(langCode);
            }
            if (File.Exists(Path.Combine(dsdurDir, "dsdict.yaml")))
                languages.Insert(0, "default");
        }

        // Check dspitch
        if (Directory.Exists(dspitchDir) && File.Exists(Path.Combine(dspitchDir, "dsconfig.yaml")))
            pitchPath = dspitchDir;

        // Check dsvariance
        if (Directory.Exists(dsvarianceDir) && File.Exists(Path.Combine(dsvarianceDir, "dsconfig.yaml")))
            variancePath = dsvarianceDir;

        // Check root dsconfig.yaml for acoustic model
        if (File.Exists(rootConfig))
        {
            try
            {
                var yaml = File.ReadAllText(rootConfig);
                var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                    .IgnoreUnmatchedProperties().Build();
                var config = deserializer.Deserialize<DsConfig>(yaml);
                hasAcoustic = !string.IsNullOrEmpty(config.acoustic);
            }
            catch { }
        }

        // Must have at least dsdur OR root acoustic model
        if (durPath == null && !hasAcoustic)
            return null;

        // Determine display name from character.txt first, then directory name
        string name = dirName;
        var charTxt = Path.Combine(dirPath, "character.txt");
        if (File.Exists(charTxt))
        {
            try
            {
                foreach (var line in File.ReadAllLines(charTxt))
                {
                    if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                    {
                        name = line.Substring("name=".Length).Trim();
                        break;
                    }
                }
            }
            catch { }
        }

        return new SingerDetectionInfo
        {
            Name = name,
            RootPath = dirPath,
            DurPath = durPath,
            PitchPath = pitchPath,
            VariancePath = variancePath,
            HasAcousticModel = hasAcoustic,
            Languages = languages,
        };
    }

    // ================================================================
    //  Plugin Config Persistence (singers directory)
    // ================================================================

    internal static string LoadSingersDirectory()
    {
        try
        {
            if (File.Exists(PluginConfigPath))
            {
                var json = File.ReadAllText(PluginConfigPath);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("singersDirectory", out var dir))
                {
                    var path = dir.GetString();
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        return path;
                }
            }
        }
        catch { }

        // Default: check common locations in priority order
        string[] candidates = [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuneLab", "Singers"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenUtau", "Singers"),
            @"C:\Program Files\OpenUTAU\Singers",
            @"D:\Program Files\OpenUTAU\Singers",
            @"D:\Program Files\OpenUtau\Singers",
        ];

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return string.Empty;
    }

    internal static void SaveSingersDirectory(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(PluginConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var config = new Dictionary<string, string> { { "singersDirectory", path } };
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PluginConfigPath, json);
        }
        catch { }
    }
}