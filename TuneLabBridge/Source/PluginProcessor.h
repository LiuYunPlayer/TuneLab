#pragma once

#include <JuceHeader.h>
#include "IPC/IPCClient.h"
#include "Model/TrackInfo.h"
#include "Model/TransportState.h"

#include <memory>
#include <vector>
#include <atomic>

namespace TuneLabBridge
{

/**
 * Main audio processor for the TuneLab Bridge plugin.
 * Receives audio from TuneLab via shared memory and outputs to the DAW.
 */
class TuneLabBridgeProcessor : public juce::AudioProcessor,
                                public juce::Timer
{
public:
    TuneLabBridgeProcessor();
    ~TuneLabBridgeProcessor() override;

    // AudioProcessor interface
    void prepareToPlay(double sampleRate, int samplesPerBlock) override;
    void releaseResources() override;
    void processBlock(juce::AudioBuffer<float>&, juce::MidiBuffer&) override;

    // State persistence
    void getStateInformation(juce::MemoryBlock& destData) override;
    void setStateInformation(const void* data, int sizeInBytes) override;

    // Editor
    juce::AudioProcessorEditor* createEditor() override;
    bool hasEditor() const override { return true; }

    // Plugin info
    const juce::String getName() const override { return "TuneLab Bridge"; }
    bool acceptsMidi() const override { return false; }
    bool producesMidi() const override { return false; }
    bool isMidiEffect() const override { return false; }
    double getTailLengthSeconds() const override { return 0.0; }
    
    // Programs
    int getNumPrograms() override { return 1; }
    int getCurrentProgram() override { return 0; }
    void setCurrentProgram(int) override {}
    const juce::String getProgramName(int) override { return {}; }
    void changeProgramName(int, const juce::String&) override {}

    // Bridge API
    bool connectToBridge();
    void disconnectFromBridge();
    bool isConnected() const { return m_connected.load(); }
    
    void selectTrack(const juce::String& trackId);
    juce::String getSelectedTrackId() const;
    
    const std::vector<TrackInfo>& getTrackList() const;
    void refreshTrackList();
    
    // Transport sync
    void setTransportSyncEnabled(bool enabled) { m_transportSync.store(enabled); }
    bool isTransportSyncEnabled() const { return m_transportSync.load(); }

    // Callbacks for UI updates
    std::function<void()> onTrackListChanged;
    std::function<void(bool)> onConnectionChanged;

private:
    void timerCallback() override;
    void syncTransportState();

    std::unique_ptr<IPCClient> m_ipcClient;
    
    std::atomic<bool> m_connected{false};
    std::atomic<bool> m_transportSync{true};
    
    // Cached track list for thread-safe access
    mutable std::mutex m_trackListMutex;
    std::vector<TrackInfo> m_trackList;
    juce::String m_selectedTrackId;
    
    // Transport state tracking
    bool m_lastPlayState = false;
    double m_lastPosition = 0.0;
    
    // Audio processing
    double m_currentSampleRate = 48000.0;
    int m_currentBlockSize = 512;
    
    JUCE_DECLARE_NON_COPYABLE_WITH_LEAK_DETECTOR(TuneLabBridgeProcessor)
};

} // namespace TuneLabBridge
