#pragma once

#include <cstdint>

namespace TuneLabBridge
{

/**
 * Transport state for synchronization with TuneLab.
 */
struct TransportState
{
    bool isPlaying = false;
    double position = 0.0;     // Position in seconds
    double sampleRate = 48000.0;
    double tempo = 120.0;
    
    // Beat/bar information
    double ppqPosition = 0.0;  // Position in quarter notes
    int timeSigNumerator = 4;
    int timeSigDenominator = 4;
    
    /**
     * Gets the position in samples.
     */
    int64_t getPositionInSamples() const
    {
        return static_cast<int64_t>(position * sampleRate);
    }
    
    /**
     * Sets position from sample count.
     */
    void setPositionFromSamples(int64_t samples)
    {
        if (sampleRate > 0)
            position = static_cast<double>(samples) / sampleRate;
    }
    
    /**
     * Advances the position by the given number of samples.
     */
    void advanceBySamples(int numSamples)
    {
        if (sampleRate > 0)
            position += static_cast<double>(numSamples) / sampleRate;
    }
};

} // namespace TuneLabBridge
