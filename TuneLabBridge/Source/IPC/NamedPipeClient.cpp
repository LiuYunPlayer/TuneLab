#include "NamedPipeClient.h"
#include "../Model/BridgeProtocol.h"
#include "../Utils/Logger.h"

namespace TuneLabBridge
{

NamedPipeClient::NamedPipeClient()
{
    LOG_INFO("NamedPipeClient created");
}

NamedPipeClient::~NamedPipeClient()
{
    disconnect();
}

bool NamedPipeClient::connect()
{
    LOG_INFO("NamedPipeClient::connect() called");
    
    if (m_connected.load())
    {
        LOG_INFO("Already connected, returning true");
        return true;
    }
    
    // Clean up any previous connection first (in case disconnect wasn't called)
    cleanupConnection();
    
    m_pipe = std::make_unique<juce::NamedPipe>();
    
    // JUCE NamedPipe expects just the pipe name on Windows
    // It internally converts to \\.\pipe\<name>
    if (!m_pipe->openExisting(Protocol::PipeName))
    {
        LOG_ERROR("Failed to open existing pipe: " + std::string(Protocol::PipeName));
        m_pipe.reset();
        return false;
    }
    
    LOG_INFO("Pipe opened successfully");
    
    m_shouldStop.store(false);
    m_connected.store(true);
    
    // Start read thread
    m_readThread = std::make_unique<std::thread>(&NamedPipeClient::readLoop, this);
    LOG_INFO("Read thread started");
    
    // Notify connection callback
    {
        std::lock_guard<std::mutex> lock(m_callbackMutex);
        if (m_connectionCallback)
            m_connectionCallback(true);
    }
    
    LOG_INFO("NamedPipeClient::connect() returning true");
    return true;
}

void NamedPipeClient::disconnect()
{
    bool wasConnected = m_connected.exchange(false);
    
    cleanupConnection();
    
    // Notify connection callback only if we were actually connected before
    if (wasConnected)
    {
        std::lock_guard<std::mutex> lock(m_callbackMutex);
        if (m_connectionCallback)
            m_connectionCallback(false);
    }
}

void NamedPipeClient::cleanupConnection()
{
    m_shouldStop.store(true);
    
    // Close pipe to unblock read
    if (m_pipe)
        m_pipe->close();
    
    // Wait for read thread to finish
    if (m_readThread)
    {
        if (m_readThread->joinable())
            m_readThread->join();
        m_readThread.reset();
    }
    
    m_pipe.reset();
}

bool NamedPipeClient::sendMessage(const juce::String& json)
{
    if (!m_connected.load() || !m_pipe)
        return false;
    
    std::lock_guard<std::mutex> lock(m_sendMutex);
    
    // Add newline delimiter
    juce::String message = json + "\n";
    juce::MemoryBlock data(message.toRawUTF8(), message.getNumBytesAsUTF8());
    
    return m_pipe->write(data.getData(), static_cast<int>(data.getSize()), 1000) >= 0;
}

void NamedPipeClient::setMessageCallback(std::function<void(const juce::String&)> callback)
{
    std::lock_guard<std::mutex> lock(m_callbackMutex);
    m_messageCallback = std::move(callback);
}

void NamedPipeClient::setConnectionCallback(std::function<void(bool)> callback)
{
    std::lock_guard<std::mutex> lock(m_callbackMutex);
    m_connectionCallback = std::move(callback);
}

void NamedPipeClient::readLoop()
{
    LOG_INFO("readLoop() started");
    juce::String lineBuffer;
    char buffer[4096];
    
    while (!m_shouldStop.load() && m_pipe)
    {
        // Check if pipe is still open before reading
        if (!m_pipe->isOpen())
        {
            LOG_INFO("Pipe is closed, exiting readLoop");
            break;
        }
        
        int bytesRead = m_pipe->read(buffer, sizeof(buffer) - 1, 500);
        
        if (bytesRead < 0)
        {
            // On Windows, JUCE's NamedPipe::read() may return -1 for both timeout and error
            // Only treat as error if the pipe is actually closed
            if (!m_pipe->isOpen())
            {
                LOG_INFO("Pipe read returned -1 and pipe is closed, disconnecting");
                break;
            }
            
            // Pipe is still open, this is likely just a timeout - continue waiting
            // (JUCE Windows behavior: sometimes returns -1 instead of 0 for timeout)
            continue;
        }
        
        if (bytesRead > 0)
        {
            buffer[bytesRead] = '\0';
            lineBuffer += juce::String::fromUTF8(buffer, bytesRead);
            LOG_DEBUG("Received " + std::to_string(bytesRead) + " bytes");
            
            // Process complete lines
            int newlinePos;
            while ((newlinePos = lineBuffer.indexOf("\n")) >= 0)
            {
                juce::String line = lineBuffer.substring(0, newlinePos);
                lineBuffer = lineBuffer.substring(newlinePos + 1);
                
                if (line.isNotEmpty())
                {
                    LOG_DEBUG("Processing message: " + line.substring(0, 200).toStdString());
                    processMessage(line);
                }
            }
        }
    }
    
    LOG_INFO("readLoop() exiting, m_shouldStop=" + std::to_string(m_shouldStop.load()));
    
    // Notify disconnection only if we weren't asked to stop
    if (!m_shouldStop.load())
    {
        m_connected.store(false);
        std::lock_guard<std::mutex> lock(m_callbackMutex);
        if (m_connectionCallback)
            m_connectionCallback(false);
    }
}

void NamedPipeClient::processMessage(const juce::String& json)
{
    std::lock_guard<std::mutex> lock(m_callbackMutex);
    if (m_messageCallback)
        m_messageCallback(json);
}

// Static helper methods for creating JSON messages

juce::String NamedPipeClient::createConnectMessage(const juce::String& clientId, int sampleRate, int bufferSize)
{
    juce::DynamicObject::Ptr payload = new juce::DynamicObject();
    payload->setProperty("action", "connect");
    payload->setProperty("clientId", clientId);
    payload->setProperty("sampleRate", sampleRate);
    payload->setProperty("bufferSize", bufferSize);
    
    juce::DynamicObject::Ptr message = new juce::DynamicObject();
    message->setProperty("type", "command");
    message->setProperty("id", juce::Uuid().toString());
    message->setProperty("payload", juce::var(payload.get()));
    
    return juce::JSON::toString(juce::var(message.get()), true);
}

juce::String NamedPipeClient::createDisconnectMessage()
{
    juce::DynamicObject::Ptr payload = new juce::DynamicObject();
    payload->setProperty("action", "disconnect");
    
    juce::DynamicObject::Ptr message = new juce::DynamicObject();
    message->setProperty("type", "command");
    message->setProperty("id", juce::Uuid().toString());
    message->setProperty("payload", juce::var(payload.get()));
    
    return juce::JSON::toString(juce::var(message.get()), true);
}

juce::String NamedPipeClient::createGetTrackListMessage()
{
    juce::DynamicObject::Ptr payload = new juce::DynamicObject();
    payload->setProperty("action", "getTrackList");
    
    juce::DynamicObject::Ptr message = new juce::DynamicObject();
    message->setProperty("type", "command");
    message->setProperty("id", juce::Uuid().toString());
    message->setProperty("payload", juce::var(payload.get()));
    
    return juce::JSON::toString(juce::var(message.get()), true);
}

juce::String NamedPipeClient::createSelectTrackMessage(const juce::String& trackId)
{
    juce::DynamicObject::Ptr payload = new juce::DynamicObject();
    payload->setProperty("action", "selectTrack");
    payload->setProperty("trackId", trackId);
    
    juce::DynamicObject::Ptr message = new juce::DynamicObject();
    message->setProperty("type", "command");
    message->setProperty("id", juce::Uuid().toString());
    message->setProperty("payload", juce::var(payload.get()));
    
    return juce::JSON::toString(juce::var(message.get()), true);
}

juce::String NamedPipeClient::createTransportMessage(bool isPlaying, double position)
{
    juce::DynamicObject::Ptr payload = new juce::DynamicObject();
    payload->setProperty("action", "transport");
    payload->setProperty("state", isPlaying ? "play" : "pause");
    payload->setProperty("position", position);
    
    juce::DynamicObject::Ptr message = new juce::DynamicObject();
    message->setProperty("type", "command");
    message->setProperty("id", juce::Uuid().toString());
    message->setProperty("payload", juce::var(payload.get()));
    
    return juce::JSON::toString(juce::var(message.get()), true);
}

juce::String NamedPipeClient::createSeekMessage(double position)
{
    juce::DynamicObject::Ptr payload = new juce::DynamicObject();
    payload->setProperty("action", "seek");
    payload->setProperty("position", position);
    
    juce::DynamicObject::Ptr message = new juce::DynamicObject();
    message->setProperty("type", "command");
    message->setProperty("id", juce::Uuid().toString());
    message->setProperty("payload", juce::var(payload.get()));
    
    return juce::JSON::toString(juce::var(message.get()), true);
}

} // namespace TuneLabBridge
