using System;
using System.Runtime.InteropServices;

namespace TuneLab.PluginHost;

/// <summary>
/// Error codes returned by native functions
/// </summary>
public enum PluginHostError
{
    Ok = 0,
    InvalidHandle = -1,
    PluginNotFound = -2,
    PluginLoadFailed = -3,
    InvalidFormat = -4,
    OutOfMemory = -5,
    NotInitialized = -6,
    AlreadyInitialized = -7,
    ParameterNotFound = -8,
    InvalidParameter = -9,
    ProcessingFailed = -10,
    Unknown = -999
}

/// <summary>
/// Plugin type enumeration
/// </summary>
public enum PluginType
{
    Unknown = 0,
    Vst2 = 1,
    Vst3 = 2,
    AudioUnit = 3,
    Ladspa = 4
}

/// <summary>
/// Plugin category enumeration
/// </summary>
public enum PluginCategory
{
    Unknown = 0,
    Effect = 1,
    Instrument = 2,
    Analyzer = 3,
    Generator = 4
}

/// <summary>
/// Plugin information structure
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct PluginInfo
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Name;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Vendor;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string Version;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Uid;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
    public string FilePath;

    public PluginType Type;
    public PluginCategory Category;
    public int NumInputChannels;
    public int NumOutputChannels;
    public int NumParameters;

    [MarshalAs(UnmanagedType.I1)]
    public bool HasEditor;

    [MarshalAs(UnmanagedType.I1)]
    public bool AcceptsMidi;

    [MarshalAs(UnmanagedType.I1)]
    public bool ProducesMidi;

    [MarshalAs(UnmanagedType.I1)]
    public bool IsSynth;
}

/// <summary>
/// Plugin parameter information structure
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct PluginParameterInfo
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Name;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string Label;

    public float DefaultValue;
    public float MinValue;
    public float MaxValue;
    public int NumSteps;

    [MarshalAs(UnmanagedType.I1)]
    public bool IsAutomatable;

    [MarshalAs(UnmanagedType.I1)]
    public bool IsDiscrete;

    [MarshalAs(UnmanagedType.I1)]
    public bool IsBoolean;
}

/// <summary>
/// Audio configuration structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct AudioConfig
{
    public double SampleRate;
    public int BlockSize;
    public int NumInputChannels;
    public int NumOutputChannels;
}

/// <summary>
/// MIDI event structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MidiEvent
{
    public int SampleOffset;
    public byte Status;
    public byte Data1;
    public byte Data2;
    public byte Channel;
}

/// <summary>
/// Callback delegates
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void PluginScanProgressCallback(
    [MarshalAs(UnmanagedType.LPStr)] string currentPath,
    int found,
    int total,
    IntPtr userData);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void PluginScanCompleteCallback(int totalFound, IntPtr userData);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void ParameterChangeCallback(
    IntPtr instance,
    int paramIndex,
    float value,
    IntPtr userData);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void EditorResizeCallback(
    IntPtr instance,
    int width,
    int height,
    IntPtr userData);

/// <summary>
/// Native P/Invoke methods for the PluginHost library
/// </summary>
internal static class NativeMethods
{
    private const string LibraryName = "PluginHost";

    // ========================================================================
    // Host Initialization and Shutdown
    // ========================================================================

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_Initialize();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PluginHost_Shutdown();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool PluginHost_IsInitialized();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PluginHost_GetLastError(
        [MarshalAs(UnmanagedType.LPStr)] System.Text.StringBuilder buffer,
        int bufferSize);

    // ========================================================================
    // Plugin Scanning
    // ========================================================================

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern PluginHostError PluginHost_AddScanPath(
        [MarshalAs(UnmanagedType.LPStr)] string path);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern PluginHostError PluginHost_RemoveScanPath(
        [MarshalAs(UnmanagedType.LPStr)] string path);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PluginHost_ClearScanPaths();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_StartScan(
        PluginScanProgressCallback? progressCallback,
        PluginScanCompleteCallback? completeCallback,
        IntPtr userData);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PluginHost_StopScan();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool PluginHost_IsScanning();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PluginHost_GetPluginCount();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_GetPluginInfo(int index, out PluginInfo info);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern PluginHostError PluginHost_GetPluginInfoByUid(
        [MarshalAs(UnmanagedType.LPStr)] string uid,
        out PluginInfo info);

    // ========================================================================
    // Plugin Instance Management
    // ========================================================================

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern PluginHostError PluginHost_LoadPlugin(
        [MarshalAs(UnmanagedType.LPStr)] string filePath,
        out IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern PluginHostError PluginHost_LoadPluginByUid(
        [MarshalAs(UnmanagedType.LPStr)] string uid,
        out IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_UnloadPlugin(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_GetInstanceInfo(IntPtr handle, out PluginInfo info);

    // ========================================================================
    // Audio Processing Configuration
    // ========================================================================

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_SetAudioConfig(IntPtr handle, ref AudioConfig config);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_GetAudioConfig(IntPtr handle, out AudioConfig config);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_PrepareToPlay(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_ReleaseResources(IntPtr handle);

    // ========================================================================
    // Audio Processing
    // ========================================================================

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe PluginHostError PluginHost_ProcessAudio(
        IntPtr handle,
        float** inputBuffers,
        float** outputBuffers,
        int numInputChannels,
        int numOutputChannels,
        int numSamples);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe PluginHostError PluginHost_ProcessAudioInterleaved(
        IntPtr handle,
        float* inputBuffer,
        float* outputBuffer,
        int numInputChannels,
        int numOutputChannels,
        int numSamples);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_Reset(IntPtr handle);

    // ========================================================================
    // MIDI Processing
    // ========================================================================

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_SendMidiEvents(
        IntPtr handle,
        [In] MidiEvent[] events,
        int numEvents);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_SendNoteOn(
        IntPtr handle,
        int channel,
        int note,
        int velocity,
        int sampleOffset);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_SendNoteOff(
        IntPtr handle,
        int channel,
        int note,
        int velocity,
        int sampleOffset);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_SendAllNotesOff(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_SendControlChange(
        IntPtr handle,
        int channel,
        int controller,
        int value,
        int sampleOffset);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_SendPitchBend(
        IntPtr handle,
        int channel,
        int value,
        int sampleOffset);

    // ========================================================================
    // Parameter Management
    // ========================================================================

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PluginHost_GetParameterCount(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_GetParameterInfo(
        IntPtr handle,
        int paramIndex,
        out PluginParameterInfo info);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_GetParameter(
        IntPtr handle,
        int paramIndex,
        out float value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_SetParameter(
        IntPtr handle,
        int paramIndex,
        float value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PluginHost_GetParameterText(
        IntPtr handle,
        int paramIndex,
        [MarshalAs(UnmanagedType.LPStr)] System.Text.StringBuilder buffer,
        int bufferSize);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_SetParameterChangeCallback(
        IntPtr handle,
        ParameterChangeCallback? callback,
        IntPtr userData);

    // ========================================================================
    // Plugin State (Presets)
    // ========================================================================

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_GetState(
        IntPtr handle,
        [Out] byte[]? data,
        ref int dataSize);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_SetState(
        IntPtr handle,
        [In] byte[] data,
        int dataSize);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PluginHost_GetPresetCount(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PluginHost_GetCurrentPreset(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_SetCurrentPreset(IntPtr handle, int presetIndex);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PluginHost_GetPresetName(
        IntPtr handle,
        int presetIndex,
        [MarshalAs(UnmanagedType.LPStr)] System.Text.StringBuilder buffer,
        int bufferSize);

    // ========================================================================
    // Plugin Editor (GUI)
    // ========================================================================

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool PluginHost_HasEditor(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_OpenEditor(
        IntPtr handle,
        IntPtr parentWindow,
        out IntPtr editorHandle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_CloseEditor(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_GetEditorSize(
        IntPtr handle,
        out int width,
        out int height);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PluginHostError PluginHost_SetEditorResizeCallback(
        IntPtr handle,
        EditorResizeCallback? callback,
        IntPtr userData);

    // ========================================================================
    // Latency
    // ========================================================================

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PluginHost_GetLatency(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern double PluginHost_GetTailTime(IntPtr handle);
}
