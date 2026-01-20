#pragma once

#include <JuceHeader.h>

namespace TuneLabBridge
{

/**
 * Status bar component showing connection status.
 */
class StatusBar : public juce::Component
{
public:
    StatusBar();
    ~StatusBar() override = default;
    
    void paint(juce::Graphics& g) override;
    void resized() override;
    
    void setConnected(bool connected);
    bool isConnected() const { return m_connected; }
    
    void setStatusText(const juce::String& text);

private:
    bool m_connected = false;
    juce::String m_statusText = "Disconnected";
    
    juce::Colour m_connectedColour{0xff94e2d5};
    juce::Colour m_disconnectedColour{0xfff38ba8};
    juce::Colour m_textColour{0xffcdd6f4};
    juce::Colour m_backgroundColour{0xff313244};
    
    JUCE_DECLARE_NON_COPYABLE_WITH_LEAK_DETECTOR(StatusBar)
};

} // namespace TuneLabBridge
