/*
 * PluginInstance.cpp - Plugin instance implementation
 */

#include "PluginInstance.h"

namespace PluginHost
{

PluginInstance::PluginInstance(std::unique_ptr<juce::AudioPluginInstance> plugin,
                               const juce::PluginDescription& description)
    : plugin(std::move(plugin))
    , description(description)
{
    if (this->plugin)
    {
        this->plugin->addListener(this);
    }
}

PluginInstance::~PluginInstance()
{
    closeEditor();
    
    if (plugin)
    {
        plugin->removeListener(this);
        releaseResources();
    }
}

// ============================================================================
// Plugin Info
// ============================================================================

std::string PluginInstance::getName() const
{
    return description.name.toStdString();
}

std::string PluginInstance::getVendor() const
{
    return description.manufacturerName.toStdString();
}

std::string PluginInstance::getUid() const
{
    return description.createIdentifierString().toStdString();
}

bool PluginInstance::hasEditor() const
{
    return plugin ? plugin->hasEditor() : false;
}

bool PluginInstance::acceptsMidi() const
{
    return plugin ? plugin->acceptsMidi() : false;
}

bool PluginInstance::producesMidi() const
{
    return plugin ? plugin->producesMidi() : false;
}

bool PluginInstance::isSynth() const
{
    return description.isInstrument;
}

int PluginInstance::getNumInputChannels() const
{
    return description.numInputChannels;
}

int PluginInstance::getNumOutputChannels() const
{
    return description.numOutputChannels;
}

// ============================================================================
// Audio Configuration
// ============================================================================

bool PluginInstance::setAudioConfig(double sampleRate, int blockSize,
                                     int numInputChannels, int numOutputChannels)
{
    if (!plugin)
        return false;
    
    // Release if already prepared
    if (isPrepared)
    {
        releaseResources();
    }
    
    currentSampleRate = sampleRate;
    currentBlockSize = blockSize;
    numInputs = numInputChannels;
    numOutputs = numOutputChannels;
    
    return true;
}

void PluginInstance::prepareToPlay()
{
    if (!plugin || isPrepared)
        return;
    
    std::lock_guard<std::mutex> lock(processingMutex);
    
    // Configure plugin bus layout
    juce::AudioProcessor::BusesLayout layout;
    
    // Set up input buses
    if (numInputs > 0)
    {
        layout.inputBuses.add(juce::AudioChannelSet::canonicalChannelSet(numInputs));
    }
    
    // Set up output buses
    if (numOutputs > 0)
    {
        layout.outputBuses.add(juce::AudioChannelSet::canonicalChannelSet(numOutputs));
    }
    
    plugin->setBusesLayout(layout);
    
    // Prepare the plugin
    plugin->prepareToPlay(currentSampleRate, currentBlockSize);
    
    // Allocate audio buffer
    int maxChannels = std::max(numInputs, numOutputs);
    audioBuffer.setSize(maxChannels, currentBlockSize);
    audioBuffer.clear();
    
    isPrepared = true;
}

void PluginInstance::releaseResources()
{
    if (!plugin || !isPrepared)
        return;
    
    std::lock_guard<std::mutex> lock(processingMutex);
    
    plugin->releaseResources();
    audioBuffer.setSize(0, 0);
    
    isPrepared = false;
}

void PluginInstance::reset()
{
    if (!plugin)
        return;
    
    std::lock_guard<std::mutex> lock(processingMutex);
    
    plugin->reset();
    
    // Clear MIDI buffer
    {
        std::lock_guard<std::mutex> midiLock(midiMutex);
        midiBuffer.clear();
        pendingMidiEvents.clear();
    }
}

// ============================================================================
// Audio Processing
// ============================================================================

void PluginInstance::processAudio(const float** inputBuffers, float** outputBuffers,
                                   int numInputChannels, int numOutputChannels, int numSamples)
{
    if (!plugin || !isPrepared)
        return;
    
    std::lock_guard<std::mutex> lock(processingMutex);
    
    // Ensure buffer is large enough
    int maxChannels = std::max(numInputChannels, numOutputChannels);
    if (audioBuffer.getNumChannels() < maxChannels || audioBuffer.getNumSamples() < numSamples)
    {
        audioBuffer.setSize(maxChannels, numSamples, false, false, true);
    }
    
    // Clear the buffer
    audioBuffer.clear();
    
    // Copy input data
    for (int ch = 0; ch < numInputChannels && inputBuffers != nullptr; ++ch)
    {
        if (inputBuffers[ch] != nullptr)
        {
            audioBuffer.copyFrom(ch, 0, inputBuffers[ch], numSamples);
        }
    }
    
    // Process pending MIDI events
    {
        std::lock_guard<std::mutex> midiLock(midiMutex);
        midiBuffer.clear();
        
        for (const auto& event : pendingMidiEvents)
        {
            juce::MidiMessage msg;
            
            if ((event.status & 0xF0) == 0x90) // Note On
            {
                msg = juce::MidiMessage::noteOn(event.channel + 1, event.data1, static_cast<uint8_t>(event.data2));
            }
            else if ((event.status & 0xF0) == 0x80) // Note Off
            {
                msg = juce::MidiMessage::noteOff(event.channel + 1, event.data1, static_cast<uint8_t>(event.data2));
            }
            else if ((event.status & 0xF0) == 0xB0) // Control Change
            {
                msg = juce::MidiMessage::controllerEvent(event.channel + 1, event.data1, event.data2);
            }
            else if ((event.status & 0xF0) == 0xE0) // Pitch Bend
            {
                int value = (event.data2 << 7) | event.data1;
                msg = juce::MidiMessage::pitchWheel(event.channel + 1, value);
            }
            else
            {
                // Generic MIDI message
                msg = juce::MidiMessage(event.status, event.data1, event.data2);
            }
            
            midiBuffer.addEvent(msg, event.sampleOffset);
        }
        
        pendingMidiEvents.clear();
    }
    
    // Process audio
    plugin->processBlock(audioBuffer, midiBuffer);
    
    // Copy output data
    for (int ch = 0; ch < numOutputChannels && outputBuffers != nullptr; ++ch)
    {
        if (outputBuffers[ch] != nullptr)
        {
            if (ch < audioBuffer.getNumChannels())
            {
                juce::FloatVectorOperations::copy(outputBuffers[ch], 
                                                   audioBuffer.getReadPointer(ch), 
                                                   numSamples);
            }
            else
            {
                // Fill with zeros if no output for this channel
                juce::FloatVectorOperations::clear(outputBuffers[ch], numSamples);
            }
        }
    }
}

void PluginInstance::processAudioInterleaved(const float* inputBuffer, float* outputBuffer,
                                              int numInputChannels, int numOutputChannels, int numSamples)
{
    // Allocate temporary deinterleaved buffers
    std::vector<std::vector<float>> inputDeinterleaved(numInputChannels);
    std::vector<std::vector<float>> outputDeinterleaved(numOutputChannels);
    
    std::vector<const float*> inputPtrs(numInputChannels);
    std::vector<float*> outputPtrs(numOutputChannels);
    
    // Deinterleave input
    for (int ch = 0; ch < numInputChannels; ++ch)
    {
        inputDeinterleaved[ch].resize(numSamples);
        inputPtrs[ch] = inputDeinterleaved[ch].data();
        
        if (inputBuffer)
        {
            for (int s = 0; s < numSamples; ++s)
            {
                inputDeinterleaved[ch][s] = inputBuffer[s * numInputChannels + ch];
            }
        }
    }
    
    // Prepare output buffers
    for (int ch = 0; ch < numOutputChannels; ++ch)
    {
        outputDeinterleaved[ch].resize(numSamples);
        outputPtrs[ch] = outputDeinterleaved[ch].data();
    }
    
    // Process
    processAudio(inputPtrs.data(), outputPtrs.data(), numInputChannels, numOutputChannels, numSamples);
    
    // Interleave output
    if (outputBuffer)
    {
        for (int s = 0; s < numSamples; ++s)
        {
            for (int ch = 0; ch < numOutputChannels; ++ch)
            {
                outputBuffer[s * numOutputChannels + ch] = outputDeinterleaved[ch][s];
            }
        }
    }
}

// ============================================================================
// MIDI Processing
// ============================================================================

void PluginInstance::addMidiEvents(const std::vector<InternalMidiEvent>& events)
{
    std::lock_guard<std::mutex> lock(midiMutex);
    pendingMidiEvents.insert(pendingMidiEvents.end(), events.begin(), events.end());
}

void PluginInstance::sendNoteOn(int channel, int note, int velocity, int sampleOffset)
{
    InternalMidiEvent event;
    event.status = 0x90 | (channel & 0x0F);
    event.data1 = static_cast<uint8_t>(note);
    event.data2 = static_cast<uint8_t>(velocity);
    event.channel = static_cast<uint8_t>(channel);
    event.sampleOffset = sampleOffset;
    
    std::lock_guard<std::mutex> lock(midiMutex);
    pendingMidiEvents.push_back(event);
}

void PluginInstance::sendNoteOff(int channel, int note, int velocity, int sampleOffset)
{
    InternalMidiEvent event;
    event.status = 0x80 | (channel & 0x0F);
    event.data1 = static_cast<uint8_t>(note);
    event.data2 = static_cast<uint8_t>(velocity);
    event.channel = static_cast<uint8_t>(channel);
    event.sampleOffset = sampleOffset;
    
    std::lock_guard<std::mutex> lock(midiMutex);
    pendingMidiEvents.push_back(event);
}

void PluginInstance::sendAllNotesOff()
{
    std::lock_guard<std::mutex> lock(midiMutex);
    
    // Send all notes off on all channels
    for (int ch = 0; ch < 16; ++ch)
    {
        InternalMidiEvent event;
        event.status = 0xB0 | ch;  // Control Change
        event.data1 = 123;          // All Notes Off
        event.data2 = 0;
        event.channel = static_cast<uint8_t>(ch);
        event.sampleOffset = 0;
        pendingMidiEvents.push_back(event);
    }
}

void PluginInstance::sendControlChange(int channel, int controller, int value, int sampleOffset)
{
    InternalMidiEvent event;
    event.status = 0xB0 | (channel & 0x0F);
    event.data1 = static_cast<uint8_t>(controller);
    event.data2 = static_cast<uint8_t>(value);
    event.channel = static_cast<uint8_t>(channel);
    event.sampleOffset = sampleOffset;
    
    std::lock_guard<std::mutex> lock(midiMutex);
    pendingMidiEvents.push_back(event);
}

void PluginInstance::sendPitchBend(int channel, int value, int sampleOffset)
{
    InternalMidiEvent event;
    event.status = 0xE0 | (channel & 0x0F);
    event.data1 = static_cast<uint8_t>(value & 0x7F);
    event.data2 = static_cast<uint8_t>((value >> 7) & 0x7F);
    event.channel = static_cast<uint8_t>(channel);
    event.sampleOffset = sampleOffset;
    
    std::lock_guard<std::mutex> lock(midiMutex);
    pendingMidiEvents.push_back(event);
}

// ============================================================================
// Parameters
// ============================================================================

int PluginInstance::getParameterCount() const
{
    if (!plugin)
        return 0;
    
    return plugin->getParameters().size();
}

std::string PluginInstance::getParameterName(int index) const
{
    if (!plugin)
        return "";
    
    auto& params = plugin->getParameters();
    if (index < 0 || index >= params.size())
        return "";
    
    return params[index]->getName(256).toStdString();
}

float PluginInstance::getParameter(int index) const
{
    if (!plugin)
        return 0.0f;
    
    auto& params = plugin->getParameters();
    if (index < 0 || index >= params.size())
        return 0.0f;
    
    return params[index]->getValue();
}

void PluginInstance::setParameter(int index, float value)
{
    if (!plugin)
        return;
    
    auto& params = plugin->getParameters();
    if (index < 0 || index >= params.size())
        return;
    
    params[index]->setValue(value);
}

std::string PluginInstance::getParameterText(int index) const
{
    if (!plugin)
        return "";
    
    auto& params = plugin->getParameters();
    if (index < 0 || index >= params.size())
        return "";
    
    return params[index]->getCurrentValueAsText().toStdString();
}

bool PluginInstance::getParameterInfo(int index,
                                       std::string& name,
                                       std::string& label,
                                       float& defaultValue,
                                       float& minValue,
                                       float& maxValue,
                                       int& numSteps,
                                       bool& isAutomatable,
                                       bool& isDiscrete,
                                       bool& isBoolean) const
{
    if (!plugin)
        return false;
    
    auto& params = plugin->getParameters();
    if (index < 0 || index >= params.size())
        return false;
    
    auto* param = params[index];
    
    name = param->getName(256).toStdString();
    label = param->getLabel().toStdString();
    defaultValue = param->getDefaultValue();
    minValue = 0.0f;  // Normalized range
    maxValue = 1.0f;
    numSteps = param->getNumSteps();
    isAutomatable = param->isAutomatable();
    isDiscrete = param->isDiscrete();
    isBoolean = param->isBoolean();
    
    return true;
}

void PluginInstance::setParameterChangeCallback(ParameterCallback callback)
{
    parameterCallback = callback;
}

// ============================================================================
// Plugin State
// ============================================================================

std::vector<uint8_t> PluginInstance::getState() const
{
    std::vector<uint8_t> result;
    
    if (!plugin)
        return result;
    
    juce::MemoryBlock state;
    plugin->getStateInformation(state);
    
    result.resize(state.getSize());
    std::memcpy(result.data(), state.getData(), state.getSize());
    
    return result;
}

bool PluginInstance::setState(const std::vector<uint8_t>& data)
{
    if (!plugin || data.empty())
        return false;
    
    plugin->setStateInformation(data.data(), static_cast<int>(data.size()));
    return true;
}

int PluginInstance::getPresetCount() const
{
    if (!plugin)
        return 0;
    
    return plugin->getNumPrograms();
}

int PluginInstance::getCurrentPreset() const
{
    if (!plugin)
        return -1;
    
    return plugin->getCurrentProgram();
}

void PluginInstance::setCurrentPreset(int index)
{
    if (!plugin)
        return;
    
    plugin->setCurrentProgram(index);
}

std::string PluginInstance::getPresetName(int index) const
{
    if (!plugin)
        return "";
    
    return plugin->getProgramName(index).toStdString();
}

// ============================================================================
// Editor
// ============================================================================

void* PluginInstance::openEditor(void* parentWindow)
{
    if (!plugin || !plugin->hasEditor())
        return nullptr;
    
    closeEditor();
    
    editor.reset(plugin->createEditor());
    
    if (!editor)
        return nullptr;
    
    editorPtr = editor.get();
    
    // Attach to parent window
    if (parentWindow)
    {
        // Use juce::ComponentPeer::StyleFlags::windowIgnoresMouseClicks to 0
        // to create a regular child window
        editor->addToDesktop(0, parentWindow);
        
        // Position the editor at the top-left corner of the parent
        editor->setTopLeftPosition(0, 0);
    }
    
    editor->setVisible(true);
    
    return editor.get();
}

void PluginInstance::closeEditor()
{
    if (editor)
    {
        editor->setVisible(false);
        editor.reset();
        editorPtr = nullptr;
    }
}

bool PluginInstance::getEditorSize(int& width, int& height) const
{
    if (!editor)
    {
        // Try to get default size from plugin
        if (plugin && plugin->hasEditor())
        {
            auto* tmpEditor = plugin->createEditorIfNeeded();
            if (tmpEditor)
            {
                width = tmpEditor->getWidth();
                height = tmpEditor->getHeight();
                return true;
            }
        }
        return false;
    }
    
    width = editor->getWidth();
    height = editor->getHeight();
    return true;
}

void PluginInstance::setEditorResizeCallback(EditorResizeCallback callback)
{
    editorResizeCallback = callback;
}

// ============================================================================
// Latency
// ============================================================================

int PluginInstance::getLatency() const
{
    if (!plugin)
        return 0;
    
    return plugin->getLatencySamples();
}

double PluginInstance::getTailTime() const
{
    if (!plugin)
        return 0.0;
    
    return plugin->getTailLengthSeconds();
}

// ============================================================================
// AudioProcessorListener
// ============================================================================

void PluginInstance::audioProcessorParameterChanged(juce::AudioProcessor* /*processor*/,
                                                     int parameterIndex,
                                                     float newValue)
{
    if (parameterCallback)
    {
        parameterCallback(parameterIndex, newValue);
    }
}

void PluginInstance::audioProcessorChanged(juce::AudioProcessor* /*processor*/,
                                            const juce::AudioProcessor::ChangeDetails& /*details*/)
{
    // Handle latency changes, etc.
}

} // namespace PluginHost
