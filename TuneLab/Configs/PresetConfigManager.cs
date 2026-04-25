using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using TuneLab.Base.Utils;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Configs;

internal static class PresetConfigManager
{
    static string StorageFilePath => Path.Combine(PathManager.ConfigsFolder, "Presets.json");

    public static List<PartPreset> LoadPresets()
    {
        try
        {
            return LoadPresets(throwOnError: false);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load presets: " + ex);
            return [];
        }
    }

    public static void SavePreset(PartPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Name))
            throw new ArgumentException("Preset name cannot be empty.", nameof(preset));

        PathManager.MakeSureExist(PathManager.ConfigsFolder);

        var presets = LoadPresets(throwOnError: true);
        var index = presets.FindIndex(item => item.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            presets[index] = preset;
        else
            presets.Add(preset);

        var json = new JArray(presets.Select(ToJson));
        File.WriteAllText(StorageFilePath, json.ToString(Formatting.Indented));
    }

    public static void DeletePreset(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            return;

        if (!File.Exists(StorageFilePath))
            return;

        var presets = LoadPresets(throwOnError: true);
        presets.RemoveAll(item => item.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));

        var json = new JArray(presets.Select(ToJson));
        File.WriteAllText(StorageFilePath, json.ToString(Formatting.Indented));
    }

    public static void RenamePreset(string oldPresetName, string newPresetName)
    {
        if (string.IsNullOrWhiteSpace(oldPresetName) || string.IsNullOrWhiteSpace(newPresetName))
            return;

        if (oldPresetName.Equals(newPresetName, StringComparison.OrdinalIgnoreCase))
            return;

        if (!File.Exists(StorageFilePath))
            return;

        var presets = LoadPresets(throwOnError: true);
        var sourceIndex = presets.FindIndex(item => item.Name.Equals(oldPresetName, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0)
            return;

        var targetIndex = presets.FindIndex(item => item.Name.Equals(newPresetName, StringComparison.OrdinalIgnoreCase));
        if (targetIndex >= 0)
        {
            presets.RemoveAt(targetIndex);
            if (targetIndex < sourceIndex)
                sourceIndex--;
        }

        presets[sourceIndex].Name = newPresetName;

        var json = new JArray(presets.Select(ToJson));
        File.WriteAllText(StorageFilePath, json.ToString(Formatting.Indented));
    }

    static List<PartPreset> LoadPresets(bool throwOnError)
    {
        if (!File.Exists(StorageFilePath))
            return [];

        try
        {
            var content = File.ReadAllText(StorageFilePath);
            if (string.IsNullOrWhiteSpace(content))
                return [];

            var root = JArray.Parse(content);
            return root
                .OfType<JObject>()
                .Select(FromJson)
                .Where(preset => !string.IsNullOrWhiteSpace(preset.Name))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to parse presets: " + ex);
            if (throwOnError)
                throw;

            return [];
        }
    }

    static JObject ToJson(PartPreset preset)
    {
        var automations = new JObject();
        foreach (var kvp in preset.Automations)
            automations.Add(kvp.Key, kvp.Value);

        return new JObject
        {
            ["name"] = preset.Name,
            ["voice"] = new JObject
            {
                ["type"] = preset.Voice.Type,
                ["id"] = preset.Voice.ID,
            },
            ["properties"] = ToJson(preset.Properties),
            ["automations"] = automations,
        };
    }

    static PartPreset FromJson(JObject json)
    {
        var preset = new PartPreset()
        {
            Name = json.Value<string>("name") ?? string.Empty,
            Voice = new VoiceInfo()
            {
                Type = json["voice"]?["type"]?.Value<string>() ?? string.Empty,
                ID = json["voice"]?["id"]?.Value<string>() ?? string.Empty,
            },
            Properties = json["properties"] is JObject properties ? FromPropertyObjectJson(properties) : PropertyObject.Empty,
        };

        if (json["automations"] is JObject automations)
        {
            foreach (var property in automations.Properties())
            {
                if (property.Value.Type == JTokenType.Integer || property.Value.Type == JTokenType.Float)
                    preset.Automations[property.Name] = property.Value.Value<double>();
            }
        }

        return preset;
    }

    static PropertyObject FromPropertyObjectJson(JObject json)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var property in json.Properties())
        {
            switch (property.Value.Type)
            {
                case JTokenType.Boolean:
                    map.Add(property.Name, property.Value.Value<bool>());
                    break;
                case JTokenType.Integer:
                case JTokenType.Float:
                    map.Add(property.Name, property.Value.Value<double>());
                    break;
                case JTokenType.String:
                    map.Add(property.Name, property.Value.Value<string>() ?? string.Empty);
                    break;
                case JTokenType.Object:
                    map.Add(property.Name, FromPropertyObjectJson((JObject)property.Value));
                    break;
            }
        }
        return new PropertyObject(map);
    }

    static JObject ToJson(PropertyObject properties)
    {
        var json = new JObject();
        foreach (var property in properties.Map)
        {
            if (property.Value.ToObject(out var propertyObject))
            {
                json.Add(property.Key, ToJson(propertyObject));
            }
            else if (property.Value.ToBool(out var boolValue))
            {
                json.Add(property.Key, boolValue);
            }
            else if (property.Value.ToDouble(out var doubleValue))
            {
                json.Add(property.Key, doubleValue);
            }
            else if (property.Value.ToString(out var stringValue))
            {
                json.Add(property.Key, stringValue);
            }
        }
        return json;
    }
}

internal class PartPreset
{
    public string Name { get; set; } = string.Empty;
    public VoiceInfo Voice { get; set; } = new();
    public PropertyObject Properties { get; set; } = PropertyObject.Empty;
    public Dictionary<string, double> Automations { get; set; } = [];
}
