#pragma once

#include <JuceHeader.h>
#include "NamedPipeClient.h"
#include "SharedMemoryClient.h"
#include "RingBuffer.h"
#include "../Model/TrackInfo.h"
#include "../Model/TransportState.h"

#include <memory>
#include <functional>
#include <vector>
#include <string>
#include <mutex>

namespace TuneLabBridge
{

/**
 * High-level IPC client that combines named pipe and shared memory communication.
 * This is the main interface for the plugin to communicate with TuneLab.
 */
class IPCClient
{
public:
    IPCClient();
    ~IPCClient();
    
    /**
     * Connects to TuneLab bridge service.
     * @param sampleRate The plugin's sample rate
     * @param bufferSize The plugin's buffer size
     * @return True if connection successful
     */
    bool connect(int sampleRate, int bufferSize);
    
    /**
     * Disconnects from the bridge service.
     */
    void disconnect();
    
    /**
     * Checks if connected to TuneLab.
     */
    bool isConnected() const { return m_connected; }
    
    /**
     * Gets the unique client ID.
     */
    const juce::String& getClientId() const { return m_clientId; }
    
    /**
     * Gets the current track list.
     */
    const std::vector<TrackInfo>& getTrackList() const { return m_trackList; }
    
    /**
     * Requests updated track list from TuneLab.
     */
    void refreshTrackList();
    
    /**
     * Selects a track for audio streaming.
     * @param trackId Track ID, or empty string for master
     */
    void selectTrack(const juce::String& trackId);
    
    /**
     * Gets the currently selected track ID.
     */
    const juce::String& getSelectedTrackId() const { return m_selectedTrackId; }
    
    /**
     * Sends transport state to TuneLab.
     */
    void sendTransportState(bool isPlaying, double position);
    
    /**
     * Sends seek command to TuneLab.
     */
    void sendSeek(double position);
    
    /**
     * Reads audio samples from the shared memory buffer.
     * Should be called from the audio thread.
     * @param leftChannel Destination for left channel
     * @param rightChannel Destination for right channel
     * @param numSamples Number of samples to read
     * @return Number of samples actually read
     */
    size_t readAudio(float* leftChannel, float* rightChannel, size_t numSamples);
    
    /**
     * Checks if audio data is available.
     */
    size_t getAvailableAudioSamples() const;
    
    // Callbacks
    std::function<void()> onConnected;
    std::function<void()> onDisconnected;
    std::function<void(const std::vector<TrackInfo>&)> onTrackListChanged;
    std::function<void(bool isPlaying, double position)> onTransportChanged;
    
private:
    void handleMessage(const juce::String& json);
    void handleResponse(const juce::var& message);
    void handleEvent(const juce::var& message);
    
    juce::String m_clientId;
    bool m_connected = false;
    int m_sampleRate = 48000;
    int m_bufferSize = 512;
    
    std::unique_ptr<NamedPipeClient> m_pipeClient;
    std::unique_ptr<SharedMemoryClient> m_shmClient;
    
    std::vector<TrackInfo> m_trackList;
    juce::String m_selectedTrackId;
    
    int64_t m_readPosition = 0;
    
    std::mutex m_trackListMutex;
    std::mutex m_audioMutex;
};

} // namespace TuneLabBridge
