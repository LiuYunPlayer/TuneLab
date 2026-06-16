using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Configs;

internal static class Settings
{
    public static readonly SettingsFile DefaultSettings = new();
    public static NotifiableProperty<string> Language { get; } = DefaultSettings.Language;
    public static NotifiableProperty<string> AutoScrollTarget { get; } = DefaultSettings.AutoScrollTarget;
    public static NotifiableProperty<double> MasterGain { get; } = DefaultSettings.MasterGain;
    public static NotifiableProperty<string> BackgroundImagePath { get; } = DefaultSettings.BackgroundImagePath;
    public static NotifiableProperty<double> BackgroundImageOpacity { get; } = DefaultSettings.BackgroundImageOpacity;
    public static NotifiableProperty<double> ParameterBoundaryExtension { get; } = DefaultSettings.ParameterBoundaryExtension;
    public static NotifiableProperty<bool> ParameterSyncMode { get; } = new(DefaultSettings.ParameterSyncMode);
    public static NotifiableProperty<string> PianoKeySamplesPath { get; } = DefaultSettings.PianoKeySamplesPath;
    public static NotifiableProperty<int> AutoSaveInterval { get; } = DefaultSettings.AutoSaveInterval;
    public static NotifiableProperty<int> AutoSaveMaxCount { get; } = DefaultSettings.AutoSaveMaxCount;
    public static NotifiableProperty<int> BufferSize { get; } = DefaultSettings.BufferSize;
    public static NotifiableProperty<int> MaxParallelSynthesisTasks { get; } = DefaultSettings.MaxParallelSynthesisTasks;
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
        AutoScrollTarget.Value = settingsFile.AutoScrollTarget;
        MasterGain.Value = settingsFile.MasterGain;
        BackgroundImagePath.Value = settingsFile.BackgroundImagePath;
        BackgroundImageOpacity.Value = settingsFile.BackgroundImageOpacity;
        ParameterBoundaryExtension.Value = settingsFile.ParameterBoundaryExtension;
        ParameterSyncMode.Value = settingsFile.ParameterSyncMode;
        PianoKeySamplesPath.Value = settingsFile.PianoKeySamplesPath;
        AutoSaveInterval.Value = settingsFile.AutoSaveInterval;
        AutoSaveMaxCount.Value = settingsFile.AutoSaveMaxCount;
        BufferSize.Value = settingsFile.BufferSize;
        MaxParallelSynthesisTasks.Value = settingsFile.MaxParallelSynthesisTasks;
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
                AutoScrollTarget = AutoScrollTarget,
                MasterGain = MasterGain,
                BackgroundImagePath = BackgroundImagePath,
                BackgroundImageOpacity = BackgroundImageOpacity,
                ParameterBoundaryExtension = ParameterBoundaryExtension,
                ParameterSyncMode = ParameterSyncMode.Value,
                PianoKeySamplesPath = PianoKeySamplesPath,
                AutoSaveInterval = AutoSaveInterval,
                AutoSaveMaxCount = AutoSaveMaxCount,
                BufferSize = BufferSize,
                MaxParallelSynthesisTasks = MaxParallelSynthesisTasks,
                SampleRate = SampleRate,
                AudioDriver = AudioDriver,
                AudioDevice = AudioDevice,
                TrackHueChangeRate = TrackHueChangeRate
            }, JsonSerializerOptions);

            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(path, content);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings: " + ex);
        }
    }

    static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions() { WriteIndented = true };
}
