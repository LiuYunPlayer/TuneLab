/*
 * VstHost.cpp - Main VST Host implementation
 */

#include "VstHost.h"
#include "PluginInstance.h"
#include <thread>
#include <chrono>

namespace PluginHost
{

VstHost& VstHost::getInstance()
{
    static VstHost instance;
    return instance;
}

VstHost::VstHost()
{
}

VstHost::~VstHost()
{
    shutdown();
}

bool VstHost::initialize()
{
    if (initialized.load())
    {
        setLastError("Plugin host already initialized");
        return false;
    }
    
    // Initialize JUCE message manager if needed
    juce::MessageManager::getInstance();
    
    // Add default plugin formats
    formatManager.addDefaultFormats();
    
    // Add default plugin scan paths based on platform
#if JUCE_WINDOWS
    addScanPath("C:\\Program Files\\Common Files\\VST3");
    addScanPath("C:\\Program Files\\VSTPlugins");
    addScanPath("C:\\Program Files\\Steinberg\\VSTPlugins");
    addScanPath("C:\\Program Files (x86)\\Common Files\\VST3");
    addScanPath("C:\\Program Files (x86)\\VSTPlugins");
    addScanPath("C:\\Program Files (x86)\\Steinberg\\VSTPlugins");
#elif JUCE_MAC
    addScanPath("/Library/Audio/Plug-Ins/VST3");
    addScanPath("/Library/Audio/Plug-Ins/VST");
    addScanPath("/Library/Audio/Plug-Ins/Components");
    addScanPath("~/Library/Audio/Plug-Ins/VST3");
    addScanPath("~/Library/Audio/Plug-Ins/VST");
    addScanPath("~/Library/Audio/Plug-Ins/Components");
#elif JUCE_LINUX
    addScanPath("/usr/lib/vst3");
    addScanPath("/usr/lib/vst");
    addScanPath("/usr/local/lib/vst3");
    addScanPath("/usr/local/lib/vst");
    addScanPath("~/.vst3");
    addScanPath("~/.vst");
#endif
    
    initialized = true;
    return true;
}

void VstHost::shutdown()
{
    if (!initialized.load())
        return;
    
    // Stop any ongoing scan
    stopScan();
    
    // Unload all instances
    {
        std::lock_guard<std::mutex> lock(instancesMutex);
        loadedInstances.clear();
    }
    
    // Clear plugin list
    {
        std::lock_guard<std::mutex> lock(pluginsMutex);
        discoveredPlugins.clear();
        uidToIndex.clear();
    }
    
    initialized = false;
}

std::string VstHost::getLastError() const
{
    std::lock_guard<std::mutex> lock(errorMutex);
    return lastError;
}

void VstHost::setLastError(const std::string& error)
{
    std::lock_guard<std::mutex> lock(errorMutex);
    lastError = error;
}

// ============================================================================
// Plugin Scanning
// ============================================================================

void VstHost::addScanPath(const std::string& path)
{
    std::lock_guard<std::mutex> lock(scanPathsMutex);
    
    // Check if path already exists
    for (const auto& existing : scanPaths)
    {
        if (existing == path)
            return;
    }
    
    scanPaths.push_back(path);
}

void VstHost::removeScanPath(const std::string& path)
{
    std::lock_guard<std::mutex> lock(scanPathsMutex);
    
    scanPaths.erase(
        std::remove(scanPaths.begin(), scanPaths.end(), path),
        scanPaths.end()
    );
}

void VstHost::clearScanPaths()
{
    std::lock_guard<std::mutex> lock(scanPathsMutex);
    scanPaths.clear();
}

bool VstHost::startScan(ScanProgressCallback progressCallback,
                        ScanCompleteCallback completeCallback)
{
    if (!initialized.load())
    {
        setLastError("Plugin host not initialized");
        return false;
    }
    
    if (scanning.load())
    {
        setLastError("Scan already in progress");
        return false;
    }
    
    scanProgressCallback = progressCallback;
    scanCompleteCallback = completeCallback;
    shouldStopScan = false;
    scanning = true;
    
    // Start scan in a separate thread
    scanThread = std::make_unique<std::thread>(&VstHost::performScan, this);
    
    return true;
}

void VstHost::stopScan()
{
    if (scanning.load())
    {
        shouldStopScan = true;
        
        if (scanThread && scanThread->joinable())
        {
            scanThread->join();
        }
        
        scanThread.reset();
        scanning = false;
    }
}

void VstHost::performScan()
{
    std::vector<std::string> pathsToScan;
    
    {
        std::lock_guard<std::mutex> lock(scanPathsMutex);
        pathsToScan = scanPaths;
    }
    
    // Clear existing plugins
    {
        std::lock_guard<std::mutex> lock(pluginsMutex);
        discoveredPlugins.clear();
        uidToIndex.clear();
    }
    
    knownPluginList.clear();
    
    int totalFound = 0;
    int totalScanned = 0;
    
    for (const auto& path : pathsToScan)
    {
        if (shouldStopScan.load())
            break;
        
        juce::File directory(path);
        
        if (!directory.exists() || !directory.isDirectory())
            continue;
        
        // Scan for plugins in this directory
        for (int formatIndex = 0; formatIndex < formatManager.getNumFormats(); ++formatIndex)
        {
            if (shouldStopScan.load())
                break;
            
            juce::AudioPluginFormat* format = formatManager.getFormat(formatIndex);
            
            if (format == nullptr)
                continue;
            
            // Search subdirectories recursively
            juce::Array<juce::File> files;
            directory.findChildFiles(files, juce::File::findFilesAndDirectories, true);
            
            for (const auto& file : files)
            {
                if (shouldStopScan.load())
                    break;
                
                // Check file extension
                juce::String ext = file.getFileExtension().toLowerCase();
                
                bool isPlugin = false;
                
#if JUCE_WINDOWS
                isPlugin = ext == ".dll" || ext == ".vst3";
#elif JUCE_MAC
                isPlugin = ext == ".vst" || ext == ".vst3" || ext == ".component";
#elif JUCE_LINUX
                isPlugin = ext == ".so" || ext == ".vst3";
#endif
                
                if (!isPlugin)
                    continue;
                
                // Try to scan this file
                juce::OwnedArray<juce::PluginDescription> descriptions;
                format->findAllTypesForFile(descriptions, file.getFullPathName());
                
                if (scanProgressCallback)
                {
                    scanProgressCallback(file.getFullPathName().toStdString(), 
                                        totalFound, 
                                        ++totalScanned);
                }
                
                for (auto* desc : descriptions)
                {
                    if (shouldStopScan.load())
                        break;
                    
                    // Convert and store the plugin description
                    PluginDescription pluginDesc = convertDescription(*desc);
                    
                    {
                        std::lock_guard<std::mutex> lock(pluginsMutex);
                        
                        // Check for duplicates
                        if (uidToIndex.find(pluginDesc.uid) != uidToIndex.end())
                            continue;
                        
                        int index = static_cast<int>(discoveredPlugins.size());
                        uidToIndex[pluginDesc.uid] = index;
                        discoveredPlugins.push_back(pluginDesc);
                        totalFound++;
                    }
                    
                    // Also add to JUCE known plugin list
                    knownPluginList.addType(*desc);
                }
            }
        }
    }
    
    scanning = false;
    
    if (scanCompleteCallback)
    {
        scanCompleteCallback(totalFound);
    }
}

int VstHost::getPluginCount() const
{
    std::lock_guard<std::mutex> lock(pluginsMutex);
    return static_cast<int>(discoveredPlugins.size());
}

bool VstHost::getPluginInfo(int index, PluginDescription& info) const
{
    std::lock_guard<std::mutex> lock(pluginsMutex);
    
    if (index < 0 || index >= static_cast<int>(discoveredPlugins.size()))
        return false;
    
    info = discoveredPlugins[index];
    return true;
}

bool VstHost::getPluginInfoByUid(const std::string& uid, PluginDescription& info) const
{
    std::lock_guard<std::mutex> lock(pluginsMutex);
    
    auto it = uidToIndex.find(uid);
    if (it == uidToIndex.end())
        return false;
    
    info = discoveredPlugins[it->second];
    return true;
}

PluginDescription VstHost::convertDescription(const juce::PluginDescription& juceDesc)
{
    PluginDescription desc;
    
    desc.name = juceDesc.name.toStdString();
    desc.vendor = juceDesc.manufacturerName.toStdString();
    desc.version = juceDesc.version.toStdString();
    desc.uid = juceDesc.createIdentifierString().toStdString();
    desc.filePath = juceDesc.fileOrIdentifier.toStdString();
    
    // Determine plugin type
    juce::String format = juceDesc.pluginFormatName.toLowerCase();
    if (format.contains("vst3"))
        desc.type = 2; // PLUGIN_TYPE_VST3
    else if (format.contains("vst"))
        desc.type = 1; // PLUGIN_TYPE_VST2
    else if (format.contains("au") || format.contains("audio unit"))
        desc.type = 3; // PLUGIN_TYPE_AU
    else if (format.contains("ladspa"))
        desc.type = 4; // PLUGIN_TYPE_LADSPA
    else
        desc.type = 0; // PLUGIN_TYPE_UNKNOWN
    
    // Determine category
    if (juceDesc.isInstrument)
        desc.category = 2; // PLUGIN_CATEGORY_INSTRUMENT
    else
        desc.category = 1; // PLUGIN_CATEGORY_EFFECT
    
    desc.numInputChannels = juceDesc.numInputChannels;
    desc.numOutputChannels = juceDesc.numOutputChannels;
    desc.hasEditor = juceDesc.hasSharedContainer;
    desc.acceptsMidi = juceDesc.isInstrument;
    desc.producesMidi = false;
    desc.isSynth = juceDesc.isInstrument;
    
    desc.juceDescription = juceDesc;
    
    return desc;
}

// ============================================================================
// Plugin Instance Management
// ============================================================================

std::shared_ptr<PluginInstance> VstHost::loadPlugin(const std::string& filePath)
{
    if (!initialized.load())
    {
        setLastError("Plugin host not initialized");
        return nullptr;
    }
    
    juce::String errorMessage;
    
    // Try to find the plugin description
    juce::OwnedArray<juce::PluginDescription> descriptions;
    
    for (int i = 0; i < formatManager.getNumFormats(); ++i)
    {
        juce::AudioPluginFormat* format = formatManager.getFormat(i);
        if (format)
        {
            format->findAllTypesForFile(descriptions, filePath);
            if (descriptions.size() > 0)
                break;
        }
    }
    
    if (descriptions.isEmpty())
    {
        setLastError("No plugin found at: " + filePath);
        return nullptr;
    }
    
    // Use the first description found
    juce::PluginDescription& desc = *descriptions[0];
    
    // Create the plugin instance
    std::unique_ptr<juce::AudioPluginInstance> pluginInstance;
    
    pluginInstance = formatManager.createPluginInstance(
        desc, 44100.0, 512, errorMessage);
    
    if (!pluginInstance)
    {
        setLastError("Failed to load plugin: " + errorMessage.toStdString());
        return nullptr;
    }
    
    // Wrap in our PluginInstance class
    auto instance = std::make_shared<PluginInstance>(std::move(pluginInstance), desc);
    
    {
        std::lock_guard<std::mutex> lock(instancesMutex);
        loadedInstances.push_back(instance);
    }
    
    return instance;
}

std::shared_ptr<PluginInstance> VstHost::loadPluginByUid(const std::string& uid)
{
    if (!initialized.load())
    {
        setLastError("Plugin host not initialized");
        return nullptr;
    }
    
    PluginDescription desc;
    if (!getPluginInfoByUid(uid, desc))
    {
        setLastError("Plugin not found: " + uid);
        return nullptr;
    }
    
    juce::String errorMessage;
    
    std::unique_ptr<juce::AudioPluginInstance> pluginInstance;
    
    pluginInstance = formatManager.createPluginInstance(
        desc.juceDescription, 44100.0, 512, errorMessage);
    
    if (!pluginInstance)
    {
        setLastError("Failed to load plugin: " + errorMessage.toStdString());
        return nullptr;
    }
    
    auto instance = std::make_shared<PluginInstance>(std::move(pluginInstance), desc.juceDescription);
    
    {
        std::lock_guard<std::mutex> lock(instancesMutex);
        loadedInstances.push_back(instance);
    }
    
    return instance;
}

bool VstHost::unloadPlugin(std::shared_ptr<PluginInstance> instance)
{
    if (!instance)
        return false;
    
    std::lock_guard<std::mutex> lock(instancesMutex);
    
    auto it = std::find(loadedInstances.begin(), loadedInstances.end(), instance);
    if (it != loadedInstances.end())
    {
        loadedInstances.erase(it);
        return true;
    }
    
    return false;
}

} // namespace PluginHost
