#include "NamedPipeClient.h"
#include "../Model/BridgeProtocol.h"

namespace TuneLabBridge
{

NamedPipeClient::NamedPipeClient()
{
}

NamedPipeClient::~NamedPipeClient()
{
    disconnect();
}

bool NamedPipeClient::connect()
{
    if (m_connected.load())
        return true;
    
    m_pipe = std::make_unique<juce::NamedPipe>();
    
    // Try to connect to the TuneLab bridge service
    if (!m_pipe->openExisting(Protocol::PipeName))
    {
        m_pipe.reset();
        return false;
    }
    
    m_connected.store(true);
    m_shouldStop.store(false);
    
    // Start read thread
    m_readThread = std::make_unique<std::thread>(&NamedPipeClient::readLoop, this);
    
    // Notify connection callback
    {
        std::lock_guard<std::mutex> lock(m_callbackMutex);
        if (m_connectionCallback)
            m_connectionCallback(true);
    }
    
    return true;
}

void NamedPipeClient::disconnect()
{
    if (!m_connected.load())
        return;
    
    m_shouldStop.store(true);
    m_connected.store(false);
    
    // Close pipe to unblock read
    if (m_pipe)
        m_pipe->close();
    
    // Wait for read thread
    if (m_readThread && m_readThread->joinable())
        m_readThread->join();
    
    m_readThread.reset();
    m_pipe.reset();
    
    // Notify connection callback
    {
        std::lock_guard<std::mutex> lock(m_callbackMutex);
        if (m_connectionCallback)
            m_connectionCallback(false);
    }
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
    juce::String lineBuffer;
    char buffer[4096];
    
    while (!m_shouldStop.load() && m_pipe)
    {
        int bytesRead = m_pipe->read(buffer, sizeof(buffer) - 1, 100);
        
        if (bytesRead < 0)
        {
            // Pipe error or closed
            if (!m_shouldStop.load())
            {
                m_connected.store(false);
                std::lock_guard<std::mutex> lock(m_callbackMutex);
                if (m_connectionCallback)
                    m_connectionCallback(false);
            }
            break;
        }
        
        if (bytesRead > 0)
        {
            buffer[bytesRead] = '\0';
            lineBuffer += juce::String::fromUTF8(buffer, bytesRead);
            
            // Process complete lines
            int newlinePos;
            while ((newlinePos = lineBuffer.indexOf("\n")) >= 0)
            {
                juce::String line = lineBuffer.substring(0, newlinePos);
                lineBuffer = lineBuffer.substring(newlinePos + 1);
                
                if (line.isNotEmpty())
                    processMessage(line);
            }
        }
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
