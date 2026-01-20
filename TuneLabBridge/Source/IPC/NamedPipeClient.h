#pragma once

#include <JuceHeader.h>
#include <string>
#include <functional>
#include <atomic>
#include <thread>
#include <queue>
#include <mutex>

namespace TuneLabBridge
{

/**
 * Client for named pipe communication with TuneLab bridge service.
 * Handles connection, message sending/receiving, and JSON protocol.
 */
class NamedPipeClient
{
public:
    NamedPipeClient();
    ~NamedPipeClient();
    
    // Non-copyable
    NamedPipeClient(const NamedPipeClient&) = delete;
    NamedPipeClient& operator=(const NamedPipeClient&) = delete;
    
    /**
     * Connects to the TuneLab bridge service.
     * @return True if connection successful
     */
    bool connect();
    
    /**
     * Disconnects from the bridge service.
     */
    void disconnect();
    
    /**
     * Checks if connected.
     */
    bool isConnected() const { return m_connected.load(); }
    
    /**
     * Sends a JSON message to the bridge.
     * @param json JSON string to send
     * @return True if sent successfully
     */
    bool sendMessage(const juce::String& json);
    
    /**
     * Sets callback for received messages.
     * Callback is called from the read thread.
     */
    void setMessageCallback(std::function<void(const juce::String&)> callback);
    
    /**
     * Sets callback for connection state changes.
     */
    void setConnectionCallback(std::function<void(bool)> callback);
    
    // Helper methods for building JSON messages
    
    /**
     * Creates a connect command message.
     */
    static juce::String createConnectMessage(const juce::String& clientId, int sampleRate, int bufferSize);
    
    /**
     * Creates a disconnect command message.
     */
    static juce::String createDisconnectMessage();
    
    /**
     * Creates a get track list command message.
     */
    static juce::String createGetTrackListMessage();
    
    /**
     * Creates a select track command message.
     */
    static juce::String createSelectTrackMessage(const juce::String& trackId);
    
    /**
     * Creates a transport command message.
     */
    static juce::String createTransportMessage(bool isPlaying, double position);
    
    /**
     * Creates a seek command message.
     */
    static juce::String createSeekMessage(double position);
    
private:
    void readLoop();
    void processMessage(const juce::String& json);
    void cleanupConnection();
    
    std::atomic<bool> m_connected{false};
    std::atomic<bool> m_shouldStop{false};
    
    std::unique_ptr<juce::NamedPipe> m_pipe;
    std::unique_ptr<std::thread> m_readThread;
    
    std::function<void(const juce::String&)> m_messageCallback;
    std::function<void(bool)> m_connectionCallback;
    
    std::mutex m_sendMutex;
    std::mutex m_callbackMutex;
};

} // namespace TuneLabBridge
