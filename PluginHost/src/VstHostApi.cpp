/*
 * VstHostApi.cpp - C API implementation for interop
 */

#include "PluginHostApi.h"
#include "VstHost.h"
#include "PluginInstance.h"
#include <unordered_map>
#include <mutex>
#include <cstring>
#include <algorithm>

using namespace PluginHost;

// ============================================================================
// Internal State
// ============================================================================

namespace
{
    std::mutex g_instanceMapMutex;
    std::unordered_map<PluginInstanceHandle, std::shared_ptr<PluginInstance>> g_instanceMap;
    int64_t g_nextHandle = 1;
    
    std::mutex g_errorMutex;
    std::string g_lastError;
    
    // Store callbacks with user data
    struct ScanCallbackData
    {
        PluginScanProgressCallback progressCallback = nullptr;
        PluginScanCompleteCallback completeCallback = nullptr;
        void* userData = nullptr;
    };
    
    ScanCallbackData g_scanCallbacks;
    
    void setError(const std::string& error)
    {
        std::lock_guard<std::mutex> lock(g_errorMutex);
        g_lastError = error;
    }
    
    PluginInstanceHandle registerInstance(std::shared_ptr<PluginInstance> instance)
    {
        std::lock_guard<std::mutex> lock(g_instanceMapMutex);
        PluginInstanceHandle handle = reinterpret_cast<PluginInstanceHandle>(g_nextHandle++);
        g_instanceMap[handle] = instance;
        return handle;
    }
    
    std::shared_ptr<PluginInstance> getInstance(PluginInstanceHandle handle)
    {
        std::lock_guard<std::mutex> lock(g_instanceMapMutex);
        auto it = g_instanceMap.find(handle);
        if (it != g_instanceMap.end())
            return it->second;
        return nullptr;
    }
    
    bool removeInstance(PluginInstanceHandle handle)
    {
        std::lock_guard<std::mutex> lock(g_instanceMapMutex);
        return g_instanceMap.erase(handle) > 0;
    }
    
    void copyStringToBuffer(const std::string& src, char* dest, size_t destSize)
    {
        if (dest && destSize > 0)
        {
            size_t copyLen = std::min(src.length(), destSize - 1);
            std::memcpy(dest, src.c_str(), copyLen);
            dest[copyLen] = '\0';
        }
    }
    
    void fillPluginInfo(const PluginDescription& desc, PluginInfo* info)
    {
        if (!info) return;
        
        copyStringToBuffer(desc.name, info->name, sizeof(info->name));
        copyStringToBuffer(desc.vendor, info->vendor, sizeof(info->vendor));
        copyStringToBuffer(desc.version, info->version, sizeof(info->version));
        copyStringToBuffer(desc.uid, info->uid, sizeof(info->uid));
        copyStringToBuffer(desc.filePath, info->filePath, sizeof(info->filePath));
        
        info->type = static_cast<PluginType>(desc.type);
        info->category = static_cast<PluginCategory>(desc.category);
        info->numInputChannels = desc.numInputChannels;
        info->numOutputChannels = desc.numOutputChannels;
        info->numParameters = desc.numParameters;
        info->hasEditor = desc.hasEditor;
        info->acceptsMidi = desc.acceptsMidi;
        info->producesMidi = desc.producesMidi;
        info->isSynth = desc.isSynth;
    }
}

// ============================================================================
// Host Initialization and Shutdown
// ============================================================================

PLUGIN_HOST_API PluginHostError PluginHost_Initialize(void)
{
    VstHost& host = VstHost::getInstance();
    
    if (host.isInitialized())
    {
        setError("Plugin host already initialized");
        return PLUGIN_HOST_ERROR_ALREADY_INITIALIZED;
    }
    
    if (!host.initialize())
    {
        setError(host.getLastError());
        return PLUGIN_HOST_ERROR_NOT_INITIALIZED;
    }
    
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API void PluginHost_Shutdown(void)
{
    // Clear all instances first
    {
        std::lock_guard<std::mutex> lock(g_instanceMapMutex);
        g_instanceMap.clear();
    }
    
    VstHost::getInstance().shutdown();
}

PLUGIN_HOST_API bool PluginHost_IsInitialized(void)
{
    return VstHost::getInstance().isInitialized();
}

PLUGIN_HOST_API int32_t PluginHost_GetLastError(char* buffer, int32_t bufferSize)
{
    std::lock_guard<std::mutex> lock(g_errorMutex);
    
    if (buffer && bufferSize > 0)
    {
        copyStringToBuffer(g_lastError, buffer, bufferSize);
    }
    
    return static_cast<int32_t>(g_lastError.length());
}

// ============================================================================
// Plugin Scanning
// ============================================================================

PLUGIN_HOST_API PluginHostError PluginHost_AddScanPath(const char* path)
{
    if (!path || !*path)
    {
        setError("Invalid path");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    VstHost::getInstance().addScanPath(path);
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_RemoveScanPath(const char* path)
{
    if (!path || !*path)
    {
        setError("Invalid path");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    VstHost::getInstance().removeScanPath(path);
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API void PluginHost_ClearScanPaths(void)
{
    VstHost::getInstance().clearScanPaths();
}

PLUGIN_HOST_API PluginHostError PluginHost_StartScan(
    PluginScanProgressCallback progressCallback,
    PluginScanCompleteCallback completeCallback,
    void* userData)
{
    if (!VstHost::getInstance().isInitialized())
    {
        setError("Plugin host not initialized");
        return PLUGIN_HOST_ERROR_NOT_INITIALIZED;
    }
    
    g_scanCallbacks.progressCallback = progressCallback;
    g_scanCallbacks.completeCallback = completeCallback;
    g_scanCallbacks.userData = userData;
    
    ScanProgressCallback wrappedProgress = nullptr;
    ScanCompleteCallback wrappedComplete = nullptr;
    
    if (progressCallback)
    {
        wrappedProgress = [](const std::string& currentPath, int found, int total)
        {
            if (g_scanCallbacks.progressCallback)
            {
                g_scanCallbacks.progressCallback(
                    currentPath.c_str(), found, total, g_scanCallbacks.userData);
            }
        };
    }
    
    if (completeCallback)
    {
        wrappedComplete = [](int totalFound)
        {
            if (g_scanCallbacks.completeCallback)
            {
                g_scanCallbacks.completeCallback(totalFound, g_scanCallbacks.userData);
            }
        };
    }
    
    if (!VstHost::getInstance().startScan(wrappedProgress, wrappedComplete))
    {
        setError(VstHost::getInstance().getLastError());
        return PLUGIN_HOST_ERROR_UNKNOWN;
    }
    
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API void PluginHost_StopScan(void)
{
    VstHost::getInstance().stopScan();
}

PLUGIN_HOST_API bool PluginHost_IsScanning(void)
{
    return VstHost::getInstance().isScanning();
}

PLUGIN_HOST_API int32_t PluginHost_GetPluginCount(void)
{
    return VstHost::getInstance().getPluginCount();
}

PLUGIN_HOST_API PluginHostError PluginHost_GetPluginInfo(int32_t index, PluginInfo* info)
{
    if (!info)
    {
        setError("Invalid info pointer");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    PluginDescription desc;
    if (!VstHost::getInstance().getPluginInfo(index, desc))
    {
        setError("Plugin not found at index");
        return PLUGIN_HOST_ERROR_PLUGIN_NOT_FOUND;
    }
    
    fillPluginInfo(desc, info);
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_GetPluginInfoByUid(const char* uid, PluginInfo* info)
{
    if (!uid || !*uid || !info)
    {
        setError("Invalid parameters");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    PluginDescription desc;
    if (!VstHost::getInstance().getPluginInfoByUid(uid, desc))
    {
        setError("Plugin not found");
        return PLUGIN_HOST_ERROR_PLUGIN_NOT_FOUND;
    }
    
    fillPluginInfo(desc, info);
    return PLUGIN_HOST_OK;
}

// ============================================================================
// Plugin Instance Management
// ============================================================================

PLUGIN_HOST_API PluginHostError PluginHost_LoadPlugin(const char* filePath, PluginInstanceHandle* handle)
{
    if (!filePath || !*filePath || !handle)
    {
        setError("Invalid parameters");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    if (!VstHost::getInstance().isInitialized())
    {
        setError("Plugin host not initialized");
        return PLUGIN_HOST_ERROR_NOT_INITIALIZED;
    }
    
    auto instance = VstHost::getInstance().loadPlugin(filePath);
    if (!instance)
    {
        setError(VstHost::getInstance().getLastError());
        return PLUGIN_HOST_ERROR_PLUGIN_LOAD_FAILED;
    }
    
    *handle = registerInstance(instance);
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_LoadPluginByUid(const char* uid, PluginInstanceHandle* handle)
{
    if (!uid || !*uid || !handle)
    {
        setError("Invalid parameters");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    if (!VstHost::getInstance().isInitialized())
    {
        setError("Plugin host not initialized");
        return PLUGIN_HOST_ERROR_NOT_INITIALIZED;
    }
    
    auto instance = VstHost::getInstance().loadPluginByUid(uid);
    if (!instance)
    {
        setError(VstHost::getInstance().getLastError());
        return PLUGIN_HOST_ERROR_PLUGIN_LOAD_FAILED;
    }
    
    *handle = registerInstance(instance);
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_UnloadPlugin(PluginInstanceHandle handle)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    VstHost::getInstance().unloadPlugin(instance);
    removeInstance(handle);
    
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_GetInstanceInfo(PluginInstanceHandle handle, PluginInfo* info)
{
    if (!info)
    {
        setError("Invalid info pointer");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    PluginInfo plugin_info;
    std::memset(&plugin_info, 0, sizeof(plugin_info));
    
    copyStringToBuffer(instance->getName(), plugin_info.name, sizeof(plugin_info.name));
    copyStringToBuffer(instance->getVendor(), plugin_info.vendor, sizeof(plugin_info.vendor));
    copyStringToBuffer(instance->getUid(), plugin_info.uid, sizeof(plugin_info.uid));
    
    plugin_info.numInputChannels = instance->getNumInputChannels();
    plugin_info.numOutputChannels = instance->getNumOutputChannels();
    plugin_info.numParameters = instance->getParameterCount();
    plugin_info.hasEditor = instance->hasEditor();
    plugin_info.acceptsMidi = instance->acceptsMidi();
    plugin_info.producesMidi = instance->producesMidi();
    plugin_info.isSynth = instance->isSynth();
    
    *info = plugin_info;
    return PLUGIN_HOST_OK;
}

// ============================================================================
// Audio Processing Configuration
// ============================================================================

PLUGIN_HOST_API PluginHostError PluginHost_SetAudioConfig(PluginInstanceHandle handle, const AudioConfig* config)
{
    if (!config)
    {
        setError("Invalid config pointer");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    if (!instance->setAudioConfig(config->sampleRate, config->blockSize,
                                   config->numInputChannels, config->numOutputChannels))
    {
        setError("Failed to set audio config");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_GetAudioConfig(PluginInstanceHandle handle, AudioConfig* config)
{
    if (!config)
    {
        setError("Invalid config pointer");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    config->sampleRate = instance->getSampleRate();
    config->blockSize = instance->getBlockSize();
    config->numInputChannels = instance->getNumInputChannels();
    config->numOutputChannels = instance->getNumOutputChannels();
    
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_PrepareToPlay(PluginInstanceHandle handle)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->prepareToPlay();
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_ReleaseResources(PluginInstanceHandle handle)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->releaseResources();
    return PLUGIN_HOST_OK;
}

// ============================================================================
// Audio Processing
// ============================================================================

PLUGIN_HOST_API PluginHostError PluginHost_ProcessAudio(
    PluginInstanceHandle handle,
    const float** inputBuffers,
    float** outputBuffers,
    int32_t numInputChannels,
    int32_t numOutputChannels,
    int32_t numSamples)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->processAudio(inputBuffers, outputBuffers,
                           numInputChannels, numOutputChannels, numSamples);
    
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_ProcessAudioInterleaved(
    PluginInstanceHandle handle,
    const float* inputBuffer,
    float* outputBuffer,
    int32_t numInputChannels,
    int32_t numOutputChannels,
    int32_t numSamples)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->processAudioInterleaved(inputBuffer, outputBuffer,
                                       numInputChannels, numOutputChannels, numSamples);
    
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_Reset(PluginInstanceHandle handle)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->reset();
    return PLUGIN_HOST_OK;
}

// ============================================================================
// MIDI Processing
// ============================================================================

PLUGIN_HOST_API PluginHostError PluginHost_SendMidiEvents(
    PluginInstanceHandle handle,
    const MidiEvent* events,
    int32_t numEvents)
{
    if (!events || numEvents <= 0)
    {
        setError("Invalid events");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    std::vector<InternalMidiEvent> internalEvents;
    internalEvents.reserve(numEvents);
    
    for (int i = 0; i < numEvents; ++i)
    {
        InternalMidiEvent e;
        e.sampleOffset = events[i].sampleOffset;
        e.status = events[i].status;
        e.data1 = events[i].data1;
        e.data2 = events[i].data2;
        e.channel = events[i].channel;
        internalEvents.push_back(e);
    }
    
    instance->addMidiEvents(internalEvents);
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_SendNoteOn(
    PluginInstanceHandle handle,
    int32_t channel,
    int32_t note,
    int32_t velocity,
    int32_t sampleOffset)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->sendNoteOn(channel, note, velocity, sampleOffset);
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_SendNoteOff(
    PluginInstanceHandle handle,
    int32_t channel,
    int32_t note,
    int32_t velocity,
    int32_t sampleOffset)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->sendNoteOff(channel, note, velocity, sampleOffset);
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_SendAllNotesOff(PluginInstanceHandle handle)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->sendAllNotesOff();
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_SendControlChange(
    PluginInstanceHandle handle,
    int32_t channel,
    int32_t controller,
    int32_t value,
    int32_t sampleOffset)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->sendControlChange(channel, controller, value, sampleOffset);
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_SendPitchBend(
    PluginInstanceHandle handle,
    int32_t channel,
    int32_t value,
    int32_t sampleOffset)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->sendPitchBend(channel, value, sampleOffset);
    return PLUGIN_HOST_OK;
}

// ============================================================================
// Parameter Management
// ============================================================================

PLUGIN_HOST_API int32_t PluginHost_GetParameterCount(PluginInstanceHandle handle)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return -1;
    }
    
    return instance->getParameterCount();
}

PLUGIN_HOST_API PluginHostError PluginHost_GetParameterInfo(
    PluginInstanceHandle handle,
    int32_t paramIndex,
    PluginParameterInfo* info)
{
    if (!info)
    {
        setError("Invalid info pointer");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    std::string name, label;
    float defaultValue, minValue, maxValue;
    int numSteps;
    bool isAutomatable, isDiscrete, isBoolean;
    
    if (!instance->getParameterInfo(paramIndex, name, label, defaultValue,
                                     minValue, maxValue, numSteps,
                                     isAutomatable, isDiscrete, isBoolean))
    {
        setError("Parameter not found");
        return PLUGIN_HOST_ERROR_PARAMETER_NOT_FOUND;
    }
    
    copyStringToBuffer(name, info->name, sizeof(info->name));
    copyStringToBuffer(label, info->label, sizeof(info->label));
    info->defaultValue = defaultValue;
    info->minValue = minValue;
    info->maxValue = maxValue;
    info->numSteps = numSteps;
    info->isAutomatable = isAutomatable;
    info->isDiscrete = isDiscrete;
    info->isBoolean = isBoolean;
    
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_GetParameter(
    PluginInstanceHandle handle,
    int32_t paramIndex,
    float* value)
{
    if (!value)
    {
        setError("Invalid value pointer");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    *value = instance->getParameter(paramIndex);
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_SetParameter(
    PluginInstanceHandle handle,
    int32_t paramIndex,
    float value)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->setParameter(paramIndex, value);
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API int32_t PluginHost_GetParameterText(
    PluginInstanceHandle handle,
    int32_t paramIndex,
    char* buffer,
    int32_t bufferSize)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return -1;
    }
    
    std::string text = instance->getParameterText(paramIndex);
    
    if (buffer && bufferSize > 0)
    {
        copyStringToBuffer(text, buffer, bufferSize);
    }
    
    return static_cast<int32_t>(text.length());
}

PLUGIN_HOST_API PluginHostError PluginHost_SetParameterChangeCallback(
    PluginInstanceHandle handle,
    ParameterChangeCallback callback,
    void* userData)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    if (callback)
    {
        instance->setParameterChangeCallback(
            [callback, userData, handle](int paramIndex, float value)
            {
                callback(handle, paramIndex, value, userData);
            });
    }
    else
    {
        instance->setParameterChangeCallback(nullptr);
    }
    
    return PLUGIN_HOST_OK;
}

// ============================================================================
// Plugin State (Presets)
// ============================================================================

PLUGIN_HOST_API PluginHostError PluginHost_GetState(
    PluginInstanceHandle handle,
    void* data,
    int32_t* dataSize)
{
    if (!dataSize)
    {
        setError("Invalid dataSize pointer");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    std::vector<uint8_t> state = instance->getState();
    
    if (!data)
    {
        // Just return the required size
        *dataSize = static_cast<int32_t>(state.size());
        return PLUGIN_HOST_OK;
    }
    
    if (*dataSize < static_cast<int32_t>(state.size()))
    {
        setError("Buffer too small");
        *dataSize = static_cast<int32_t>(state.size());
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    std::memcpy(data, state.data(), state.size());
    *dataSize = static_cast<int32_t>(state.size());
    
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_SetState(
    PluginInstanceHandle handle,
    const void* data,
    int32_t dataSize)
{
    if (!data || dataSize <= 0)
    {
        setError("Invalid state data");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    std::vector<uint8_t> state(dataSize);
    std::memcpy(state.data(), data, dataSize);
    
    if (!instance->setState(state))
    {
        setError("Failed to restore state");
        return PLUGIN_HOST_ERROR_UNKNOWN;
    }
    
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API int32_t PluginHost_GetPresetCount(PluginInstanceHandle handle)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return -1;
    }
    
    return instance->getPresetCount();
}

PLUGIN_HOST_API int32_t PluginHost_GetCurrentPreset(PluginInstanceHandle handle)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return -1;
    }
    
    return instance->getCurrentPreset();
}

PLUGIN_HOST_API PluginHostError PluginHost_SetCurrentPreset(
    PluginInstanceHandle handle,
    int32_t presetIndex)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->setCurrentPreset(presetIndex);
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API int32_t PluginHost_GetPresetName(
    PluginInstanceHandle handle,
    int32_t presetIndex,
    char* buffer,
    int32_t bufferSize)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return -1;
    }
    
    std::string name = instance->getPresetName(presetIndex);
    
    if (buffer && bufferSize > 0)
    {
        copyStringToBuffer(name, buffer, bufferSize);
    }
    
    return static_cast<int32_t>(name.length());
}

// ============================================================================
// Plugin Editor (GUI)
// ============================================================================

PLUGIN_HOST_API bool PluginHost_HasEditor(PluginInstanceHandle handle)
{
    auto instance = getInstance(handle);
    if (!instance)
        return false;
    
    return instance->hasEditor();
}

PLUGIN_HOST_API PluginHostError PluginHost_OpenEditor(
    PluginInstanceHandle handle,
    void* parentWindow,
    PluginEditorHandle* editorHandle)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    void* editor = instance->openEditor(parentWindow);
    if (!editor)
    {
        setError("Failed to open editor");
        return PLUGIN_HOST_ERROR_UNKNOWN;
    }
    
    if (editorHandle)
        *editorHandle = editor;
    
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_CloseEditor(PluginInstanceHandle handle)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    instance->closeEditor();
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_GetEditorSize(
    PluginInstanceHandle handle,
    int32_t* width,
    int32_t* height)
{
    if (!width || !height)
    {
        setError("Invalid size pointers");
        return PLUGIN_HOST_ERROR_INVALID_PARAMETER;
    }
    
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    int w, h;
    if (!instance->getEditorSize(w, h))
    {
        setError("Failed to get editor size");
        return PLUGIN_HOST_ERROR_UNKNOWN;
    }
    
    *width = w;
    *height = h;
    return PLUGIN_HOST_OK;
}

PLUGIN_HOST_API PluginHostError PluginHost_SetEditorResizeCallback(
    PluginInstanceHandle handle,
    ::EditorResizeCallback callback,
    void* userData)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return PLUGIN_HOST_ERROR_INVALID_HANDLE;
    }
    
    if (callback)
    {
        instance->setEditorResizeCallback(
            [callback, userData, handle](int width, int height)
            {
                callback(handle, width, height, userData);
            });
    }
    else
    {
        instance->setEditorResizeCallback(nullptr);
    }
    
    return PLUGIN_HOST_OK;
}

// ============================================================================
// Latency
// ============================================================================

PLUGIN_HOST_API int32_t PluginHost_GetLatency(PluginInstanceHandle handle)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return -1;
    }
    
    return instance->getLatency();
}

// ============================================================================
// Tail Time
// ============================================================================

PLUGIN_HOST_API double PluginHost_GetTailTime(PluginInstanceHandle handle)
{
    auto instance = getInstance(handle);
    if (!instance)
    {
        setError("Invalid handle");
        return -1.0;
    }
    
    return instance->getTailTime();
}
