using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Utils;

namespace TuneLab.Configs;

internal static class EditorState
{
    public static readonly EditorStateFile Defaults = new();
    public static NotifiableProperty<int> MainWindowX { get; } = new(Defaults.MainWindowX);
    public static NotifiableProperty<int> MainWindowY { get; } = new(Defaults.MainWindowY);
    public static NotifiableProperty<double> MainWindowWidth { get; } = new(Defaults.MainWindowWidth);
    public static NotifiableProperty<double> MainWindowHeight { get; } = new(Defaults.MainWindowHeight);
    public static NotifiableProperty<bool> MainWindowMaximized { get; } = new(Defaults.MainWindowMaximized);
    public static NotifiableProperty<double> TrackWindowHeight { get; } = new(Defaults.TrackWindowHeight);
    public static NotifiableProperty<double> ParameterPanelHeight { get; } = new(Defaults.ParameterPanelHeight);
    public static NotifiableProperty<double> ParameterPanelHeightNormal { get; } = new(Defaults.ParameterPanelHeightNormal);
    public static NotifiableProperty<double> ParameterPanelHeightMaximized { get; } = new(Defaults.ParameterPanelHeightMaximized);

    public static void Init(string path)
    {
        EditorStateFile? stateFile = null;
        if (File.Exists(path))
        {
            try
            {
                stateFile = JsonSerializer.Deserialize<EditorStateFile>(File.OpenRead(path));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to deserialize editor state: " + ex);
            }
        }

        stateFile ??= Defaults;

        MainWindowX.Value = stateFile.MainWindowX;
        MainWindowY.Value = stateFile.MainWindowY;
        MainWindowWidth.Value = stateFile.MainWindowWidth;
        MainWindowHeight.Value = stateFile.MainWindowHeight;
        MainWindowMaximized.Value = stateFile.MainWindowMaximized;
        TrackWindowHeight.Value = stateFile.TrackWindowHeight;
        ParameterPanelHeight.Value = stateFile.ParameterPanelHeight;
        ParameterPanelHeightNormal.Value = stateFile.ParameterPanelHeightNormal;
        ParameterPanelHeightMaximized.Value = stateFile.ParameterPanelHeightMaximized;
        if (stateFile.ParameterPanelHeight != Defaults.ParameterPanelHeight &&
            stateFile.ParameterPanelHeightNormal == Defaults.ParameterPanelHeightNormal &&
            stateFile.ParameterPanelHeightMaximized == Defaults.ParameterPanelHeightMaximized)
        {
            ParameterPanelHeightNormal.Value = stateFile.ParameterPanelHeight;
            ParameterPanelHeightMaximized.Value = stateFile.ParameterPanelHeight;
        }
    }

    public static void Save(string path)
    {
        try
        {
            var content = JsonSerializer.Serialize(new EditorStateFile()
            {
                MainWindowX = MainWindowX,
                MainWindowY = MainWindowY,
                MainWindowWidth = MainWindowWidth,
                MainWindowHeight = MainWindowHeight,
                MainWindowMaximized = MainWindowMaximized.Value,
                TrackWindowHeight = TrackWindowHeight,
                ParameterPanelHeight = ParameterPanelHeight,
                ParameterPanelHeightNormal = ParameterPanelHeightNormal,
                ParameterPanelHeightMaximized = ParameterPanelHeightMaximized,
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
            Log.Error("Failed to save editor state: " + ex);
        }
    }

    static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions() { WriteIndented = true };
}
