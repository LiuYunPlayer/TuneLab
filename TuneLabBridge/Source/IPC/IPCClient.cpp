#include "IPCClient.h"
#include "../Utils/Logger.h"

namespace TuneLabBridge
{

IPCClient::IPCClient()
    : m_clientId(juce::Uuid().toString())
{
    LOG_INFO("IPCClient constructor, clientId=" + m_clientId.toStdString());
    m_pipeClient = std::make_unique<NamedPipeClient>();
    m_shmClient = std::make_unique<SharedMemoryClient>();
    
    // Set up pipe message callback
    m_pipeClient->setMessageCallback([this](const juce::String& json) {
        LOG_DEBUG("IPCClient received message: " + json.substring(0, 200).toStdString());
        handleMessage(json);
    });
    
    // Set up connection callback
    m_pipeClient->setConnectionCallback([this](bool connected) {
        LOG_INFO("IPCClient connection callback: connected=" + std::to_string(connected) + ", m_connected=" + std::to_string(m_connected));
        if (!connected && m_connected)
        {
            LOG_INFO("IPCClient: pipe disconnected, cleaning up");
            m_connected = false;
            m_shmClient->close();
            if (onDisconnected)
                onDisconnected();
        }
    });
}

IPCClient::~IPCClient()
{
    disconnect();
}

bool IPCClient::connect(int sampleRate, int bufferSize)
{
    LOG_INFO("IPCClient::connect() called, sampleRate=" + std::to_string(sampleRate) + ", bufferSize=" + std::to_string(bufferSize));
    
    if (m_connected)
    {
        LOG_INFO("Already connected, returning true");
        return true;
    }
    
    m_sampleRate = sampleRate;
    m_bufferSize = bufferSize;
    
    // Connect via named pipe
    LOG_INFO("Connecting to named pipe...");
    if (!m_pipeClient->connect())
    {
        LOG_ERROR("Failed to connect to named pipe");
        return false;
    }
    
    LOG_INFO("Named pipe connected, sending connect message");
    
    // Send connect command
    auto connectMsg = NamedPipeClient::createConnectMessage(m_clientId, sampleRate, bufferSize);
    LOG_DEBUG("Connect message: " + connectMsg.toStdString());
    if (!m_pipeClient->sendMessage(connectMsg))
    {
        LOG_ERROR("Failed to send connect message");
        m_pipeClient->disconnect();
        return false;
    }
    
    LOG_INFO("Connect message sent, waiting for shared memory setup...");
    
    // Wait briefly for response and shared memory setup
    // In a real implementation, you might want to use async callbacks
    juce::Thread::sleep(100);
    
    // Open shared memory
    LOG_INFO("Opening shared memory with clientId: " + m_clientId.toStdString());
    if (!m_shmClient->open(m_clientId.toStdString()))
    {
        LOG_ERROR("Failed to open shared memory");
        m_pipeClient->disconnect();
        return false;
    }
    
    LOG_INFO("Shared memory opened successfully");
    
    m_connected = true;
    m_readPosition = 0;
    
    LOG_INFO("Calling onConnected callback");
    if (onConnected)
        onConnected();
    
    // Request initial track list
    LOG_INFO("Requesting initial track list");
    refreshTrackList();
    
    LOG_INFO("IPCClient::connect() completed successfully");
    return true;
}

void IPCClient::disconnect()
{
    if (!m_connected)
        return;
    
    // Send disconnect command
    auto disconnectMsg = NamedPipeClient::createDisconnectMessage();
    m_pipeClient->sendMessage(disconnectMsg);
    
    m_pipeClient->disconnect();
    m_shmClient->close();
    
    m_connected = false;
    m_trackList.clear();
    m_selectedTrackId.clear();
    
    if (onDisconnected)
        onDisconnected();
}

void IPCClient::refreshTrackList()
{
    if (!m_connected)
        return;
    
    auto msg = NamedPipeClient::createGetTrackListMessage();
    m_pipeClient->sendMessage(msg);
}

void IPCClient::selectTrack(const juce::String& trackId)
{
    if (!m_connected)
        return;
    
    m_selectedTrackId = trackId;
    
    // Reset read position when changing tracks
    {
        std::lock_guard<std::mutex> lock(m_audioMutex);
        m_readPosition = 0;
    }
    
    auto msg = NamedPipeClient::createSelectTrackMessage(trackId);
    m_pipeClient->sendMessage(msg);
}

void IPCClient::sendTransportState(bool isPlaying, double position)
{
    if (!m_connected)
        return;
    
    auto msg = NamedPipeClient::createTransportMessage(isPlaying, position);
    m_pipeClient->sendMessage(msg);
}

void IPCClient::sendSeek(double position)
{
    if (!m_connected)
        return;
    
    // Reset read position on seek
    {
        std::lock_guard<std::mutex> lock(m_audioMutex);
        m_readPosition = 0;
    }
    
    auto msg = NamedPipeClient::createSeekMessage(position);
    m_pipeClient->sendMessage(msg);
}

size_t IPCClient::readAudio(float* leftChannel, float* rightChannel, size_t numSamples)
{
    if (!m_connected || !m_shmClient->isOpen())
    {
        // Fill with silence
        std::memset(leftChannel, 0, numSamples * sizeof(float));
        std::memset(rightChannel, 0, numSamples * sizeof(float));
        return 0;
    }
    
    std::lock_guard<std::mutex> lock(m_audioMutex);
    
    size_t samplesRead = m_shmClient->readStereoSamples(leftChannel, rightChannel, m_readPosition, numSamples);
    
    // Fill remaining with silence if underrun
    if (samplesRead < numSamples)
    {
        std::memset(leftChannel + samplesRead, 0, (numSamples - samplesRead) * sizeof(float));
        std::memset(rightChannel + samplesRead, 0, (numSamples - samplesRead) * sizeof(float));
    }
    
    return samplesRead;
}

size_t IPCClient::getAvailableAudioSamples() const
{
    if (!m_connected || !m_shmClient->isOpen())
        return 0;
    
    return m_shmClient->getAvailableSamples();
}

void IPCClient::handleMessage(const juce::String& json)
{
    auto parsed = juce::JSON::parse(json);
    if (!parsed.isObject())
        return;
    
    auto* obj = parsed.getDynamicObject();
    if (!obj)
        return;
    
    juce::String type = obj->getProperty("type").toString();
    
    if (type == "response")
        handleResponse(parsed);
    else if (type == "event")
        handleEvent(parsed);
}

void IPCClient::handleResponse(const juce::var& message)
{
    auto* obj = message.getDynamicObject();
    if (!obj)
        return;
    
    auto payload = obj->getProperty("payload");
    auto* payloadObj = payload.getDynamicObject();
    if (!payloadObj)
        return;
    
    bool success = payloadObj->getProperty("success");
    if (!success)
        return;
    
    auto data = payloadObj->getProperty("data");
    
    // Check if this is a track list response
    if (data.isObject())
    {
        auto* dataObj = data.getDynamicObject();
        if (dataObj && dataObj->hasProperty("tracks"))
        {
            std::lock_guard<std::mutex> lock(m_trackListMutex);
            m_trackList = parseTrackList(dataObj->getProperty("tracks"));
            
            if (onTrackListChanged)
                onTrackListChanged(m_trackList);
        }
    }
}

void IPCClient::handleEvent(const juce::var& message)
{
    auto* obj = message.getDynamicObject();
    if (!obj)
        return;
    
    auto payload = obj->getProperty("payload");
    auto* payloadObj = payload.getDynamicObject();
    if (!payloadObj)
        return;
    
    juce::String eventName = payloadObj->getProperty("event").toString();
    
    if (eventName == "trackListChanged")
    {
        std::lock_guard<std::mutex> lock(m_trackListMutex);
        m_trackList = parseTrackList(payloadObj->getProperty("tracks"));
        
        if (onTrackListChanged)
            onTrackListChanged(m_trackList);
    }
    else if (eventName == "transportChanged")
    {
        juce::String state = payloadObj->getProperty("state").toString();
        double position = payloadObj->getProperty("position");
        bool isPlaying = (state == "play");
        
        if (onTransportChanged)
            onTransportChanged(isPlaying, position);
    }
}

} // namespace TuneLabBridge
