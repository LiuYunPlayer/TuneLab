using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Base.Utils;

namespace TuneLab.Configs;

internal static class Settings
{
    public static readonly SettingsFile DefaultSettings = new();
    public static NotifiableProperty<string> Language { get; } = DefaultSettings.Language;
    public static NotifiableProperty<double> MasterGain { get; } = DefaultSettings.MasterGain;
    public static NotifiableProperty<string> BackgroundImagePath { get; } = DefaultSettings.BackgroundImagePath;
    public static NotifiableProperty<double> BackgroundImageOpacity { get; } = DefaultSettings.BackgroundImageOpacity;
    public static NotifiableProperty<double> ParameterBoundaryExtension { get; } = DefaultSettings.ParameterBoundaryExtension;
    public static NotifiableProperty<bool> PitchSyncMode { get; } = new(DefaultSettings.PitchSyncMode);
    public static NotifiableProperty<string> PianoKeySamplesPath { get; } = DefaultSettings.PianoKeySamplesPath;
    public static NotifiableProperty<int> AutoSaveInterval { get; } = DefaultSettings.AutoSaveInterval;
    public static NotifiableProperty<int> BufferSize { get; } = DefaultSettings.BufferSize;
    public static NotifiableProperty<int> SampleRate { get; } = DefaultSettings.SampleRate;
    public static NotifiableProperty<string> AudioDriver { get; } = DefaultSettings.AudioDriver;
    public static NotifiableProperty<string> AudioDevice { get; } = DefaultSettings.AudioDevice;
    public static NotifiableProperty<double> TrackHueChangeRate { get; } = DefaultSettings.TrackHueChangeRate;
    
    public static void Init(string path)
    {
        SettingsFile? settingsFile = null;
        if (File.Exists(path))
        {
            try
            {
                settingsFile = JsonSerializer.Deserialize<SettingsFile>(File.OpenRead(path));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to deserialize settings: " + ex);
            }
        }

        settingsFile ??= DefaultSettings;

        Language.Value = settingsFile.Language;
        MasterGain.Value = settingsFile.MasterGain;
        BackgroundImagePath.Value = settingsFile.BackgroundImagePath;
        BackgroundImageOpacity.Value = settingsFile.BackgroundImageOpacity;
        ParameterBoundaryExtension.Value = settingsFile.ParameterBoundaryExtension;
        PitchSyncMode.Value = settingsFile.PitchSyncMode;
        PianoKeySamplesPath.Value = settingsFile.PianoKeySamplesPath;
        AutoSaveInterval.Value = settingsFile.AutoSaveInterval;
        BufferSize.Value = settingsFile.BufferSize;
        SampleRate.Value = settingsFile.SampleRate;
        AudioDriver.Value = settingsFile.AudioDriver;
        AudioDevice.Value = settingsFile.AudioDevice;
        TrackHueChangeRate.Value = settingsFile.TrackHueChangeRate;
    }

    public static void Save(string path)
    {
        try
        {
            var content = JsonSerializer.Serialize(new SettingsFile()
            {
                Language = Language,
                MasterGain = MasterGain,
                BackgroundImagePath = BackgroundImagePath,
                BackgroundImageOpacity = BackgroundImageOpacity,
                ParameterBoundaryExtension = ParameterBoundaryExtension,
                PitchSyncMode = PitchSyncMode.Value,
                PianoKeySamplesPath = PianoKeySamplesPath,
                AutoSaveInterval = AutoSaveInterval,
                BufferSize = BufferSize,
                SampleRate = SampleRate,
                AudioDriver = AudioDriver,
                AudioDevice = AudioDevice,
                TrackHueChangeRate = TrackHueChangeRate
            }, JsonSerializerOptions);

            File.WriteAllText(path, content);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings: " + ex);
        }
    }

    static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions() { WriteIndented = true };
}
