using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PluginHost.Interop;

/// <summary>
/// Event arguments for parameter changes
/// </summary>
public class ParameterChangedEventArgs : EventArgs
{
    public int ParameterIndex { get; }
    public float NewValue { get; }

    public ParameterChangedEventArgs(int parameterIndex, float newValue)
    {
        ParameterIndex = parameterIndex;
        NewValue = newValue;
    }
}

/// <summary>
/// Event arguments for editor resize
/// </summary>
public class EditorResizedEventArgs : EventArgs
{
    public int Width { get; }
    public int Height { get; }

    public EditorResizedEventArgs(int width, int height)
    {
        Width = width;
        Height = height;
    }
}

/// <summary>
/// Represents a loaded plugin instance
/// </summary>
public sealed class PluginInstance : IDisposable
{
    private readonly IntPtr _handle;
    private readonly PluginHostManager _manager;
    private bool _disposed;
    private bool _isPrepared;

    // Keep callbacks alive
    private ParameterChangeCallback? _parameterCallback;
    private EditorResizeCallback? _editorResizeCallback;

    /// <summary>
    /// Event raised when a parameter value changes
    /// </summary>
    public event EventHandler<ParameterChangedEventArgs>? ParameterChanged;

    /// <summary>
    /// Event raised when the editor is resized
    /// </summary>
    public event EventHandler<EditorResizedEventArgs>? EditorResized;

    internal PluginInstance(IntPtr handle, PluginHostManager manager)
    {
        _handle = handle;
        _manager = manager;

        // Set up parameter change callback
        _parameterCallback = (instance, paramIndex, value, userData) =>
        {
            ParameterChanged?.Invoke(this, new ParameterChangedEventArgs(paramIndex, value));
        };
        NativeMethods.PluginHost_SetParameterChangeCallback(_handle, _parameterCallback, IntPtr.Zero);

        // Set up editor resize callback
        _editorResizeCallback = (instance, width, height, userData) =>
        {
            EditorResized?.Invoke(this, new EditorResizedEventArgs(width, height));
        };
        NativeMethods.PluginHost_SetEditorResizeCallback(_handle, _editorResizeCallback, IntPtr.Zero);
    }

    /// <summary>
    /// Get the native handle
    /// </summary>
    public IntPtr Handle => _handle;

    /// <summary>
    /// Get plugin information
    /// </summary>
    public PluginInfo GetInfo()
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_GetInstanceInfo(_handle, out var info);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to get plugin info", result);
        }

        return info;
    }

    /// <summary>
    /// Get the plugin name
    /// </summary>
    public string Name => GetInfo().Name;

    /// <summary>
    /// Get the plugin vendor
    /// </summary>
    public string Vendor => GetInfo().Vendor;

    /// <summary>
    /// Check if the plugin has an editor GUI
    /// </summary>
    public bool HasEditor => NativeMethods.PluginHost_HasEditor(_handle);

    /// <summary>
    /// Check if the plugin is a synthesizer
    /// </summary>
    public bool IsSynth => GetInfo().IsSynth;

    /// <summary>
    /// Get the number of input channels
    /// </summary>
    public int NumInputChannels => GetInfo().NumInputChannels;

    /// <summary>
    /// Get the number of output channels
    /// </summary>
    public int NumOutputChannels => GetInfo().NumOutputChannels;

    /// <summary>
    /// Get the plugin latency in samples
    /// </summary>
    public int Latency => NativeMethods.PluginHost_GetLatency(_handle);

    /// <summary>
    /// Get the tail time in seconds
    /// </summary>
    public double TailTime => NativeMethods.PluginHost_GetTailTime(_handle);

    // ========================================================================
    // Audio Configuration
    // ========================================================================

    /// <summary>
    /// Configure audio processing
    /// </summary>
    public void SetAudioConfig(double sampleRate, int blockSize, int numInputChannels, int numOutputChannels)
    {
        ThrowIfDisposed();

        var config = new AudioConfig
        {
            SampleRate = sampleRate,
            BlockSize = blockSize,
            NumInputChannels = numInputChannels,
            NumOutputChannels = numOutputChannels
        };

        var result = NativeMethods.PluginHost_SetAudioConfig(_handle, ref config);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to set audio config", result);
        }
    }

    /// <summary>
    /// Get the current audio configuration
    /// </summary>
    public AudioConfig GetAudioConfig()
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_GetAudioConfig(_handle, out var config);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to get audio config", result);
        }

        return config;
    }

    /// <summary>
    /// Prepare the plugin for playback
    /// </summary>
    public void PrepareToPlay()
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_PrepareToPlay(_handle);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to prepare plugin", result);
        }

        _isPrepared = true;
    }

    /// <summary>
    /// Release processing resources
    /// </summary>
    public void ReleaseResources()
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_ReleaseResources(_handle);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to release resources", result);
        }

        _isPrepared = false;
    }

    /// <summary>
    /// Reset the plugin state
    /// </summary>
    public void Reset()
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_Reset(_handle);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to reset plugin", result);
        }
    }

    // ========================================================================
    // Audio Processing
    // ========================================================================

    /// <summary>
    /// Process audio with interleaved buffers
    /// </summary>
    public unsafe void ProcessAudioInterleaved(float[] inputBuffer, float[] outputBuffer,
        int numInputChannels, int numOutputChannels, int numSamples)
    {
        ThrowIfDisposed();

        if (!_isPrepared)
        {
            throw new InvalidOperationException("Plugin must be prepared before processing");
        }

        fixed (float* inputPtr = inputBuffer)
        fixed (float* outputPtr = outputBuffer)
        {
            var result = NativeMethods.PluginHost_ProcessAudioInterleaved(
                _handle, inputPtr, outputPtr, numInputChannels, numOutputChannels, numSamples);

            if (result != PluginHostError.Ok)
            {
                throw new PluginHostException("Failed to process audio", result);
            }
        }
    }

    /// <summary>
    /// Process audio with interleaved buffers (Span version)
    /// </summary>
    public unsafe void ProcessAudioInterleaved(ReadOnlySpan<float> inputBuffer, Span<float> outputBuffer,
        int numInputChannels, int numOutputChannels, int numSamples)
    {
        ThrowIfDisposed();

        if (!_isPrepared)
        {
            throw new InvalidOperationException("Plugin must be prepared before processing");
        }

        fixed (float* inputPtr = inputBuffer)
        fixed (float* outputPtr = outputBuffer)
        {
            var result = NativeMethods.PluginHost_ProcessAudioInterleaved(
                _handle, inputPtr, outputPtr, numInputChannels, numOutputChannels, numSamples);

            if (result != PluginHostError.Ok)
            {
                throw new PluginHostException("Failed to process audio", result);
            }
        }
    }

    // ========================================================================
    // MIDI
    // ========================================================================

    /// <summary>
    /// Send MIDI events to the plugin
    /// </summary>
    public void SendMidiEvents(MidiEvent[] events)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_SendMidiEvents(_handle, events, events.Length);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to send MIDI events", result);
        }
    }

    /// <summary>
    /// Send a note on event
    /// </summary>
    public void SendNoteOn(int channel, int note, int velocity, int sampleOffset = 0)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_SendNoteOn(_handle, channel, note, velocity, sampleOffset);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to send note on", result);
        }
    }

    /// <summary>
    /// Send a note off event
    /// </summary>
    public void SendNoteOff(int channel, int note, int velocity = 0, int sampleOffset = 0)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_SendNoteOff(_handle, channel, note, velocity, sampleOffset);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to send note off", result);
        }
    }

    /// <summary>
    /// Send all notes off (panic)
    /// </summary>
    public void SendAllNotesOff()
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_SendAllNotesOff(_handle);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to send all notes off", result);
        }
    }

    /// <summary>
    /// Send a control change
    /// </summary>
    public void SendControlChange(int channel, int controller, int value, int sampleOffset = 0)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_SendControlChange(_handle, channel, controller, value, sampleOffset);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to send control change", result);
        }
    }

    /// <summary>
    /// Send a pitch bend
    /// </summary>
    public void SendPitchBend(int channel, int value, int sampleOffset = 0)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_SendPitchBend(_handle, channel, value, sampleOffset);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to send pitch bend", result);
        }
    }

    // ========================================================================
    // Parameters
    // ========================================================================

    /// <summary>
    /// Get the number of parameters
    /// </summary>
    public int ParameterCount => NativeMethods.PluginHost_GetParameterCount(_handle);

    /// <summary>
    /// Get parameter information
    /// </summary>
    public PluginParameterInfo GetParameterInfo(int index)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_GetParameterInfo(_handle, index, out var info);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to get parameter info", result);
        }

        return info;
    }

    /// <summary>
    /// Get a parameter value (normalized 0-1)
    /// </summary>
    public float GetParameter(int index)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_GetParameter(_handle, index, out var value);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to get parameter", result);
        }

        return value;
    }

    /// <summary>
    /// Set a parameter value (normalized 0-1)
    /// </summary>
    public void SetParameter(int index, float value)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_SetParameter(_handle, index, value);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to set parameter", result);
        }
    }

    /// <summary>
    /// Get parameter value as text
    /// </summary>
    public string GetParameterText(int index)
    {
        ThrowIfDisposed();

        var buffer = new StringBuilder(256);
        NativeMethods.PluginHost_GetParameterText(_handle, index, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    // ========================================================================
    // State / Presets
    // ========================================================================

    /// <summary>
    /// Get the plugin state as binary data
    /// </summary>
    public byte[] GetState()
    {
        ThrowIfDisposed();

        // First, get the required size
        int size = 0;
        var result = NativeMethods.PluginHost_GetState(_handle, null, ref size);
        if (result != PluginHostError.Ok && size == 0)
        {
            throw new PluginHostException("Failed to get state size", result);
        }

        // Allocate buffer and get state
        var data = new byte[size];
        result = NativeMethods.PluginHost_GetState(_handle, data, ref size);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to get state", result);
        }

        return data;
    }

    /// <summary>
    /// Set the plugin state from binary data
    /// </summary>
    public void SetState(byte[] data)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_SetState(_handle, data, data.Length);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to set state", result);
        }
    }

    /// <summary>
    /// Get the number of presets
    /// </summary>
    public int PresetCount => NativeMethods.PluginHost_GetPresetCount(_handle);

    /// <summary>
    /// Get the current preset index
    /// </summary>
    public int CurrentPreset
    {
        get => NativeMethods.PluginHost_GetCurrentPreset(_handle);
        set
        {
            ThrowIfDisposed();
            var result = NativeMethods.PluginHost_SetCurrentPreset(_handle, value);
            if (result != PluginHostError.Ok)
            {
                throw new PluginHostException("Failed to set preset", result);
            }
        }
    }

    /// <summary>
    /// Get a preset name
    /// </summary>
    public string GetPresetName(int index)
    {
        ThrowIfDisposed();

        var buffer = new StringBuilder(256);
        NativeMethods.PluginHost_GetPresetName(_handle, index, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    // ========================================================================
    // Editor
    // ========================================================================

    /// <summary>
    /// Open the plugin editor
    /// </summary>
    /// <param name="parentWindowHandle">Native parent window handle (HWND on Windows, NSView* on macOS)</param>
    /// <returns>Editor handle</returns>
    public IntPtr OpenEditor(IntPtr parentWindowHandle)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_OpenEditor(_handle, parentWindowHandle, out var editorHandle);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to open editor", result);
        }

        return editorHandle;
    }

    /// <summary>
    /// Close the plugin editor
    /// </summary>
    public void CloseEditor()
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_CloseEditor(_handle);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to close editor", result);
        }
    }

    /// <summary>
    /// Get the editor size
    /// </summary>
    public (int Width, int Height) GetEditorSize()
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_GetEditorSize(_handle, out var width, out var height);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException("Failed to get editor size", result);
        }

        return (width, height);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PluginInstance));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Release resources if prepared
        if (_isPrepared)
        {
            try
            {
                ReleaseResources();
            }
            catch
            {
                // Ignore errors during disposal
            }
        }

        // Unload the plugin
        NativeMethods.PluginHost_UnloadPlugin(_handle);
        _manager.UnregisterInstance(this);

        _disposed = true;
    }
}
