/*
 * VstHost.h - Main VST Host management class
 */

#ifndef VST_HOST_H
#define VST_HOST_H

#include <juce_audio_processors/juce_audio_processors.h>
#include <juce_audio_devices/juce_audio_devices.h>
#include <memory>
#include <vector>
#include <string>
#include <mutex>
#include <atomic>
#include <functional>
#include <unordered_map>

namespace PluginHost
{

// Forward declarations
class PluginInstance;

/**
 * Callback types for plugin scanning
 */
using ScanProgressCallback = std::function<void(const std::string& currentPath, int found, int total)>;
using ScanCompleteCallback = std::function<void(int totalFound)>;

/**
 * Plugin information structure
 */
struct PluginDescription
{
    std::string name;
    std::string vendor;
    std::string version;
    std::string uid;
    std::string filePath;
    int type = 0;           // PluginType
    int category = 0;       // PluginCategory
    int numInputChannels = 0;
    int numOutputChannels = 0;
    int numParameters = 0;
    bool hasEditor = false;
    bool acceptsMidi = false;
    bool producesMidi = false;
    bool isSynth = false;
    
    juce::PluginDescription juceDescription;
};

/**
 * VstHost - Singleton class managing the VST host system
 */
class VstHost
{
public:
    /**
     * Get the singleton instance
     */
    static VstHost& getInstance();
    
    /**
     * Initialize the host system
     */
    bool initialize();
    
    /**
     * Shutdown the host system
     */
    void shutdown();
    
    /**
     * Check if initialized
     */
    bool isInitialized() const { return initialized.load(); }
    
    /**
     * Get the last error message
     */
    std::string getLastError() const;
    
    /**
     * Set the last error message
     */
    void setLastError(const std::string& error);
    
    // ========================================================================
    // Plugin Scanning
    // ========================================================================
    
    /**
     * Add a scan path
     */
    void addScanPath(const std::string& path);
    
    /**
     * Remove a scan path
     */
    void removeScanPath(const std::string& path);
    
    /**
     * Clear all scan paths
     */
    void clearScanPaths();
    
    /**
     * Start scanning for plugins
     */
    bool startScan(ScanProgressCallback progressCallback = nullptr,
                   ScanCompleteCallback completeCallback = nullptr);
    
    /**
     * Stop the current scan
     */
    void stopScan();
    
    /**
     * Check if scanning
     */
    bool isScanning() const { return scanning.load(); }
    
    /**
     * Get the number of discovered plugins
     */
    int getPluginCount() const;
    
    /**
     * Get plugin info by index
     */
    bool getPluginInfo(int index, PluginDescription& info) const;
    
    /**
     * Get plugin info by UID
     */
    bool getPluginInfoByUid(const std::string& uid, PluginDescription& info) const;
    
    // ========================================================================
    // Plugin Instance Management
    // ========================================================================
    
    /**
     * Load a plugin from file path
     */
    std::shared_ptr<PluginInstance> loadPlugin(const std::string& filePath);
    
    /**
     * Load a plugin by UID
     */
    std::shared_ptr<PluginInstance> loadPluginByUid(const std::string& uid);
    
    /**
     * Unload a plugin instance
     */
    bool unloadPlugin(std::shared_ptr<PluginInstance> instance);
    
    /**
     * Get the audio plugin format manager
     */
    juce::AudioPluginFormatManager& getFormatManager() { return formatManager; }
    
private:
    VstHost();
    ~VstHost();
    
    // Prevent copying
    VstHost(const VstHost&) = delete;
    VstHost& operator=(const VstHost&) = delete;
    
    /**
     * Perform the actual scanning
     */
    void performScan();
    
    /**
     * Convert JUCE description to our format
     */
    static PluginDescription convertDescription(const juce::PluginDescription& juceDesc);
    
    // State
    std::atomic<bool> initialized{false};
    std::atomic<bool> scanning{false};
    std::atomic<bool> shouldStopScan{false};
    
    // Error handling
    mutable std::mutex errorMutex;
    std::string lastError;
    
    // JUCE components
    juce::AudioPluginFormatManager formatManager;
    juce::KnownPluginList knownPluginList;
    
    // Scan paths
    mutable std::mutex scanPathsMutex;
    std::vector<std::string> scanPaths;
    
    // Discovered plugins
    mutable std::mutex pluginsMutex;
    std::vector<PluginDescription> discoveredPlugins;
    std::unordered_map<std::string, int> uidToIndex;
    
    // Scan callbacks
    ScanProgressCallback scanProgressCallback;
    ScanCompleteCallback scanCompleteCallback;
    
    // Scan thread
    std::unique_ptr<std::thread> scanThread;
    
    // Loaded instances
    mutable std::mutex instancesMutex;
    std::vector<std::shared_ptr<PluginInstance>> loadedInstances;
};

} // namespace PluginHost

#endif // VST_HOST_H
