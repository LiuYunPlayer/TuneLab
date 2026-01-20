#pragma once

#include <JuceHeader.h>
#include <string>
#include <vector>

namespace TuneLabBridge
{

/**
 * Track information received from TuneLab.
 */
struct TrackInfo
{
    juce::String id;
    juce::String name;
    juce::String type;  // "midi" or "audio"
    bool isMute = false;
    bool isSolo = false;
    double volume = 1.0;
    double pan = 0.0;
    double duration = 0.0;
    
    /**
     * Parse from JSON object.
     */
    static TrackInfo fromJson(const juce::var& json)
    {
        TrackInfo info;
        if (auto* obj = json.getDynamicObject())
        {
            info.id = obj->getProperty("id").toString();
            info.name = obj->getProperty("name").toString();
            info.type = obj->getProperty("type").toString();
            info.isMute = obj->getProperty("isMute");
            info.isSolo = obj->getProperty("isSolo");
            info.volume = obj->getProperty("volume");
            info.pan = obj->getProperty("pan");
            info.duration = obj->getProperty("duration");
        }
        return info;
    }
    
    /**
     * Convert to JSON object.
     */
    juce::var toJson() const
    {
        auto* obj = new juce::DynamicObject();
        obj->setProperty("id", id);
        obj->setProperty("name", name);
        obj->setProperty("type", type);
        obj->setProperty("isMute", isMute);
        obj->setProperty("isSolo", isSolo);
        obj->setProperty("volume", volume);
        obj->setProperty("pan", pan);
        obj->setProperty("duration", duration);
        return juce::var(obj);
    }
};

/**
 * Parse a list of tracks from JSON array.
 */
inline std::vector<TrackInfo> parseTrackList(const juce::var& json)
{
    std::vector<TrackInfo> tracks;
    if (auto* arr = json.getArray())
    {
        for (const auto& item : *arr)
        {
            tracks.push_back(TrackInfo::fromJson(item));
        }
    }
    return tracks;
}

} // namespace TuneLabBridge
