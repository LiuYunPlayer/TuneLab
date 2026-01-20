#include "PluginEditor.h"

namespace TuneLabBridge
{

TuneLabBridgeEditor::TuneLabBridgeEditor(TuneLabBridgeProcessor& p)
    : AudioProcessorEditor(&p)
    , processor(p)
    , m_safetyFlag(std::make_shared<bool>(true))
{
    // Set up title
    titleLabel.setText("TuneLab Bridge", juce::dontSendNotification);
    titleLabel.setFont(juce::Font(20.0f, juce::Font::bold));
    titleLabel.setColour(juce::Label::textColourId, primaryColour);
    titleLabel.setJustificationType(juce::Justification::centred);
    addAndMakeVisible(titleLabel);
    
    // Set up status bar
    addAndMakeVisible(statusBar);
    
    // Set up track label and selector
    trackLabel.setText("Track:", juce::dontSendNotification);
    trackLabel.setColour(juce::Label::textColourId, textColour);
    addAndMakeVisible(trackLabel);
    
    trackSelector.addListener(this);
    trackSelector.setColour(juce::ComboBox::backgroundColourId, backgroundColour.brighter(0.1f));
    trackSelector.setColour(juce::ComboBox::textColourId, textColour);
    addAndMakeVisible(trackSelector);
    
    // Set up transport sync toggle
    transportSyncButton.setButtonText("Transport Sync");
    transportSyncButton.setToggleState(processor.isTransportSyncEnabled(), juce::dontSendNotification);
    transportSyncButton.setColour(juce::ToggleButton::textColourId, textColour);
    transportSyncButton.setColour(juce::ToggleButton::tickColourId, primaryColour);
    transportSyncButton.onClick = [this]() {
        processor.setTransportSyncEnabled(transportSyncButton.getToggleState());
    };
    addAndMakeVisible(transportSyncButton);
    
    // Set up connect button
    connectButton.setButtonText("Connect");
    connectButton.addListener(this);
    connectButton.setColour(juce::TextButton::buttonColourId, primaryColour);
    connectButton.setColour(juce::TextButton::textColourOffId, backgroundColour);
    addAndMakeVisible(connectButton);
    
    // Set up refresh button
    refreshButton.setButtonText("Refresh");
    refreshButton.addListener(this);
    refreshButton.setColour(juce::TextButton::buttonColourId, backgroundColour.brighter(0.2f));
    refreshButton.setColour(juce::TextButton::textColourOffId, textColour);
    addAndMakeVisible(refreshButton);
    
    // Set up callbacks from processor with safety check
    // Capture a weak copy of the safety flag to detect if editor was destroyed
    auto safetyFlagWeak = std::weak_ptr<bool>(m_safetyFlag);
    
    processor.onConnectionChanged = [this, safetyFlagWeak](bool) {
        juce::MessageManager::callAsync([this, safetyFlagWeak]() {
            // Check if editor still exists
            if (auto flag = safetyFlagWeak.lock())
            {
                if (*flag)
                    updateConnectionStatus();
            }
        });
    };
    
    processor.onTrackListChanged = [this, safetyFlagWeak]() {
        juce::MessageManager::callAsync([this, safetyFlagWeak]() {
            // Check if editor still exists
            if (auto flag = safetyFlagWeak.lock())
            {
                if (*flag)
                    updateTrackList();
            }
        });
    };
    
    // Initial state
    updateConnectionStatus();
    updateTrackList();
    
    // Set window size
    setSize(400, 250);
}

TuneLabBridgeEditor::~TuneLabBridgeEditor()
{
    // Mark as destroyed so pending async callbacks don't access invalid memory
    *m_safetyFlag = false;
    
    processor.onConnectionChanged = nullptr;
    processor.onTrackListChanged = nullptr;
}

void TuneLabBridgeEditor::paint(juce::Graphics& g)
{
    g.fillAll(backgroundColour);
    
    // Draw border
    g.setColour(primaryColour.withAlpha(0.3f));
    g.drawRect(getLocalBounds(), 1);
}

void TuneLabBridgeEditor::resized()
{
    auto bounds = getLocalBounds().reduced(20);
    
    // Title at top
    titleLabel.setBounds(bounds.removeFromTop(30));
    bounds.removeFromTop(10);
    
    // Status bar
    statusBar.setBounds(bounds.removeFromTop(24));
    bounds.removeFromTop(15);
    
    // Track selection row
    auto trackRow = bounds.removeFromTop(30);
    trackLabel.setBounds(trackRow.removeFromLeft(50));
    trackRow.removeFromLeft(10);
    refreshButton.setBounds(trackRow.removeFromRight(70));
    trackRow.removeFromRight(10);
    trackSelector.setBounds(trackRow);
    bounds.removeFromTop(15);
    
    // Transport sync toggle
    transportSyncButton.setBounds(bounds.removeFromTop(24));
    bounds.removeFromTop(15);
    
    // Connect button at bottom
    auto buttonWidth = 120;
    auto buttonX = (bounds.getWidth() - buttonWidth) / 2;
    connectButton.setBounds(bounds.getX() + buttonX, bounds.getY(), buttonWidth, 35);
}

void TuneLabBridgeEditor::buttonClicked(juce::Button* button)
{
    if (button == &connectButton)
    {
        if (processor.isConnected())
            processor.disconnectFromBridge();
        else
            processor.connectToBridge();
    }
    else if (button == &refreshButton)
    {
        processor.refreshTrackList();
    }
}

void TuneLabBridgeEditor::comboBoxChanged(juce::ComboBox* comboBox)
{
    if (comboBox == &trackSelector)
    {
        int selectedId = trackSelector.getSelectedId();
        if (selectedId == 1)
        {
            // Master output
            processor.selectTrack("");
        }
        else if (selectedId > 1)
        {
            const auto& tracks = processor.getTrackList();
            int index = selectedId - 2;
            if (index >= 0 && index < static_cast<int>(tracks.size()))
            {
                processor.selectTrack(tracks[index].id);
            }
        }
    }
}

void TuneLabBridgeEditor::updateConnectionStatus()
{
    bool connected = processor.isConnected();
    
    statusBar.setConnected(connected);
    connectButton.setButtonText(connected ? "Disconnect" : "Connect");
    
    trackSelector.setEnabled(connected);
    refreshButton.setEnabled(connected);
}

void TuneLabBridgeEditor::updateTrackList()
{
    trackSelector.clear();
    
    // Add master output option
    trackSelector.addItem("Master Output", 1);
    
    // Add tracks
    const auto& tracks = processor.getTrackList();
    int id = 2;
    for (const auto& track : tracks)
    {
        juce::String itemText = track.name;
        if (track.type == "midi")
            itemText += " [MIDI]";
        else
            itemText += " [Audio]";
        
        trackSelector.addItem(itemText, id++);
    }
    
    // Select current track
    juce::String selectedId = processor.getSelectedTrackId();
    if (selectedId.isEmpty())
    {
        trackSelector.setSelectedId(1);
    }
    else
    {
        int index = 0;
        for (const auto& track : tracks)
        {
            if (track.id == selectedId)
            {
                trackSelector.setSelectedId(index + 2);
                break;
            }
            index++;
        }
    }
}

} // namespace TuneLabBridge
