/*
 * PluginInstance.h - Individual plugin instance wrapper
 */

#ifndef PLUGIN_INSTANCE_H
#define PLUGIN_INSTANCE_H

#include <juce_audio_processors/juce_audio_processors.h>
#include <memory>
#include <string>
#include <mutex>
#include <vector>
#include <functional>

namespace PluginHost
{

/**
 * Parameter change callback type
 */
using ParameterCallback = std::function<void(int paramIndex, float value)>;

/**
 * Editor resize callback type
 */
using EditorResizeCallback = std::function<void(int width, int height)>;

/**
 * MIDI event structure for internal use
 */
struct InternalMidiEvent
{
    int sampleOffset;
    uint8_t status;
    uint8_t data1;
    uint8_t data2;
    uint8_t channel;
};

/**
 * PluginInstance - Wrapper for a loaded plugin
 */
class PluginInstance : public juce::AudioProcessorListener
{
public:
    /**
     * Constructor
     * @param plugin The JUCE plugin instance to wrap
     * @param description The plugin description
     */
    PluginInstance(std::unique_ptr<juce::AudioPluginInstance> plugin,
                   const juce::PluginDescription& description);
    
    /**
     * Destructor
     */
    ~PluginInstance() override;
    
    // ========================================================================
    // Plugin Info
    // ========================================================================
    
    /**
     * Get the plugin name
     */
    std::string getName() const;
    
    /**
     * Get the plugin vendor
     */
    std::string getVendor() const;
    
    /**
     * Get the unique identifier
     */
    std::string getUid() const;
    
    /**
     * Check if plugin has an editor GUI
     */
    bool hasEditor() const;
    
    /**
     * Check if plugin accepts MIDI
     */
    bool acceptsMidi() const;
    
    /**
     * Check if plugin produces MIDI
     */
    bool producesMidi() const;
    
    /**
     * Check if plugin is a synthesizer
     */
    bool isSynth() const;
    
    /**
     * Get input channel count
     */
    int getNumInputChannels() const;
    
    /**
     * Get output channel count
     */
    int getNumOutputChannels() const;
    
    // ========================================================================
    // Audio Configuration
    // ========================================================================
    
    /**
     * Set the audio configuration
     */
    bool setAudioConfig(double sampleRate, int blockSize,
                        int numInputs, int numOutputs);
    
    /**
     * Get current sample rate
     */
    double getSampleRate() const { return currentSampleRate; }
    
    /**
     * Get current block size
     */
    int getBlockSize() const { return currentBlockSize; }
    
    /**
     * Prepare the plugin for playback
     */
    void prepareToPlay();
    
    /**
     * Release processing resources
     */
    void releaseResources();
    
    /**
     * Reset the plugin state
     */
    void reset();
    
    // ========================================================================
    // Audio Processing
    // ========================================================================
    
    /**
     * Process audio (non-interleaved)
     */
    void processAudio(const float** inputBuffers, float** outputBuffers,
                      int numInputChannels, int numOutputChannels, int numSamples);
    
    /**
     * Process audio (interleaved)
     */
    void processAudioInterleaved(const float* inputBuffer, float* outputBuffer,
                                  int numInputChannels, int numOutputChannels, int numSamples);
    
    // ========================================================================
    // MIDI Processing
    // ========================================================================
    
    /**
     * Add MIDI events to be processed in the next audio block
     */
    void addMidiEvents(const std::vector<InternalMidiEvent>& events);
    
    /**
     * Send note on
     */
    void sendNoteOn(int channel, int note, int velocity, int sampleOffset);
    
    /**
     * Send note off
     */
    void sendNoteOff(int channel, int note, int velocity, int sampleOffset);
    
    /**
     * Send all notes off
     */
    void sendAllNotesOff();
    
    /**
     * Send control change
     */
    void sendControlChange(int channel, int controller, int value, int sampleOffset);
    
    /**
     * Send pitch bend
     */
    void sendPitchBend(int channel, int value, int sampleOffset);
    
    // ========================================================================
    // Parameters
    // ========================================================================
    
    /**
     * Get parameter count
     */
    int getParameterCount() const;
    
    /**
     * Get parameter name
     */
    std::string getParameterName(int index) const;
    
    /**
     * Get parameter value (normalized 0-1)
     */
    float getParameter(int index) const;
    
    /**
     * Set parameter value (normalized 0-1)
     */
    void setParameter(int index, float value);
    
    /**
     * Get parameter as text
     */
    std::string getParameterText(int index) const;
    
    /**
     * Get parameter info
     */
    bool getParameterInfo(int index, 
                          std::string& name, 
                          std::string& label,
                          float& defaultValue, 
                          float& minValue, 
                          float& maxValue,
                          int& numSteps,
                          bool& isAutomatable,
                          bool& isDiscrete,
                          bool& isBoolean) const;
    
    /**
     * Set parameter change callback
     */
    void setParameterChangeCallback(ParameterCallback callback);
    
    // ========================================================================
    // Plugin State
    // ========================================================================
    
    /**
     * Get the plugin state as binary data
     */
    std::vector<uint8_t> getState() const;
    
    /**
     * Set the plugin state from binary data
     */
    bool setState(const std::vector<uint8_t>& data);
    
    /**
     * Get preset count
     */
    int getPresetCount() const;
    
    /**
     * Get current preset index
     */
    int getCurrentPreset() const;
    
    /**
     * Set current preset
     */
    void setCurrentPreset(int index);
    
    /**
     * Get preset name
     */
    std::string getPresetName(int index) const;
    
    // ========================================================================
    // Editor
    // ========================================================================
    
    /**
     * Open the plugin editor
     * @param parentWindow Native window handle (HWND, NSView*, etc.)
     * @return Editor handle or nullptr on failure
     */
    void* openEditor(void* parentWindow);
    
    /**
     * Close the plugin editor
     */
    void closeEditor();
    
    /**
     * Get editor size
     */
    bool getEditorSize(int& width, int& height) const;
    
    /**
     * Set editor resize callback
     */
    void setEditorResizeCallback(EditorResizeCallback callback);
    
    // ========================================================================
    // Latency
    // ========================================================================
    
    /**
     * Get plugin latency in samples
     */
    int getLatency() const;
    
    /**
     * Get tail time in seconds
     */
    double getTailTime() const;
    
    // ========================================================================
    // AudioProcessorListener overrides
    // ========================================================================
    
    void audioProcessorParameterChanged(juce::AudioProcessor* processor,
                                        int parameterIndex,
                                        float newValue) override;
    
    void audioProcessorChanged(juce::AudioProcessor* processor,
                               const juce::AudioProcessor::ChangeDetails& details) override;
    
private:
    // The wrapped plugin instance
    std::unique_ptr<juce::AudioPluginInstance> plugin;
    juce::PluginDescription description;
    
    // Audio configuration
    double currentSampleRate = 44100.0;
    int currentBlockSize = 512;
    int numInputs = 2;
    int numOutputs = 2;
    bool isPrepared = false;
    
    // Audio buffer for processing
    juce::AudioBuffer<float> audioBuffer;
    
    // MIDI buffer
    juce::MidiBuffer midiBuffer;
    mutable std::mutex midiMutex;
    std::vector<InternalMidiEvent> pendingMidiEvents;
    
    // Editor
    std::unique_ptr<juce::AudioProcessorEditor> editor;
    juce::Component::SafePointer<juce::AudioProcessorEditor> editorPtr;
    
    // Callbacks
    ParameterCallback parameterCallback;
    EditorResizeCallback editorResizeCallback;
    
    // Processing lock
    mutable std::mutex processingMutex;
};

} // namespace PluginHost

#endif // PLUGIN_INSTANCE_H
