#include "PluginProcessor.h"
#include "PluginEditor.h"

namespace TuneLabBridge
{

TuneLabBridgeProcessor::TuneLabBridgeProcessor()
    : AudioProcessor(BusesProperties()
                     .withOutput("Output", juce::AudioChannelSet::stereo(), true))
{
    m_ipcClient = std::make_unique<IPCClient>();
    
    // Set up IPCClient callbacks
    m_ipcClient->onConnected = [this]() {
        m_connected.store(true);
        if (onConnectionChanged)
            onConnectionChanged(true);
    };
    
    m_ipcClient->onDisconnected = [this]() {
        m_connected.store(false);
        if (onConnectionChanged)
            onConnectionChanged(false);
    };
    
    m_ipcClient->onTrackListChanged = [this](const std::vector<TrackInfo>& tracks) {
        {
            std::lock_guard<std::mutex> lock(m_trackListMutex);
            m_trackList = tracks;
        }
        if (onTrackListChanged)
            onTrackListChanged();
    };
}

TuneLabBridgeProcessor::~TuneLabBridgeProcessor()
{
    stopTimer();
    disconnectFromBridge();
}

void TuneLabBridgeProcessor::prepareToPlay(double sampleRate, int samplesPerBlock)
{
    m_currentSampleRate = sampleRate;
    m_currentBlockSize = samplesPerBlock;
    
    // Start timer for transport sync (10ms interval)
    startTimer(10);
}

void TuneLabBridgeProcessor::releaseResources()
{
    stopTimer();
}

void TuneLabBridgeProcessor::processBlock(juce::AudioBuffer<float>& buffer, juce::MidiBuffer&)
{
    juce::ScopedNoDenormals noDenormals;
    
    // Clear any input channels (this is an output-only plugin)
    for (auto i = getTotalNumInputChannels(); i < getTotalNumOutputChannels(); ++i)
        buffer.clear(i, 0, buffer.getNumSamples());
    
    if (!m_connected.load() || !m_ipcClient)
    {
        // Not connected - output silence
        buffer.clear();
        return;
    }
    
    const int numSamples = buffer.getNumSamples();
    
    // Get pointers to output channels
    float* leftChannel = buffer.getWritePointer(0);
    float* rightChannel = buffer.getNumChannels() > 1 ? buffer.getWritePointer(1) : nullptr;
    
    if (rightChannel == nullptr)
    {
        // Mono output - create temp buffer for right channel
        std::vector<float> tempRight(numSamples);
        m_ipcClient->readAudio(leftChannel, tempRight.data(), numSamples);
    }
    else
    {
        // Stereo output
        m_ipcClient->readAudio(leftChannel, rightChannel, numSamples);
    }
}

void TuneLabBridgeProcessor::getStateInformation(juce::MemoryBlock& destData)
{
    // Save state
    juce::DynamicObject::Ptr state = new juce::DynamicObject();
    state->setProperty("selectedTrackId", m_selectedTrackId);
    state->setProperty("transportSync", m_transportSync.load());
    
    juce::String json = juce::JSON::toString(juce::var(state.get()), true);
    destData.append(json.toRawUTF8(), json.getNumBytesAsUTF8());
}

void TuneLabBridgeProcessor::setStateInformation(const void* data, int sizeInBytes)
{
    // Restore state
    juce::String json = juce::String::fromUTF8(static_cast<const char*>(data), sizeInBytes);
    auto parsed = juce::JSON::parse(json);
    
    if (auto* obj = parsed.getDynamicObject())
    {
        m_selectedTrackId = obj->getProperty("selectedTrackId").toString();
        m_transportSync.store(obj->getProperty("transportSync"));
    }
}

juce::AudioProcessorEditor* TuneLabBridgeProcessor::createEditor()
{
    return new TuneLabBridgeEditor(*this);
}

bool TuneLabBridgeProcessor::connectToBridge()
{
    if (m_connected.load())
        return true;
    
    return m_ipcClient->connect(static_cast<int>(m_currentSampleRate), m_currentBlockSize);
}

void TuneLabBridgeProcessor::disconnectFromBridge()
{
    if (!m_connected.load())
        return;
    
    m_ipcClient->disconnect();
}

void TuneLabBridgeProcessor::selectTrack(const juce::String& trackId)
{
    m_selectedTrackId = trackId;
    
    if (m_connected.load())
        m_ipcClient->selectTrack(trackId);
}

juce::String TuneLabBridgeProcessor::getSelectedTrackId() const
{
    return m_selectedTrackId;
}

const std::vector<TrackInfo>& TuneLabBridgeProcessor::getTrackList() const
{
    return m_trackList;
}

void TuneLabBridgeProcessor::refreshTrackList()
{
    if (m_connected.load())
        m_ipcClient->refreshTrackList();
}

void TuneLabBridgeProcessor::timerCallback()
{
    if (m_transportSync.load())
        syncTransportState();
}

void TuneLabBridgeProcessor::syncTransportState()
{
    if (!m_connected.load() || !m_ipcClient)
        return;
    
    auto playHead = getPlayHead();
    if (!playHead)
        return;
    
    auto posInfo = playHead->getPosition();
    if (!posInfo)
        return;
    
    bool isPlaying = posInfo->getIsPlaying();
    double position = 0.0;
    
    if (auto timeInSeconds = posInfo->getTimeInSeconds())
        position = *timeInSeconds;
    
    // Check if state changed
    if (isPlaying != m_lastPlayState || std::abs(position - m_lastPosition) > 0.05)
    {
        m_lastPlayState = isPlaying;
        m_lastPosition = position;
        
        m_ipcClient->sendTransportState(isPlaying, position);
    }
}

} // namespace TuneLabBridge

// Plugin factory function
juce::AudioProcessor* JUCE_CALLTYPE createPluginFilter()
{
    return new TuneLabBridge::TuneLabBridgeProcessor();
}
