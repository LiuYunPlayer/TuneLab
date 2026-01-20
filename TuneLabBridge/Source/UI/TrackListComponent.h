#pragma once

#include <JuceHeader.h>
#include "../Model/TrackInfo.h"
#include <vector>

namespace TuneLabBridge
{

/**
 * Track list component for displaying and selecting tracks.
 * This is an alternative to the ComboBox for a more detailed track view.
 */
class TrackListComponent : public juce::Component,
                            public juce::ListBoxModel
{
public:
    TrackListComponent();
    ~TrackListComponent() override = default;
    
    void resized() override;
    
    // ListBoxModel
    int getNumRows() override;
    void paintListBoxItem(int rowNumber, juce::Graphics& g, int width, int height, bool rowIsSelected) override;
    void selectedRowsChanged(int lastRowSelected) override;
    
    // Track management
    void setTracks(const std::vector<TrackInfo>& tracks);
    void clearTracks();
    
    // Selection
    void selectTrack(const juce::String& trackId);
    juce::String getSelectedTrackId() const;
    
    // Callback
    std::function<void(const juce::String&)> onTrackSelected;

private:
    juce::ListBox listBox;
    std::vector<TrackInfo> m_tracks;
    
    juce::Colour m_backgroundColour{0xff1e1e2e};
    juce::Colour m_selectedColour{0xff45475a};
    juce::Colour m_textColour{0xffcdd6f4};
    juce::Colour m_subtextColour{0xff9399b2};
    
    JUCE_DECLARE_NON_COPYABLE_WITH_LEAK_DETECTOR(TrackListComponent)
};

} // namespace TuneLabBridge
