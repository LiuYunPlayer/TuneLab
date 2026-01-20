#pragma once

#include <JuceHeader.h>
#include "PluginProcessor.h"
#include "UI/TrackListComponent.h"
#include "UI/StatusBar.h"

namespace TuneLabBridge
{

/**
 * Plugin editor (GUI) for TuneLab Bridge.
 * Provides track selection, connection status, and transport sync controls.
 */
class TuneLabBridgeEditor : public juce::AudioProcessorEditor,
                             public juce::Button::Listener,
                             public juce::ComboBox::Listener
{
public:
    explicit TuneLabBridgeEditor(TuneLabBridgeProcessor&);
    ~TuneLabBridgeEditor() override;

    void paint(juce::Graphics&) override;
    void resized() override;
    
    // Button::Listener
    void buttonClicked(juce::Button* button) override;
    
    // ComboBox::Listener
    void comboBoxChanged(juce::ComboBox* comboBox) override;

private:
    void updateConnectionStatus();
    void updateTrackList();

    TuneLabBridgeProcessor& processor;
    
    // UI Components
    juce::Label titleLabel;
    StatusBar statusBar;
    
    juce::Label trackLabel;
    juce::ComboBox trackSelector;
    
    juce::ToggleButton transportSyncButton;
    juce::TextButton connectButton;
    juce::TextButton refreshButton;
    
    // Colors
    juce::Colour backgroundColour{0xff1e1e2e};
    juce::Colour primaryColour{0xff89b4fa};
    juce::Colour textColour{0xffcdd6f4};
    
    JUCE_DECLARE_NON_COPYABLE_WITH_LEAK_DETECTOR(TuneLabBridgeEditor)
};

} // namespace TuneLabBridge
