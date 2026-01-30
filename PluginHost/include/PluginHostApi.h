/*
 * PluginHost - VST/VST3/AU Plugin Host Library
 * C API for interop with managed languages (C#, etc.)
 * 
 * Copyright (c) 2024 TuneLab
 */

#ifndef PLUGIN_HOST_API_H
#define PLUGIN_HOST_API_H

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

// Platform-specific export/import macros
#if defined(_WIN32) || defined(_WIN64)
    #ifdef PLUGIN_HOST_EXPORTS
        #define PLUGIN_HOST_API __declspec(dllexport)
    #else
        #define PLUGIN_HOST_API __declspec(dllimport)
    #endif
#else
    #define PLUGIN_HOST_API __attribute__((visibility("default")))
#endif

// Handle types (opaque pointers)
typedef void* PluginHostHandle;
typedef void* PluginInstanceHandle;
typedef void* PluginEditorHandle;

// ============================================================================
// Error codes
// ============================================================================
typedef enum {
    PLUGIN_HOST_OK = 0,
    PLUGIN_HOST_ERROR_INVALID_HANDLE = -1,
    PLUGIN_HOST_ERROR_PLUGIN_NOT_FOUND = -2,
    PLUGIN_HOST_ERROR_PLUGIN_LOAD_FAILED = -3,
    PLUGIN_HOST_ERROR_INVALID_FORMAT = -4,
    PLUGIN_HOST_ERROR_OUT_OF_MEMORY = -5,
    PLUGIN_HOST_ERROR_NOT_INITIALIZED = -6,
    PLUGIN_HOST_ERROR_ALREADY_INITIALIZED = -7,
    PLUGIN_HOST_ERROR_PARAMETER_NOT_FOUND = -8,
    PLUGIN_HOST_ERROR_INVALID_PARAMETER = -9,
    PLUGIN_HOST_ERROR_PROCESSING_FAILED = -10,
    PLUGIN_HOST_ERROR_UNKNOWN = -999
} PluginHostError;

// ============================================================================
// Plugin information structures
// ============================================================================
typedef enum {
    PLUGIN_TYPE_UNKNOWN = 0,
    PLUGIN_TYPE_VST2 = 1,
    PLUGIN_TYPE_VST3 = 2,
    PLUGIN_TYPE_AU = 3,
    PLUGIN_TYPE_LADSPA = 4
} PluginType;

typedef enum {
    PLUGIN_CATEGORY_UNKNOWN = 0,
    PLUGIN_CATEGORY_EFFECT = 1,
    PLUGIN_CATEGORY_INSTRUMENT = 2,
    PLUGIN_CATEGORY_ANALYZER = 3,
    PLUGIN_CATEGORY_GENERATOR = 4
} PluginCategory;

typedef struct {
    char name[256];
    char vendor[256];
    char version[64];
    char uid[128];
    char filePath[1024];
    PluginType type;
    PluginCategory category;
    int32_t numInputChannels;
    int32_t numOutputChannels;
    int32_t numParameters;
    bool hasEditor;
    bool acceptsMidi;
    bool producesMidi;
    bool isSynth;
} PluginInfo;

typedef struct {
    char name[256];
    char label[64];
    float defaultValue;
    float minValue;
    float maxValue;
    int32_t numSteps;
    bool isAutomatable;
    bool isDiscrete;
    bool isBoolean;
} PluginParameterInfo;

// ============================================================================
// Audio configuration
// ============================================================================
typedef struct {
    double sampleRate;
    int32_t blockSize;
    int32_t numInputChannels;
    int32_t numOutputChannels;
} AudioConfig;

// ============================================================================
// MIDI event structure
// ============================================================================
typedef struct {
    int32_t sampleOffset;    // Sample offset within the current block
    uint8_t status;          // MIDI status byte
    uint8_t data1;           // First data byte (note number, controller number, etc.)
    uint8_t data2;           // Second data byte (velocity, controller value, etc.)
    uint8_t channel;         // MIDI channel (0-15)
} MidiEvent;

// ============================================================================
// Callback function types
// ============================================================================
typedef void (*PluginScanProgressCallback)(const char* currentPath, int32_t found, int32_t total, void* userData);
typedef void (*PluginScanCompleteCallback)(int32_t totalFound, void* userData);
typedef void (*ParameterChangeCallback)(PluginInstanceHandle instance, int32_t paramIndex, float value, void* userData);
typedef void (*EditorResizeCallback)(PluginInstanceHandle instance, int32_t width, int32_t height, void* userData);

// ============================================================================
// Host Initialization and Shutdown
// ============================================================================

/**
 * Initialize the plugin host system
 * @return PLUGIN_HOST_OK on success, error code otherwise
 */
PLUGIN_HOST_API PluginHostError PluginHost_Initialize(void);

/**
 * Shutdown the plugin host system and release all resources
 */
PLUGIN_HOST_API void PluginHost_Shutdown(void);

/**
 * Check if the plugin host is initialized
 * @return true if initialized, false otherwise
 */
PLUGIN_HOST_API bool PluginHost_IsInitialized(void);

/**
 * Get the last error message
 * @param buffer Buffer to store the error message
 * @param bufferSize Size of the buffer
 * @return Length of the error message
 */
PLUGIN_HOST_API int32_t PluginHost_GetLastError(char* buffer, int32_t bufferSize);

// ============================================================================
// Plugin Scanning
// ============================================================================

/**
 * Add a directory to scan for plugins
 * @param path Directory path to add
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_AddScanPath(const char* path);

/**
 * Remove a directory from the scan paths
 * @param path Directory path to remove
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_RemoveScanPath(const char* path);

/**
 * Clear all scan paths
 */
PLUGIN_HOST_API void PluginHost_ClearScanPaths(void);

/**
 * Start scanning for plugins (asynchronous)
 * @param progressCallback Callback for progress updates (can be NULL)
 * @param completeCallback Callback when scan is complete (can be NULL)
 * @param userData User data passed to callbacks
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_StartScan(
    PluginScanProgressCallback progressCallback,
    PluginScanCompleteCallback completeCallback,
    void* userData
);

/**
 * Stop the current plugin scan
 */
PLUGIN_HOST_API void PluginHost_StopScan(void);

/**
 * Check if a scan is currently in progress
 * @return true if scanning, false otherwise
 */
PLUGIN_HOST_API bool PluginHost_IsScanning(void);

/**
 * Get the number of discovered plugins
 * @return Number of plugins
 */
PLUGIN_HOST_API int32_t PluginHost_GetPluginCount(void);

/**
 * Get plugin information by index
 * @param index Plugin index (0 to PluginHost_GetPluginCount() - 1)
 * @param info Pointer to PluginInfo structure to fill
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_GetPluginInfo(int32_t index, PluginInfo* info);

/**
 * Get plugin information by unique ID
 * @param uid Plugin unique identifier
 * @param info Pointer to PluginInfo structure to fill
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_GetPluginInfoByUid(const char* uid, PluginInfo* info);

// ============================================================================
// Plugin Instance Management
// ============================================================================

/**
 * Load a plugin from file path
 * @param filePath Path to the plugin file
 * @param handle Output handle for the loaded plugin
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_LoadPlugin(const char* filePath, PluginInstanceHandle* handle);

/**
 * Load a plugin by unique ID (must have been scanned first)
 * @param uid Plugin unique identifier
 * @param handle Output handle for the loaded plugin
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_LoadPluginByUid(const char* uid, PluginInstanceHandle* handle);

/**
 * Unload and release a plugin instance
 * @param handle Plugin instance handle
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_UnloadPlugin(PluginInstanceHandle handle);

/**
 * Get information about a loaded plugin instance
 * @param handle Plugin instance handle
 * @param info Pointer to PluginInfo structure to fill
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_GetInstanceInfo(PluginInstanceHandle handle, PluginInfo* info);

// ============================================================================
// Audio Processing Configuration
// ============================================================================

/**
 * Configure audio processing for a plugin instance
 * @param handle Plugin instance handle
 * @param config Audio configuration
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_SetAudioConfig(PluginInstanceHandle handle, const AudioConfig* config);

/**
 * Get the current audio configuration
 * @param handle Plugin instance handle
 * @param config Output audio configuration
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_GetAudioConfig(PluginInstanceHandle handle, AudioConfig* config);

/**
 * Prepare the plugin for processing (call before processing audio)
 * @param handle Plugin instance handle
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_PrepareToPlay(PluginInstanceHandle handle);

/**
 * Release processing resources (call when done processing)
 * @param handle Plugin instance handle
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_ReleaseResources(PluginInstanceHandle handle);

// ============================================================================
// Audio Processing
// ============================================================================

/**
 * Process audio through the plugin
 * @param handle Plugin instance handle
 * @param inputBuffers Array of input channel buffers (can be NULL for instruments)
 * @param outputBuffers Array of output channel buffers
 * @param numInputChannels Number of input channels
 * @param numOutputChannels Number of output channels
 * @param numSamples Number of samples to process
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_ProcessAudio(
    PluginInstanceHandle handle,
    const float** inputBuffers,
    float** outputBuffers,
    int32_t numInputChannels,
    int32_t numOutputChannels,
    int32_t numSamples
);

/**
 * Process audio with interleaved buffers
 * @param handle Plugin instance handle
 * @param inputBuffer Interleaved input buffer (can be NULL for instruments)
 * @param outputBuffer Interleaved output buffer
 * @param numInputChannels Number of input channels
 * @param numOutputChannels Number of output channels
 * @param numSamples Number of samples per channel
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_ProcessAudioInterleaved(
    PluginInstanceHandle handle,
    const float* inputBuffer,
    float* outputBuffer,
    int32_t numInputChannels,
    int32_t numOutputChannels,
    int32_t numSamples
);

/**
 * Reset the plugin's internal state (clear delay lines, etc.)
 * @param handle Plugin instance handle
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_Reset(PluginInstanceHandle handle);

// ============================================================================
// MIDI Processing
// ============================================================================

/**
 * Send MIDI events to the plugin
 * @param handle Plugin instance handle
 * @param events Array of MIDI events
 * @param numEvents Number of events
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_SendMidiEvents(
    PluginInstanceHandle handle,
    const MidiEvent* events,
    int32_t numEvents
);

/**
 * Send a single MIDI note on event
 * @param handle Plugin instance handle
 * @param channel MIDI channel (0-15)
 * @param note Note number (0-127)
 * @param velocity Velocity (0-127)
 * @param sampleOffset Sample offset within current block
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_SendNoteOn(
    PluginInstanceHandle handle,
    int32_t channel,
    int32_t note,
    int32_t velocity,
    int32_t sampleOffset
);

/**
 * Send a single MIDI note off event
 * @param handle Plugin instance handle
 * @param channel MIDI channel (0-15)
 * @param note Note number (0-127)
 * @param velocity Release velocity (0-127)
 * @param sampleOffset Sample offset within current block
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_SendNoteOff(
    PluginInstanceHandle handle,
    int32_t channel,
    int32_t note,
    int32_t velocity,
    int32_t sampleOffset
);

/**
 * Send all notes off (panic)
 * @param handle Plugin instance handle
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_SendAllNotesOff(PluginInstanceHandle handle);

/**
 * Send a MIDI control change
 * @param handle Plugin instance handle
 * @param channel MIDI channel (0-15)
 * @param controller Controller number (0-127)
 * @param value Controller value (0-127)
 * @param sampleOffset Sample offset within current block
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_SendControlChange(
    PluginInstanceHandle handle,
    int32_t channel,
    int32_t controller,
    int32_t value,
    int32_t sampleOffset
);

/**
 * Send a pitch bend message
 * @param handle Plugin instance handle
 * @param channel MIDI channel (0-15)
 * @param value Pitch bend value (0-16383, center = 8192)
 * @param sampleOffset Sample offset within current block
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_SendPitchBend(
    PluginInstanceHandle handle,
    int32_t channel,
    int32_t value,
    int32_t sampleOffset
);

// ============================================================================
// Parameter Management
// ============================================================================

/**
 * Get the number of parameters
 * @param handle Plugin instance handle
 * @return Number of parameters, or -1 on error
 */
PLUGIN_HOST_API int32_t PluginHost_GetParameterCount(PluginInstanceHandle handle);

/**
 * Get parameter information
 * @param handle Plugin instance handle
 * @param paramIndex Parameter index
 * @param info Output parameter information
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_GetParameterInfo(
    PluginInstanceHandle handle,
    int32_t paramIndex,
    PluginParameterInfo* info
);

/**
 * Get parameter value (normalized 0-1)
 * @param handle Plugin instance handle
 * @param paramIndex Parameter index
 * @param value Output parameter value
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_GetParameter(
    PluginInstanceHandle handle,
    int32_t paramIndex,
    float* value
);

/**
 * Set parameter value (normalized 0-1)
 * @param handle Plugin instance handle
 * @param paramIndex Parameter index
 * @param value Parameter value (0-1)
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_SetParameter(
    PluginInstanceHandle handle,
    int32_t paramIndex,
    float value
);

/**
 * Get parameter value as text
 * @param handle Plugin instance handle
 * @param paramIndex Parameter index
 * @param buffer Output buffer for text
 * @param bufferSize Buffer size
 * @return Length of text, or -1 on error
 */
PLUGIN_HOST_API int32_t PluginHost_GetParameterText(
    PluginInstanceHandle handle,
    int32_t paramIndex,
    char* buffer,
    int32_t bufferSize
);

/**
 * Set parameter change callback
 * @param handle Plugin instance handle
 * @param callback Callback function
 * @param userData User data
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_SetParameterChangeCallback(
    PluginInstanceHandle handle,
    ParameterChangeCallback callback,
    void* userData
);

// ============================================================================
// Plugin State (Presets)
// ============================================================================

/**
 * Get the plugin state as a binary blob
 * @param handle Plugin instance handle
 * @param data Output buffer (can be NULL to query size)
 * @param dataSize Pointer to size (input: buffer size, output: actual size)
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_GetState(
    PluginInstanceHandle handle,
    void* data,
    int32_t* dataSize
);

/**
 * Set the plugin state from a binary blob
 * @param handle Plugin instance handle
 * @param data State data
 * @param dataSize Size of state data
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_SetState(
    PluginInstanceHandle handle,
    const void* data,
    int32_t dataSize
);

/**
 * Get the number of presets/programs
 * @param handle Plugin instance handle
 * @return Number of presets, or -1 on error
 */
PLUGIN_HOST_API int32_t PluginHost_GetPresetCount(PluginInstanceHandle handle);

/**
 * Get the current preset index
 * @param handle Plugin instance handle
 * @return Current preset index, or -1 on error
 */
PLUGIN_HOST_API int32_t PluginHost_GetCurrentPreset(PluginInstanceHandle handle);

/**
 * Set the current preset
 * @param handle Plugin instance handle
 * @param presetIndex Preset index
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_SetCurrentPreset(
    PluginInstanceHandle handle,
    int32_t presetIndex
);

/**
 * Get preset name
 * @param handle Plugin instance handle
 * @param presetIndex Preset index
 * @param buffer Output buffer
 * @param bufferSize Buffer size
 * @return Length of name, or -1 on error
 */
PLUGIN_HOST_API int32_t PluginHost_GetPresetName(
    PluginInstanceHandle handle,
    int32_t presetIndex,
    char* buffer,
    int32_t bufferSize
);

// ============================================================================
// Plugin Editor (GUI)
// ============================================================================

/**
 * Check if the plugin has a GUI editor
 * @param handle Plugin instance handle
 * @return true if the plugin has an editor
 */
PLUGIN_HOST_API bool PluginHost_HasEditor(PluginInstanceHandle handle);

/**
 * Create and show the plugin editor
 * @param handle Plugin instance handle
 * @param parentWindow Native parent window handle (HWND on Windows, NSView* on macOS)
 * @param editorHandle Output editor handle
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_OpenEditor(
    PluginInstanceHandle handle,
    void* parentWindow,
    PluginEditorHandle* editorHandle
);

/**
 * Close and destroy the plugin editor
 * @param handle Plugin instance handle
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_CloseEditor(PluginInstanceHandle handle);

/**
 * Get the editor size
 * @param handle Plugin instance handle
 * @param width Output width
 * @param height Output height
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_GetEditorSize(
    PluginInstanceHandle handle,
    int32_t* width,
    int32_t* height
);

/**
 * Set editor resize callback
 * @param handle Plugin instance handle
 * @param callback Callback function
 * @param userData User data
 * @return PLUGIN_HOST_OK on success
 */
PLUGIN_HOST_API PluginHostError PluginHost_SetEditorResizeCallback(
    PluginInstanceHandle handle,
    EditorResizeCallback callback,
    void* userData
);

// ============================================================================
// Latency
// ============================================================================

/**
 * Get the plugin's latency in samples
 * @param handle Plugin instance handle
 * @return Latency in samples, or -1 on error
 */
PLUGIN_HOST_API int32_t PluginHost_GetLatency(PluginInstanceHandle handle);

// ============================================================================
// Tail Time
// ============================================================================

/**
 * Get the plugin's tail time in seconds (for effects with reverb, delay, etc.)
 * @param handle Plugin instance handle
 * @return Tail time in seconds, or -1 on error
 */
PLUGIN_HOST_API double PluginHost_GetTailTime(PluginInstanceHandle handle);

#ifdef __cplusplus
}
#endif

#endif // PLUGIN_HOST_API_H
