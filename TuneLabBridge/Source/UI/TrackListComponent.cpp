#include "TrackListComponent.h"

namespace TuneLabBridge
{

TrackListComponent::TrackListComponent()
{
    listBox.setModel(this);
    listBox.setColour(juce::ListBox::backgroundColourId, m_backgroundColour);
    addAndMakeVisible(listBox);
}

void TrackListComponent::resized()
{
    listBox.setBounds(getLocalBounds());
}

int TrackListComponent::getNumRows()
{
    return static_cast<int>(m_tracks.size());
}

void TrackListComponent::paintListBoxItem(int rowNumber, juce::Graphics& g, int width, int height, bool rowIsSelected)
{
    if (rowNumber < 0 || rowNumber >= static_cast<int>(m_tracks.size()))
        return;
    
    const auto& track = m_tracks[rowNumber];
    
    // Background
    if (rowIsSelected)
        g.fillAll(m_selectedColour);
    
    // Track name
    g.setColour(m_textColour);
    g.setFont(14.0f);
    g.drawText(track.name, 10, 2, width - 80, 18, juce::Justification::centredLeft);
    
    // Track type badge
    g.setColour(m_subtextColour);
    g.setFont(11.0f);
    juce::String typeStr = track.type == "midi" ? "MIDI" : "Audio";
    g.drawText(typeStr, width - 60, 2, 50, 18, juce::Justification::centredRight);
}

void TrackListComponent::selectedRowsChanged(int lastRowSelected)
{
    if (lastRowSelected >= 0 && lastRowSelected < static_cast<int>(m_tracks.size()))
    {
        if (onTrackSelected)
            onTrackSelected(m_tracks[lastRowSelected].id);
    }
}

void TrackListComponent::setTracks(const std::vector<TrackInfo>& tracks)
{
    m_tracks = tracks;
    listBox.updateContent();
    listBox.repaint();
}

void TrackListComponent::clearTracks()
{
    m_tracks.clear();
    listBox.updateContent();
    listBox.repaint();
}

void TrackListComponent::selectTrack(const juce::String& trackId)
{
    for (size_t i = 0; i < m_tracks.size(); ++i)
    {
        if (m_tracks[i].id == trackId)
        {
            listBox.selectRow(static_cast<int>(i));
            return;
        }
    }
    listBox.deselectAllRows();
}

juce::String TrackListComponent::getSelectedTrackId() const
{
    int selected = listBox.getSelectedRow();
    if (selected >= 0 && selected < static_cast<int>(m_tracks.size()))
        return m_tracks[selected].id;
    return {};
}

} // namespace TuneLabBridge
