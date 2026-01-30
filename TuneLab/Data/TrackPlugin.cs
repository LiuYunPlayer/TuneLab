using System;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.PluginHost;
using TuneLab.Base.Utils;

namespace TuneLab.Data;

/// <summary>
/// Represents a plugin in a track's effect chain
/// </summary>
internal class TrackPlugin : DataObject, ITrackPlugin
{
    /// <summary>
    /// Event fired when the plugin is loaded or unloaded
    /// </summary>
    public IActionEvent PluginChanged => mPluginChanged;
    
    /// <summary>
    /// The track this plugin belongs to
    /// </summary>
    public ITrack Track { get; }
    
    /// <summary>
    /// The plugin unique identifier
    /// </summary>
    public DataString PluginUid { get; }
    
    /// <summary>
    /// The plugin file path
    /// </summary>
    public DataString PluginPath { get; }
    
    /// <summary>
    /// The plugin display name
    /// </summary>
    public DataString Name { get; }
    
    /// <summary>
    /// Whether the plugin is bypassed
    /// </summary>
    public DataStruct<bool> Bypassed { get; }
    
    /// <summary>
    /// Plugin state data (Base64 encoded)
    /// </summary>
    public DataString StateData { get; }
    
    // Explicit interface implementations for ITrackPlugin
    IDataProperty<string> ITrackPlugin.PluginUid => PluginUid;
    IDataProperty<string> ITrackPlugin.PluginPath => PluginPath;
    IDataProperty<string> ITrackPlugin.Name => Name;
    IDataProperty<bool> ITrackPlugin.Bypassed => Bypassed;
    
    /// <summary>
    /// The loaded plugin instance (may be null if not loaded)
    /// </summary>
    public PluginInstance? Plugin => mPlugin;
    
    /// <summary>
    /// Whether a plugin is loaded
    /// </summary>
    public bool IsLoaded => mPlugin != null;
    
    /// <summary>
    /// Whether the plugin editor is currently open
    /// </summary>
    public bool IsEditorOpen { get; private set; }
    
    public TrackPlugin(ITrack track)
    {
        Track = track;
        PluginUid = new DataString(this, string.Empty);
        PluginPath = new DataString(this, string.Empty);
        Name = new DataString(this, string.Empty);
        Bypassed = new DataStruct<bool>(this);
        StateData = new DataString(this, string.Empty);
    }
    
    public TrackPlugin(ITrack track, TrackPluginInfo info) : this(track)
    {
        IDataObject<TrackPluginInfo>.SetInfo(this, info);
    }
    
    public TrackPluginInfo GetInfo()
    {
        // Update state data from plugin if loaded
        if (mPlugin != null)
        {
            try
            {
                var stateBytes = mPlugin.GetState();
                IDataObject<TrackPluginInfo>.SetInfo(StateData, Convert.ToBase64String(stateBytes));
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to get plugin state: {ex}");
            }
        }
        
        return new TrackPluginInfo
        {
            PluginUid = PluginUid.Value,
            PluginPath = PluginPath.Value,
            Name = Name.Value,
            Bypassed = Bypassed.Value,
            StateData = StateData.Value
        };
    }
    
    void IDataObject<TrackPluginInfo>.SetInfo(TrackPluginInfo info)
    {
        IDataObject<TrackPluginInfo>.SetInfo(PluginUid, info.PluginUid);
        IDataObject<TrackPluginInfo>.SetInfo(PluginPath, info.PluginPath);
        IDataObject<TrackPluginInfo>.SetInfo(Name, info.Name);
        IDataObject<TrackPluginInfo>.SetInfo(Bypassed, info.Bypassed);
        IDataObject<TrackPluginInfo>.SetInfo(StateData, info.StateData);
    }
    
    /// <summary>
    /// Load the plugin
    /// </summary>
    public bool LoadPlugin()
    {
        if (mPlugin != null) return true;
        
        try
        {
            var manager = PluginHostManager.Instance;
            if (!manager.IsInitialized) return false;
            
            // Try to load by UID first
            if (!string.IsNullOrEmpty(PluginUid.Value))
            {
                try
                {
                    mPlugin = manager.LoadPluginByUid(PluginUid.Value);
                }
                catch
                {
                    // UID lookup failed, try path
                    mPlugin = null;
                }
            }
            
            // Fallback to path
            if (mPlugin == null && !string.IsNullOrEmpty(PluginPath.Value))
            {
                try
                {
                    mPlugin = manager.LoadPlugin(PluginPath.Value);
                }
                catch
                {
                    mPlugin = null;
                }
            }
            
            if (mPlugin != null)
            {
                // Update name from plugin
                IDataObject<TrackPluginInfo>.SetInfo(Name, mPlugin.Name);
                
                // Restore state if we have state data
                if (!string.IsNullOrEmpty(StateData.Value))
                {
                    try
                    {
                        var stateBytes = Convert.FromBase64String(StateData.Value);
                        mPlugin.SetState(stateBytes);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to restore plugin state: {ex}");
                    }
                }
                
                mPluginChanged.Invoke();
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load plugin: {ex}");
        }
        
        return false;
    }
    
    /// <summary>
    /// Load a plugin from a file path
    /// </summary>
    public bool LoadPlugin(string filePath)
    {
        UnloadPlugin();
        
        try
        {
            var manager = PluginHostManager.Instance;
            if (!manager.IsInitialized) return false;
            
            mPlugin = manager.LoadPlugin(filePath);
            if (mPlugin != null)
            {
                IDataObject<TrackPluginInfo>.SetInfo(PluginPath, filePath);
                IDataObject<TrackPluginInfo>.SetInfo(PluginUid, mPlugin.GetInfo().Uid);
                IDataObject<TrackPluginInfo>.SetInfo(Name, mPlugin.Name);
                mPluginChanged.Invoke();
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load plugin from {filePath}: {ex}");
        }
        
        return false;
    }
    
    /// <summary>
    /// Load a plugin by UID
    /// </summary>
    public bool LoadPluginByUid(string uid)
    {
        UnloadPlugin();
        
        try
        {
            var manager = PluginHostManager.Instance;
            if (!manager.IsInitialized) return false;
            
            mPlugin = manager.LoadPluginByUid(uid);
            if (mPlugin != null)
            {
                IDataObject<TrackPluginInfo>.SetInfo(PluginUid, uid);
                IDataObject<TrackPluginInfo>.SetInfo(PluginPath, mPlugin.GetInfo().FilePath);
                IDataObject<TrackPluginInfo>.SetInfo(Name, mPlugin.Name);
                mPluginChanged.Invoke();
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load plugin by UID {uid}: {ex}");
        }
        
        return false;
    }
    
    /// <summary>
    /// Unload the plugin
    /// </summary>
    public void UnloadPlugin()
    {
        if (mPlugin == null) return;
        
        // Save state before unloading
        try
        {
            var stateBytes = mPlugin.GetState();
            IDataObject<TrackPluginInfo>.SetInfo(StateData, Convert.ToBase64String(stateBytes));
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save plugin state: {ex}");
        }
        
        CloseEditor();
        mPlugin.Dispose();
        mPlugin = null;
        mPluginChanged.Invoke();
    }
    
    /// <summary>
    /// Open the plugin editor
    /// </summary>
    public void OpenEditor(IntPtr parentWindow)
    {
        if (mPlugin == null || !mPlugin.HasEditor) return;
        
        try
        {
            mPlugin.OpenEditor(parentWindow);
            IsEditorOpen = true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open plugin editor: {ex}");
        }
    }
    
    /// <summary>
    /// Close the plugin editor
    /// </summary>
    public void CloseEditor()
    {
        if (mPlugin == null || !IsEditorOpen) return;
        
        try
        {
            mPlugin.CloseEditor();
            IsEditorOpen = false;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to close plugin editor: {ex}");
        }
    }
    
    /// <summary>
    /// Configure the plugin for audio processing
    /// </summary>
    public void ConfigureAudio(double sampleRate, int blockSize, int numInputChannels, int numOutputChannels)
    {
        if (mPlugin == null) return;
        
        try
        {
            mPlugin.SetAudioConfig(sampleRate, blockSize, numInputChannels, numOutputChannels);
            mPlugin.PrepareToPlay();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to configure plugin audio: {ex}");
        }
    }
    
    /// <summary>
    /// Process audio through the plugin
    /// </summary>
    public void ProcessAudio(float[] inputBuffer, float[] outputBuffer, int numChannels, int numSamples)
    {
        if (mPlugin == null || Bypassed.Value)
        {
            // Bypass: copy input to output
            if (inputBuffer != outputBuffer)
            {
                Array.Copy(inputBuffer, outputBuffer, Math.Min(inputBuffer.Length, outputBuffer.Length));
            }
            return;
        }
        
        try
        {
            mPlugin.ProcessAudioInterleaved(inputBuffer, outputBuffer, numChannels, numChannels, numSamples);
        }
        catch (Exception ex)
        {
            Log.Error($"Plugin processing error: {ex}");
            // On error, pass through
            if (inputBuffer != outputBuffer)
            {
                Array.Copy(inputBuffer, outputBuffer, Math.Min(inputBuffer.Length, outputBuffer.Length));
            }
        }
    }
    
    /// <summary>
    /// Get the plugin latency in samples
    /// </summary>
    public int GetLatency()
    {
        if (mPlugin == null || Bypassed.Value) return 0;
        return mPlugin.Latency;
    }
    
    public void Dispose()
    {
        UnloadPlugin();
    }
    
    private PluginInstance? mPlugin;
    private readonly ActionEvent mPluginChanged = new();
}
