using System;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.PluginHost;

namespace TuneLab.Data;

/// <summary>
/// Interface for a plugin in a track's effect chain
/// </summary>
internal interface ITrackPlugin : IDataObject<TrackPluginInfo>, IDisposable
{
    /// <summary>
    /// Event fired when the plugin is loaded or unloaded
    /// </summary>
    IActionEvent PluginChanged { get; }
    
    /// <summary>
    /// The track this plugin belongs to
    /// </summary>
    ITrack Track { get; }
    
    /// <summary>
    /// The plugin unique identifier
    /// </summary>
    IDataProperty<string> PluginUid { get; }
    
    /// <summary>
    /// The plugin file path
    /// </summary>
    IDataProperty<string> PluginPath { get; }
    
    /// <summary>
    /// The plugin display name
    /// </summary>
    IDataProperty<string> Name { get; }
    
    /// <summary>
    /// Whether the plugin is bypassed
    /// </summary>
    IDataProperty<bool> Bypassed { get; }
    
    /// <summary>
    /// Whether a plugin is loaded
    /// </summary>
    bool IsLoaded { get; }
    
    /// <summary>
    /// Whether the plugin editor is currently open
    /// </summary>
    bool IsEditorOpen { get; }
    
    /// <summary>
    /// The loaded plugin instance (may be null if not loaded)
    /// </summary>
    PluginInstance? Plugin { get; }
    
    /// <summary>
    /// Load the plugin based on stored UID/Path
    /// </summary>
    bool LoadPlugin();
    
    /// <summary>
    /// Load a plugin from a file path
    /// </summary>
    bool LoadPlugin(string filePath);
    
    /// <summary>
    /// Load a plugin by UID
    /// </summary>
    bool LoadPluginByUid(string uid);
    
    /// <summary>
    /// Unload the plugin
    /// </summary>
    void UnloadPlugin();
    
    /// <summary>
    /// Open the plugin editor
    /// </summary>
    void OpenEditor(IntPtr parentWindow);
    
    /// <summary>
    /// Close the plugin editor
    /// </summary>
    void CloseEditor();
    
    /// <summary>
    /// Configure the plugin for audio processing
    /// </summary>
    void ConfigureAudio(double sampleRate, int blockSize, int numInputChannels, int numOutputChannels);
    
    /// <summary>
    /// Process audio through the plugin
    /// </summary>
    void ProcessAudio(float[] inputBuffer, float[] outputBuffer, int numChannels, int numSamples);
    
    /// <summary>
    /// Get the plugin latency in samples
    /// </summary>
    int GetLatency();
}
