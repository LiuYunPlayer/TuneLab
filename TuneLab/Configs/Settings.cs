using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Base.Event;

namespace TuneLab.Configs;

internal static class Settings
{
    static readonly SettingsFile DefaultSettings = new();
    public static NotifiableProperty<string> Language { get; } = DefaultSettings.Language;
    public static NotifiableProperty<string> BackgroundPath { get; } = DefaultSettings.BackgroudPath;
    public static NotifiableProperty<double> ParameterExtend { get; } = DefaultSettings.ParameterExtend;
    public static NotifiableProperty<string> KeySamplesPath { get; } = DefaultSettings.KeySamplesPath;
    
    public static void Init(string path)
    {
        SettingsFile? settingsFile = null;
        if (File.Exists(path))
        {
            settingsFile = JsonSerializer.Deserialize<SettingsFile>(File.OpenRead(path));
        }

        settingsFile ??= DefaultSettings;

        Language.Value = settingsFile.Language;
        BackgroundPath.Value = settingsFile.BackgroudPath;
        ParameterExtend.Value = settingsFile.ParameterExtend;
        KeySamplesPath.Value = settingsFile.KeySamplesPath;
    }

    public static void Save(string path)
    {
        var content = JsonSerializer.Serialize(new SettingsFile()
        {
            Language = Language,
            BackgroudPath = BackgroundPath,
            ParameterExtend = ParameterExtend,
            KeySamplesPath = KeySamplesPath,
        });

        File.WriteAllText(path, content);
    }
}
