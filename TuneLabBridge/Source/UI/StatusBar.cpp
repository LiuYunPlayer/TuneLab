#include "StatusBar.h"

namespace TuneLabBridge
{

StatusBar::StatusBar()
{
}

void StatusBar::paint(juce::Graphics& g)
{
    auto bounds = getLocalBounds();
    
    // Background
    g.setColour(m_backgroundColour);
    g.fillRoundedRectangle(bounds.toFloat(), 4.0f);
    
    // Status indicator
    auto indicatorBounds = bounds.removeFromLeft(24).reduced(6);
    g.setColour(m_connected ? m_connectedColour : m_disconnectedColour);
    g.fillEllipse(indicatorBounds.toFloat());
    
    // Status text
    g.setColour(m_textColour);
    g.setFont(14.0f);
    g.drawText(m_statusText, bounds.reduced(8, 0), juce::Justification::centredLeft);
}

void StatusBar::resized()
{
}

void StatusBar::setConnected(bool connected)
{
    m_connected = connected;
    m_statusText = connected ? "Connected to TuneLab" : "Disconnected";
    repaint();
}

void StatusBar::setStatusText(const juce::String& text)
{
    m_statusText = text;
    repaint();
}

} // namespace TuneLabBridge
